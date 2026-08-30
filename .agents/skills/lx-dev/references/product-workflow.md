# 产品工作流

## 创建游戏

在干净框架基线上运行：

```powershell
.\lx.ps1 create game MyGame
.\lx.ps1 validate
```

命令在 `godot_project/` 内创建 `script/MyGame/AGENTS.md`、`script/MyGame/GameRoot.cs` 和初始世界场景，把产品根目录与根命名空间写入 `content/game/game-manifest.json`，并生成 `GameCatalog` 与 `WorldCatalog`。产品目录规则会随新游戏一起就位，不需要另行复制提示词。

按根仓库不变量先完成框架能力映射。落地时从注入上下文和生成目录定位 UI、资源、场景、Feature、输入、音频、内容、对象池或诊断入口；产品类型只承载游戏语义。框架确实缺少能力时，把缺口与产品临时方案分开说明。

## 添加结构单元

```powershell
.\lx.ps1 create world Dungeon dungeon
.\lx.ps1 create feature Player player
.\lx.ps1 create node PlayerBody CharacterBody2D player_body
.\lx.ps1 create screen MainMenu main_menu
.\lx.ps1 create content Item items
.\lx.ps1 create input Jump game_jump Space
.\lx.ps1 create res player_sprite Texture2D res://content/art/player.png Cached
```

`create node` 用于不能继承 `LXNode` 的任意 Godot 原生节点，生成 C# `partial` 类、同类型场景和显式 `ILXContextReceiver` 实现；其余结构命令会更新对应事实源清单并刷新类型化目录。所有命令都会校验名称和重复路径。脚手架完成后，只编辑产品源码、场景和清单，不编辑生成目录或绑定文件。

命令的第二个可选参数是稳定 ID，也是场景文件名：`create feature HangarFeature hangar` 生成 `scene/features/hangar.tscn`，`create screen HangarScreen hangar` 生成 `scene/ui/hangar.tscn`。参数不确定时只运行 `./lx.ps1 create --help` 或子命令 `--help`，不读脚手架源码。成功返回的 `create` 已完成存在性和重复校验；除非后续需要编辑产物，不再用 `git status`、`rg --files` 或重读 manifest 反复确认。

参数已经由用户完整给出时直接走批量快路径：只读取各已知写入目标最近的局部 `AGENTS.md`，按请求顺序运行全部 `create` 命令，然后把命令回显的变更路径一次传给 `check`，最后运行 `validate`。此路径不需要 `inspect`、`--help`、预读现有 manifest/场景、生成后枚举或逐个重读产物；若用户只要求脚手架存在，也不补写空实现。

### 注册并使用动态资源

产品代码要求使用的资源，无论其 `res://` 路径是否位于框架目录，都直接用 `create res`；命令会添加 `Product` scope 并生成 `LX.Generated.ResCatalog` 的强类型属性，不手改 manifest 或生成目录。例如：

```powershell
.\lx.ps1 create res framework_entry PackedScene res://scene/main.tscn Cached
```

在 `LXNode`/`UIScreen` 中只需读要编辑的产品文件，通过注入上下文取得并让 `Lifetime` 持有租约：

```csharp
using Godot;
using LX.Generated;

private PackedScene? _frameworkEntry;

protected override void OnLXInitialized()
{
    _frameworkEntry = Lifetime.Own(LX.Res.Acquire(ResCatalog.FrameworkEntry)).Resource;
}
```

`create res` 会直接回显生成的完整符号；`ResCatalog.<Property>` 由 snake_case ID 稳定转为 PascalCase，无需搜索或读取生成 catalog，也无需搜索 `AssetLease`、`LifetimeScope` 或验证场景源码来猜测用法。只有框架维护者在添加框架内建资源时才手动使用 `Framework` scope。

当任务只是“注册一个已给出路径的资源，并让某个已知产品文件通过生成引用获取它”时，最短流程固定为：读取 `godot_project/AGENTS.md` 与该产品目录最近的 `AGENTS.md`，运行一次 `create res`，读取并修改目标产品文件，随后一次 `check` 和一次 `validate`。不运行 `inspect`、禁用 API 全目录扫描、产品目录枚举、`git status`，也不额外读取 `resource-lifecycle.md`；上面的 `Lifetime.Own(LX.Res.Acquire(...)).Resource` 已是该场景的完整所有权契约。

## 使用生成入口

`LXNode` 和 `UIScreen` 中的 `LX` 是被注入的上下文，不是全局服务定位器：

