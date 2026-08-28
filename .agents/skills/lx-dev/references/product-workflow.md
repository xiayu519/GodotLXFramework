# 产品工作流

## 创建游戏

在干净框架基线上运行：

```powershell
.\lx.ps1 create game MyGame
.\lx.ps1 validate
```

命令在 `godot_project/` 内创建 `script/MyGame/AGENTS.md`、`script/MyGame/GameRoot.cs` 和初始世界场景，把产品根目录与根命名空间写入 `content/game/game-manifest.json`，并生成 `GameCatalog` 与 `WorldCatalog`。产品目录规则会随新游戏一起就位，不需要另行复制提示词。

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

## Luban 策划数据

跨表引用、策划 schema 或批量配置使用外层 `game_design/`。schema 使用可审查的 XML，源数据使用 JSON；工具版本和提交由 `game_design/toolchain.json` 固定。Windows 人工转表可双击 `game_design/build.bat`，命令行和 Codex 使用统一入口：

```powershell
.\lx.ps1 data
```

有产品层时，命令以 Luban `cs-bin` 生成强类型代码到产品根目录 `Generated/Luban/`，把 `.bytes` 二进制表写入 `content/data/luban/`。运行时只通过现有内容服务构造 Luban 表集合：

```csharp
var tables = LX.Content.LoadLubanTables(loader => new GameData.Tables(loader));
var probe = tables.TbDesignProbe.Get("lx_framework");
```

`LX.Content` 通过 Godot `FileAccess` 读取 `.bytes` 并交给 Luban `ByteBuf`。不手改 Luban 输出，不创建静态 `Tables` 单例。普通小型 JSON 表仍可使用 `lx create content`；两种入口共享 `LX.Content`，不建立第二套内容服务。

`data` 必须生成两次并得到相同输出哈希、编译隔离的生成 C#，并确认缺失跨表引用 fixture 被 Luban 拒绝；验证事实记录在 `.lx/luban/report.json`。新增 schema 类型时至少覆盖相应字段形态和一个负向数据错误。

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
```

目录属性名由清单 ID 生成。需要准确名称时读取生成目录，或运行 `inspect` 后查看 `.lx/project-index.json`。

## 必需循环

```powershell
# 只有需要跨模块结构概览时运行 .\lx.ps1 inspect
# 编辑事实源与产品源码
.\lx.ps1 check <changed-path> [...]
.\lx.ps1 validate
```

`check` 会在事实源需要时先运行 Luban 或现有生成器，再执行最小去重检查组合。`validate` 是最终门禁，覆盖固定版本 Luban 转表、架构与 API 注释边界、清单、生成漂移、编译、纯核心测试、Godot 无窗口场景矩阵和 UI 视觉基准。

人工开发者可在 Godot 底部 `LX Tools` 面板执行同一命令。Windows 导出模板安装后用 `.\lx.ps1 export windows` 做 Release 产物 smoke；普通环境不会因没有模板而伪造导出成功。
