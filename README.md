# LXFramework

LXFramework 是一个 **Codex 优先、面向全 AI 游戏开发** 的 Godot 4.7.2 .NET / C# 游戏客户端框架。

> 不要求先成为 Godot 专家，也可以从游戏需求直接做到可运行的 Windows PC 包。

准备好 Godot .NET 和 .NET SDK 后，无论是否具备 Godot 开发经验，都可以直接向 Codex 描述要做的游戏。Codex 会按照仓库约定完成项目结构、C# 逻辑、场景与资源、策划数据和自动检查；安装同版本 export templates 后，还可以沿用同一套流程导出并验证 Windows PC 包。开发者可以把注意力放在玩法与最终结果上。

这并不是绕过 Godot：游戏仍然由 Godot 运行和导出。LXFramework 做的是把容易出错的引擎细节、工程规范和交付流程收敛为 Codex 可执行的统一约定，让 Godot 经验不再成为开始开发的前置条件。熟悉 Godot 的开发者仍然可以使用编辑器、场景、节点、资源和调试工具进行原生开发，项目不会变成只能由 AI 操作的黑盒。

仓库内置分层 `AGENTS.md`、领域 Skill、类型化清单、脚手架和验证命令，Codex 可以从自然语言需求出发，理解项目结构并持续完成：

```text
游戏需求 → 创建工程结构 → 实现玩法与内容 → 自动验证 → 导出 Windows PC 包
```

当前版本不包含网络、资源下载和热更新。

## 示例项目