```csharp
var items = LX.Content.Load(MyGame.Generated.ContentCatalog.Items);
await LX.Scenes.ChangeAsync(WorldCatalog.Dungeon.Id, Lifetime.Token);
await using var player = await LX.Features.SpawnAsync(
    FeatureCatalog.Player.Id,
    this,
    Lifetime,
    Lifetime.Token);
var menu = await LX.UI.NavigateAsync(UICatalog.MainMenu.Id, parentLifetime: Lifetime);
using var texture = Lifetime.Own(LX.Res.Acquire(ResCatalog.PlayerSprite));
var material = AssetBinding<Material>.Create(
    LX.Res,
    Lifetime,
    value => sprite.Material = value);
await material.SetAsync(ResCatalog.PlayerMaterial, Lifetime.Token);
```

目录属性名由清单 ID 生成。创建命令会回显新符号；需要跨模块概览时运行 `inspect`，它会直接输出产品根、服务名和 catalog 计数。只有摘要不足时才读命令回显的绝对 `project-index.json` 路径。

根规则要求池化的节点直接使用 `NodePool<TNode>`。池由所属 Feature 或世界的 `Lifetime` 持有；短作用域优先使用 `RentLease`，动态集合在所属流程收口时逐一 `Return`。

一次性动态场景使用 `PackedSceneInstance<TNode>`；动态 UI 图片和 Godot `AtlasTexture` 使用 `UIScreen.BindTexture`。材质、字体、Shader、Mesh 或自定义 `Resource` 的动态替换使用 `AssetBinding<T>`。所有入口都复用 `LX.Res`，具体所有权与闭环断言读取 `resource-lifecycle.md`。

重开闭环采样放在 UI 与 Feature handle 退出作用域之后，通过 `LX.Diagnostics.Snapshot()` 取得框架所有权事实；明确区分稳定缓存与活跃租约。需要交付安装包时，再用 `export <platform>` 运行包内产品 smoke，确认生成数据和非 Godot 原生文件确实进入产物。

## 必需循环

```powershell
# 只有需要跨模块结构概览时运行 .\lx.ps1 inspect
# 编辑事实源与产品源码
.\lx.ps1 check <changed-path> [...]
.\lx.ps1 validate
```

产品可在 `content/game/game-manifest.json` 的 `productSmokes` 声明快速、确定、会自行退出的业务 smoke；Debug 与导出共用 ID、参数、marker 和 timeout。旧字段 `exportSmokes` 只用于兼容，不能与 `productSmokes` 同时声明：

```json
{
  "productSmokes": [
    {
      "id": "restart_cycle",
      "argument": "--game-smoke-restart",
      "successMarker": "GAME_RESTART_SMOKE_PASS",
      "timeoutSeconds": 30
    }
  ],
  "visualTargets": [
    {
      "id": "battle_hud",
      "scenePath": "res://scene/validation/battle_hud_fixture.tscn",
      "baselinePath": "tests/Visual/Baselines/battle_hud.png",
      "width": 1280,
      "height": 720
    }
  ]
}
```

```powershell
.\lx.ps1 smoke product all
```

有状态的玩法、UI 导航或重开任务在可持续运行的 Editor/Debug 会话中，再按需读取 `runtime snapshot ui|features|resources|input|metrics`；业务关键状态用现有 Metrics/结构化日志暴露。产品视觉夹具通过 `visualTargets` 注册并用 `visual compare product` 比较，只有人工确认设计变化后才能 approve。`validate` 会运行已声明的产品 smoke 和视觉目标；未声明时明确跳过。

以“全面展示/炫技/验证框架 API”为目标时，先以 `api/LXFramework.PublicApi.txt` 作为公开面清单，用 `inspect --product-coverage` 查看服务级静态映射，再把能力分成主玩法自然使用、Framework Lab 独立展示、product smoke 自动验证和不适用四类。不要为追求表面 100% 覆盖把互斥策略、危险维护入口或与玩法无关的低层 API 硬塞进主流程；对声称已覆盖的能力必须给出可运行场景、断言或快照证据。

`check` 会在事实源需要时先运行 Luban 或现有生成器，再执行最小去重检查组合；冷启动工作区缺少忽略的 `.lx/luban/report.json` 时也会自动补建，不需要手工 `data` 后重试。`validate` 是最终门禁，覆盖固定版本 Luban 转表、架构与 API 注释边界、清单、生成漂移、编译、纯核心测试、Godot 无窗口场景矩阵和 UI 视觉基准。