完整的飞机大战第一关示例已经发布在 [`sample` 分支](https://github.com/xiayu519/LXFramework/tree/sample)。它实际使用 Luban、Feature、UI、类型化资源、输入、音频、`NodePool<TNode>` 和统一生命周期，并验证胜利、死亡、连续重开与资源释放闭环；可运行的 Windows PC 包位于该分支的 `build/windows/`。

## Codex 优先的开发体验

- **没有 Godot 经验也能开始**：直接描述玩法、界面、数据和交付目标，由 Codex 按框架约定完成实现；开发者不需要先掌握大量编辑器操作和工程规则。
- **从需求到 PC 包形成闭环**：创建、实现、生成、检查、运行和 Windows 导出都有统一入口，不把“代码写完”误当成游戏已经交付。
- **仓库即上下文**：Codex 会从根目录开始读取分层 `AGENTS.md`，自动获得架构边界、目录规则和完成标准。
- **知识按任务加载**：框架开发、产品开发、UI、资源和数据等知识通过 Skill 按需加载，不要求开发者在提示词中复制整套文档。
- **交付结果可以验证**：Codex 使用 `lx create` 创建结构，通过类型化 API 实现功能，并以 `check`、`validate` 和实际导出结果作为完成证据。
- **人与 AI 使用同一套事实源**：Codex 和 CI 使用完整 CLI；Godot 中的 `LX Tools` 只呈现开发者真正需要的中文操作，两者共用相同的清单和生成器。

使用 Codex 时，在仓库根目录打开项目并描述要实现的游戏功能即可。当前唯一保证配置是 `gpt-5.6-sol/high`。具体指令分层和推荐提问方式见 [AI 开发工作流](Books/AI-Development-Workflow.md)。

## 为什么使用 LXFramework

- **直接面向游戏结果开发**：从自然语言需求开始，由 Codex 按统一流程完成游戏功能并导出可运行的 Windows PC 包，而不只是生成零散代码。
- **降低 Godot 的上手门槛**：不要求先熟悉场景组织、资源路径、生命周期和构建细节；框架把这些约束变成脚手架、类型化 API 和自动检查。
- **保留 Godot 原生开发方式**：仍然使用 `.tscn`、Node、Resource 和 Godot 编辑器，不需要学习另一套引擎。
- **统一生命周期**：事件订阅、定时任务、资源租约和功能实例可以绑定到节点或功能的 `Lifetime`，退出时自动取消和释放。
- **减少字符串和路径错误**：世界、UI、输入、资源和内容都从清单生成类型化目录，业务代码不需要到处拼接 `res://` 路径。
- **产品代码与框架代码分层**：游戏只依赖框架，框架不会反向依赖具体游戏，便于持续升级和复用。
- **常用系统已经集成**：UI 栈、资源缓存、场景切换、输入上下文、Luban、存档迁移、本地化、音频和诊断使用同一套运行时上下文。
- **创建和检查都有统一入口**：常用结构通过 `lx create` 创建；一次 `validate` 可以检查清单、生成代码、编译、测试和 Godot 场景。

## 环境要求

- Windows 10/11
- Godot 4.7.2 .NET
- .NET SDK 8.0
- PowerShell 5.1 或 PowerShell 7+
- Windows 导出时需要安装与 Godot 编辑器相同版本的 export templates

Luban 不需要全局安装。第一次运行数据生成时，固定版本工具会安装到 Git 忽略的 `.tools/` 目录。

## 开始使用

### Codex：直接从游戏需求开始

在仓库根目录打开 Codex，然后直接描述游戏目标即可。例如：

```text
使用 LXFramework 创建一个带主菜单、第一关战斗和胜负结算的 2D 游戏。
完成玩法、数据和资源接入，运行完整验证，并导出 Windows PC 包。
```

Codex 会读取仓库指令和对应 Skill，使用框架原生能力完成结构创建、功能实现、检查与导出。你仍然可以随时打开 Godot 查看场景、运行游戏或继续手动开发。

### 1. 直接打开 Godot 工程

LXFramework 已经包含完整的 `project.godot` 和 C# 工程文件，不需要先运行脚本才能打开。使用 Godot 官方推荐的 Project Manager：

1. 启动 **Godot 4.7.2 .NET**。
2. 在 Project Manager 中点击 **Import**。
3. 选择 `godot_project/` 目录，或直接选择 `godot_project/project.godot`。
4. 点击 **Import & Edit**，等待 Godot 完成首次资源导入。

以后可以直接在 Project Manager 中双击该项目进入编辑器。这与打开普通 Godot 项目的方式完全相同；仓库外层目录只是额外保存 `game_design/`、Git 和统一命令入口。

Godot 官方说明见 [Using the Project Manager](https://docs.godotengine.org/en/stable/tutorials/editor/project_manager.html) 和 [C# basics](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html)。

### 2. 第一次创建游戏产品层

当前仓库是干净的框架基线，第一次开发具体游戏时需要创建一次产品层。优先打开 Godot 底部的 **LX 开发工具**，点击 **创建内容…**，创建类型选择 **游戏产品层**，在 **游戏名称** 中输入 `MyGame`。

也可以从仓库外层根目录使用命令行：

```powershell
.\lx.ps1 create game MyGame
```

该命令会自动创建：

```text
godot_project/
├─ script/MyGame/
│  ├─ AGENTS.md
│  ├─ GameRoot.cs
│  └─ Generated/
├─ scene/world/              # 初始世界场景
└─ content/game/
   └─ game-manifest.json     # 游戏名称、代码根目录和初始世界
```

它还会生成 `GameCatalog` 和 `WorldCatalog`。`Generated/` 中的文件由工具维护，不要手动修改。

`create game` 是框架的一次性初始化，不是每次打开 Godot 都要执行的步骤。

### 3. 构建并运行

回到 Godot 编辑器：

1. 点击编辑器右上角的 **Build** 编译 C#。
2. 按 **F6** 可以运行当前场景。
3. 按 **F5** 可以从项目主场景运行完整游戏。

工程固定主场景是 `godot_project/scene/main.tscn`。其中的 `LXHost` 会创建框架服务、加载产品清单并进入初始世界，不要为了运行自己的游戏而替换这个入口。

### 4. 验证项目

日常编辑和运行仍然使用 Godot。完成一项功能后，从仓库外层运行完整检查：

```powershell
.\lx.ps1 validate
```

`validate` 是 LXFramework 的提交前门禁，包含生成一致性、公开 API 基线、Core 测试、EventHub 严格零分配、Godot smoke 与视觉比较；它不是打开 Godot 的前置条件。只有环境或工具异常时才需要运行：

```powershell
.\lx.ps1 doctor
```

Codex 需要审查修复内容时先生成计划；`apply` 只修改当前 checkout 可以确定生成的派生文件，系统安装仍需明确授权：

```powershell
.\lx.ps1 doctor --plan
.\lx.ps1 upgrade --plan
```

维护进程意外中断后使用计划 ID 恢复；如果 apply 后文件又被修改，恢复会报告冲突并保留新修改：

```powershell
.\lx.ps1 doctor --recover <plan-id>
.\lx.ps1 upgrade --recover <plan-id>
```

### 版本管理建议

如果要在这份框架上开发游戏，建议先从干净的 `main` 创建产品分支，再执行 `create game`：

```powershell
git switch -c game/my-game
```

Git 分支是项目版本管理建议，与 Godot 如何打开和运行工程无关。

## 项目目录

```text
LXFramework/
├─ godot_project/                 # 唯一 Godot 工程根，也是 res:// 根
│  ├─ content/                    # 游戏、世界、UI、资源、输入和内容清单
│  ├─ scene/                      # 世界、Feature、UI 和其他场景
│  ├─ script/<GameName>/          # 产品代码，主要开发区域
│  ├─ src/LXFramework.Core/       # 纯 C# 基础模块，不依赖 Godot
│  ├─ src/LXFramework/            # Godot 适配与运行时服务
│  ├─ tests/                      # 单元测试、场景测试和视觉基准
│  └─ addons/lx_tools/            # Godot 编辑器中的 LX Tools 面板
├─ game_design/                   # Luban schema 和策划源数据
└─ lx.ps1                         # 创建、生成、检查、运行和导出的统一入口
```

一般游戏开发主要修改以下位置：

- `godot_project/script/<GameName>/`：游戏 C# 代码。
- `godot_project/scene/`：Godot 场景。
- `godot_project/content/`：运行时清单和普通内容数据。
- `game_design/schema/`、`game_design/data/`：Luban 策划表源文件。
- 美术、音频等资源目录：由项目自行在 `godot_project/content/` 下组织，再通过资源清单注册。

不要手改 `Generated/`、Luban 生成代码或生成的二进制表。

## 使用框架的基本方式

### 从节点访问框架服务

产品节点通常继承 `LXNode`，UI 页面继承 `UIScreen`。节点进入框架管理的场景树前会收到 `LXContext`，之后通过实例属性 `LX` 调用服务：

```csharp
public partial class PlayerController : LXNode
{
    protected override void OnLXInitialized()
    {
        LX.Events.Subscribe<PlayerDied>(OnPlayerDied, Lifetime);
        LX.Scheduler.Schedule(TimeSpan.FromSeconds(1), UpdateEnergy, Lifetime);
    }

    private void OnPlayerDied(PlayerDied message)
    {
        // 处理事件
    }

    private void UpdateEnergy()
    {
        // 定时逻辑
    }
}
```

这里的 `LX` 是注入到当前节点的上下文，不是静态全局对象。`Lifetime` 结束时，订阅和定时任务会一起取消。

如果节点必须继承 `CharacterBody2D`、`Area2D` 等 Godot 原生类型，使用：

```powershell
.\lx.ps1 create node PlayerBody CharacterBody2D player_body
```

生成的节点会保留 Godot 原生继承关系，并显式接收 LX 上下文。

### 使用类型化目录

框架根据清单生成以下入口：

- `WorldCatalog`：世界。
- `FeatureCatalog`：可装卸功能。
- `UICatalog`：UI 页面。
- `InputCatalog`：输入动作。
- `ResCatalog`：资源。
- `ContentCatalog`：普通内容表。

业务代码调用生成项，不直接拼资源路径或清单 ID：

```csharp
await LX.Scenes.ChangeAsync(WorldCatalog.Dungeon.Id, Lifetime.Token);
var menu = await LX.UI.NavigateAsync(
    UICatalog.MainMenu.Id,
    parentLifetime: Lifetime);
using var texture = Lifetime.Own(LX.Res.Acquire(ResCatalog.PlayerSprite));
```

## 创建游戏结构

优先使用脚手架命令创建结构。命令会同时创建源码或场景、更新清单并刷新类型化目录。

| 需求 | 命令 |
|---|---|
| 创建游戏产品层 | `.\lx.ps1 create game MyGame` |
| 创建世界 | `.\lx.ps1 create world Dungeon dungeon` |
| 创建可装卸功能 | `.\lx.ps1 create feature Player player` |
| 创建 UI 页面 | `.\lx.ps1 create screen MainMenu main_menu` |
| 创建 Godot 原生节点 | `.\lx.ps1 create node PlayerBody CharacterBody2D player_body` |
| 创建普通 JSON 内容表 | `.\lx.ps1 create content Item items` |
| 注册输入动作 | `.\lx.ps1 create input Jump game_jump Space` |
| 注册资源 | `.\lx.ps1 create res player_sprite Texture2D res://content/art/player.png Cached` |

### World、Feature、Screen 和 Node 怎么选

- **World**：游戏当前运行的主要世界，例如主菜单世界、城镇、地牢或战斗关卡。
- **Feature**：可以独立生成和释放的功能场景，例如玩家、任务系统、战斗模块或调试工具。
- **Screen**：由 UI 栈管理的页面、弹窗或覆盖层。
- **Node**：必须继承特定 Godot 原生节点，并且需要调用 LX 服务的普通场景节点。

## 框架功能与调用方式

### 生命周期、事件和定时任务

`LifetimeScope` 负责统一取消和释放。事件订阅、定时任务、资源租约和 Feature 应绑定到最窄的生命周期：

```csharp
LX.Events.Subscribe<QuestCompleted>(OnQuestCompleted, Lifetime);
LX.Scheduler.Schedule(TimeSpan.FromSeconds(2), RefreshQuest, Lifetime);
```

框架同时提供：

- `LX.Clock`、`LX.Scheduler`：普通游戏时间与调度。
- `LX.PhysicsClock`、`LX.PhysicsScheduler`：物理帧时间与调度。
- `LX.Pause`：暂停状态。
- `LX.Random`：可确定性复现的随机数。
- `LX.Actions`：受生命周期管理的顺序、并行、竞速、超时、重试和清理编排。

复杂过程保持为可观测动作树，而不是散落的异步状态变量：

```csharp
await LX.Actions.RunAsync(
    LXActions.Sequence(
        LXActions.Async(ct => LX.UI.PlayFadeAsync(UIFadeMode.FadeOut, cancellationToken: ct)),
        LXActions.Async(ct => LX.Scenes.ChangeAsync(WorldCatalog.Dungeon.Id, ct)),
        LXActions.Async(ct => LX.UI.PlayFadeAsync(UIFadeMode.FadeIn, cancellationToken: ct))),
    Lifetime);
```

### 世界和场景切换

创建世界：

```powershell
.\lx.ps1 create world Dungeon dungeon
```

切换到生成的世界：

```csharp
await LX.Scenes.ChangeAsync(WorldCatalog.Dungeon.Id, Lifetime.Token);
```

需要加载进度或控制旧世界释放时机时，可以先预载：

```csharp
Action<SceneLoadProgress> progress = value => UpdateLoading(value.Ratio);
using var preloaded = await LX.Scenes.PreloadAsync(
    WorldCatalog.Dungeon.Id,
    progress);

await LX.Scenes.ChangeAsync(
    WorldCatalog.Dungeon.ScenePath,
    SceneTransitionMode.KeepPreviousUntilReady,
    progress);
```

`KeepPreviousUntilReady` 会在新世界准备完成后才释放旧世界；`ReleasePreviousBeforeLoad` 可以降低峰值内存，但加载失败时可能没有活动世界。

### 2D 相机

相机控制器绑定调用方传入的具体 `Camera2D`，不建立全局“当前相机”，因此单相机和多相机使用相同 API。控制器随指定 `Lifetime` 回收，并提供平滑跟随、死区、相机中心边界和衰减震动：

```csharp
var cameraController = Camera2DController.Attach(camera, Lifetime);
cameraController.Follow(
    player,
    new Camera2DFollowOptions
    {
        DeadZoneSize = new Vector2(96, 64),
        SmoothingSpeed = 8,
    });
cameraController.SetCenterBounds(new Rect2(0, 0, 4096, 2304));
cameraController.Shake(10, TimeSpan.FromSeconds(0.25));
```

边界约束的是相机中心世界坐标。需要改变震动之外的基础偏移时设置 `BaseOffset`；3D 相机将使用独立的 `Camera3DController`，当前版本尚未实现。

### Feature

Feature 是由框架创建、注入并释放的独立功能场景：

```csharp
await using var player = await LX.Features.SpawnAsync(
    FeatureCatalog.Player.Id,
    this,
    Lifetime,
    Lifetime.Token);
```

Feature 适合有独立节点树和生命周期的模块。纯数据或纯算法不需要为了使用框架而包装成 Feature。

### UI

创建并注册页面：

```powershell
.\lx.ps1 create screen InventoryScreen inventory
```

打开页面并等待强类型结果：

```csharp
var handle = await LX.UI.OpenAsync(UICatalog.Inventory, payload);
UIResult<InventoryChoice> choice =
    await handle.WaitForResultAsync<InventoryChoice>();
await handle.CloseAsync();
```

页面内部通过 `RequestClose(result)` 返回结果。UI 描述支持：

- 页面层级与页面栈。
- 缓存策略。
- 覆盖、模态和输入策略。
- 焦点策略。
- 异步进入/退出过渡。
- Toast、确认框、Loading、Tooltip 和虚拟列表等基础组件。

全屏黑幕过场由内置缓存 prefab 执行。`FadeOut` 会保持黑幕，后续 `FadeIn` 将其移除；
`FadeOutIn` 自动完成透明、黑色、透明的完整流程：

```csharp
await LX.UI.PlayFadeAsync(UIFadeMode.FadeOut, cancellationToken: Lifetime.Token);
// 在黑幕保持期间切换世界或完成其他原子操作。
await LX.UI.PlayFadeAsync(
    UIFadeMode.FadeIn,
    new UIFadeOptions
    {
        FadeInDuration = TimeSpan.FromSeconds(0.5),
        Transition = Tween.TransitionType.Cubic,
        Ease = Tween.EaseType.Out,
    },
    Lifetime.Token);
```

默认淡出、淡入各为 `0.35s`，完整流程中间保持 `0.05s`，曲线为 `Sine/InOut`；
同一 `UIService` 上的过场请求会串行执行。

内置组件展示场景：

```text
res://scene/ui/examples/ui_components_showcase.tscn
```

### 资源

先把资源注册到清单：

```powershell
.\lx.ps1 create res inventory_icon Texture2D res://content/art/inventory.png Cached
```

`Texture2D`、`AtlasTexture`、`PackedScene`、`AudioStream`、字体、材质、Shader、Mesh 和自定义 `Resource` 都使用同一个 `LX.Res`。短作用域读取通过生成目录获取资源租约：

```csharp
using var icon = Lifetime.Own(LX.Res.Acquire(ResCatalog.InventoryIcon));
Texture2D texture = icon.Resource;
```

会动态替换的资源属性使用 `AssetBinding<T>`。它会在替换或生命周期结束时先清空目标引用，再归还旧租约，避免材质、字体、Mesh 等资源在重开流程中不断叠加：

```csharp
var material = AssetBinding<Material>.Create(
    LX.Res,
    Lifetime,
    value => sprite.Material = value);
await material.SetAsync(ResCatalog.PlayerMaterial, Lifetime.Token);
```

UI 动态图片和 Godot 原生 `AtlasTexture` 使用页面激活期绑定；缓存页面隐藏后也不会继续占用这次打开加载的图片：

```csharp
var portrait = BindTexture(_portrait);
await portrait.SetAsync(ResCatalog.PlayerPortrait, cancellationToken);
```

`PackedScene` 是 Godot 原生的场景模板，也是最接近 Unity Prefab 的概念。一次性实例使用 `PackedSceneInstance<TNode>`，大量重复的敌机、子弹或特效使用 `PackedSceneNodePool<TNode>`：

```csharp
await using var enemy = await PackedSceneInstance<Enemy>.CreateAsync(
    LX,
    ResCatalog.Enemy,
    this,
    Lifetime,
    Lifetime.Token);
```

资源策略：

- `Transient`：最后一个租约释放后立即移除。
- `Cached`：允许框架继续缓存。
- `Resident`：常驻资源。

不要在产品代码中使用 `GD.Load` 或 `ResourceLoader.Load*` 动态加载资源，也不要对加载得到的共享 `Resource` 手动 `Dispose()`。这样资源依赖、缓存、替换、释放和缺失检查才能由同一个系统管理。`LX.Res.Snapshot()` 可区分活动租约和空闲缓存；重开闭环应确认活动租约回到基线，Node 在异步释放后已经失效。

批量资源可以预热并报告进度：

```csharp
using var preload = await LX.Res.PreloadAsync(
    new AssetPreloadSet<Texture2D>(
        "inventory",
        [new AssetLoadRequest<Texture2D>(
            "inventory_icon",
            ResCatalog.InventoryIcon)]));
```

### 输入

注册输入动作：

```powershell
.\lx.ps1 create input OpenInventory game_open_inventory I
```

代码使用 `InputCatalog`，并可通过输入上下文限制当前允许的动作：

```csharp
using var inventoryInput = LX.Input.PushContext(
    new InputContextDescriptor(
        "inventory",
        new HashSet<InputActionId>
        {
            InputCatalog.Confirm,
            InputCatalog.Cancel,
        },
        InputContextMode.Exclusive));

InputPrompt prompt = LX.Input.Prompt(InputCatalog.Confirm);
var conflicts = LX.Input.FindBindingConflicts();
```

- `Exclusive`：拦截当前上下文未声明的动作。
- `Passthrough`：允许继续查询下层上下文。
- `InputPrompt`：获取与当前设备匹配的按键提示。

### 内容和 Luban 策划表

少量、简单的 JSON 数据可以使用：

```powershell
.\lx.ps1 create content Item items
```

跨表引用、复杂类型或批量策划数据使用 Luban：

- `game_design/schema/`：XML schema。
- `game_design/data/`：人工维护的 JSON 数据。
- `game_design/toolchain.json`：固定 Luban 版本。

生成数据：

```powershell
.\lx.ps1 data
```

运行时通过 `LX.Content` 创建生成的表集合：

```csharp
var tables = LX.Content.LoadLubanTables(
    loader => new GameData.Tables(loader));
var probe = tables.TbDesignProbe.Get("lx_framework");
```

不要手改 `content/data/luban/` 或产品目录中的 `Generated/Luban/`，也不要创建静态 `Tables` 单例。

### 存档、设置和本地化

- `SaveStore<T>`：版本迁移、原子替换、备份回退、存档槽枚举和删除。
- `LX.Settings`：保存设备或用户偏好，避免把机器设置混进游戏进度存档。
- `LX.Localization`：文本查询、缺失 key 记录、伪本地化和本地化资源变体。

```csharp
using var titleKey = new StringName("inventory.title");
string title = LX.Localization.Text(titleKey);

string localizedTexture = LX.Localization.ResolveVariant(
    new Dictionary<string, string>
    {
        ["zh_CN"] = "res://content/art/title.zh_CN.png",
        ["en"] = "res://content/art/title.en.png",
    });
```

### 音频

`LX.Audio` 统一管理音频组、并发上限、拒绝/抢占策略、音频快照和音乐淡入淡出。音频资源仍然通过 `LX.Res` 注册和持有，避免建立另一套资源生命周期。

### 运行时诊断

```csharp
LX.Diagnostics.Log(
    DiagnosticSeverity.Information,
    "inventory",
    "opened");

string snapshotPath = LX.Diagnostics.WriteSnapshot();
```

诊断快照会汇总生命周期、场景、资源、UI、Feature、音频、输入、本地化、设置、指标和近期结构化日志，适合定位“当前运行时到底处于什么状态”。

Godot Editor/Debug 正在运行时，Codex 可以读取当前会话而不修改游戏状态：

```powershell
.\lx.ps1 runtime status --json
.\lx.ps1 runtime snapshot ui --json
.\lx.ps1 runtime snapshot resources --json
.\lx.ps1 runtime snapshot actions --json
```

查询会验证进程、心跳、`sessionId` 和 `generation`，不会把上次运行残留的文件当成当前证据。可查询领域和命令副作用通过按需能力目录发现：

```powershell
.\lx.ps1 capabilities runtime --json
```

## 推荐的开发流程

一次常见功能开发可以按下面的顺序进行：

1. 使用 `create world|feature|screen|node|input|res|content` 创建结构。
2. 在产品目录中实现 C# 逻辑，在 Godot 编辑器中编辑场景。
3. 修改内容清单或 `game_design/` 中的策划源数据。
4. 把本次明确改动的路径一次交给 `check`。
5. 功能完成后运行 `validate`。

例如：

```powershell
.\lx.ps1 create feature Inventory inventory
.\lx.ps1 create screen InventoryScreen inventory
.\lx.ps1 create input OpenInventory game_open_inventory I

.\lx.ps1 check godot_project/script/MyGame godot_project/content/ui/ui-manifest.json godot_project/content/input/input-manifest.json
.\lx.ps1 validate
```

`check` 用于快速迭代，只运行当前修改需要的生成和检查；`validate` 是提交前的完整验证。公开 API 有意改变时，先审查差异，再运行 `.\lx.ps1 api update` 更新版本化基线。

## Godot 编辑器工具

打开工程后，Godot 底部的 `LX Tools` 插件显示为 **LX 开发工具**，默认只提供面向人工开发者的操作：

- **创建内容…**：通过中文表单创建游戏产品层、World、Feature、UIScreen、Godot 节点、JSON 内容表、输入动作和资源引用。
- **生成策划数据**：修改 `game_design/schema` 或 `game_design/data` 后生成 Luban C# 和 `.bytes`。
- **场景依赖**：检查当前已保存场景引用的资源，并标出缺失项。
- **打开策划数据目录**：打开 Godot `res://` 之外的 `game_design/`。

创建和数据生成在独立后台进程中运行。顶部状态会明确显示 **正在执行**、**成功** 或 **失败**；左侧列出中文诊断，右侧保留原始输出用于排错。生成 C# 导致 Godot 重新加载程序集时，面板重新加载后仍会从 `.lx/editor-command.json` 恢复最终结果。

`generate`、`validate` 和视觉基准属于 Codex、CI 或框架维护命令，不显示在普通开发者工具栏中，仍可从仓库外层通过 `lx.ps1` 使用。

## UI 视觉检查与导出

视觉基准是框架维护工作，不显示在普通 Godot 工具栏中。修改框架 UI 后从仓库外层比较：

```powershell
.\lx.ps1 visual compare ui_components
```

只有人工确认差异符合设计时才更新基准：

```powershell
.\lx.ps1 visual approve ui_components
```

安装相同版本的 Godot export templates 后，可导出并启动 Windows 产物进行 smoke：

```powershell
.\lx.ps1 export windows
```

产物固定写入外层 `build/windows/`。产品可在 `game-manifest.json` 声明包内 smoke，验证真实玩法流程和 Luban 等非 Godot 原生文件已进入产物。普通开发和 `validate` 不要求安装导出模板。

`.github/workflows/validate.yml` 在 push/PR 运行完整门禁；定时或手动任务可运行 `.\lx.ps1 soak 5`，版本标签或手动任务会安装精确 `4.7.2.stable.mono` templates 并验证 Windows Release。soak 与 export 不进入普通本地 `validate`。

## 开发时必须遵守的边界

- `LXFramework.Core` 不依赖 Godot。
- `LXFramework` 不依赖产品代码；产品代码只能反向依赖框架。
- 不创建全局 `LXContext`、服务定位器或第二套事件、时间、资源、生命周期、场景、对象池和 UI 系统。
- 产品代码不使用动态 `GD.Load` 或 `ResourceLoader.Load*`。
- `content/` 和 `game_design/` 是数据事实源，生成目录不可手改。
- Godot 场景树操作必须留在主线程。

## 进一步了解

- [贡献与提交约定](CONTRIBUTING.md)
- [更新记录](CHANGELOG.md)
- [AI 开发工作流](Books/AI-Development-Workflow.md)

许可证：[MIT](LICENSE)。
