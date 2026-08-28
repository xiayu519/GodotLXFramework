# LXFramework

LXFramework 是面向 Godot 4.6 .NET / C# 12 的游戏客户端框架。它首先为 Codex 全 AI 开发提供明确事实源、脚手架和可执行门禁，同时保留完整的人工编辑器与命令行入口。

框架不提供第二套引擎：场景、节点、资源和导出仍遵循 Godot；LX 只统一生命周期、资源租约、UI、输入、内容、存档、配置表和验证工作流。网络、下载与热更新暂不在当前版本范围内。

## 主要能力

- Codex 原生工作流：分层 `AGENTS.md`、按需 Skill、Project Knowledge、隔离模型评测。
- 单一工程边界：`godot_project/` 是唯一 `project.godot` 与 `res://` 根。
- 人工工具：Godot 底部 `LX Tools` 面板可执行检查、生成、Luban、脚手架、资源依赖和视觉比较。
- 强约束架构：纯 C# Core、Godot adapter、产品层单向依赖；静态语法树门禁禁止越层和动态资源加载。
- 生命周期：`LifetimeScope` 统一取消与释放，资源、订阅、定时任务和功能实例可绑定生命周期。
- UI：页面栈、层级、覆盖/缓存/模态/焦点策略、异步过渡和强类型返回值。
- 资源与场景：类型化引用、租约、缓存策略、批量预热、依赖分析、后台进度和安全场景切换。
- 输入：类型化动作、输入上下文栈、设备模态、提示文本与绑定冲突检查。
- 数据：固定版本 Luban，JSON 策划源生成 C# 强类型代码和 `.bytes` 二进制表。
- 运行时基础：事件、调度器、状态机、对象池、存档迁移/备份、设置、本地化与统一诊断快照。
- 验证：静态门禁、编译、测试、Godot headless 场景断言、CPU 确定性 UI 视觉回归和性能基线。

## 环境

- Windows 10/11
- Godot 4.6.3 .NET（框架基线为 Godot 4.6 系列）
- .NET SDK 8.0
- PowerShell 5.1 或 7+
- 可选：Godot 4.6.3 export templates，用于 `export windows`
- 可选：Codex CLI。默认保证配置为 `gpt-5.6-terra/high`，复杂规划可使用 Terra/xhigh，同时支持 Sol/high。

不需要全局安装 Luban。首次运行数据命令时，固定版本工具安装到被 Git 忽略的 `.tools/`。

## 目录

```text
LXFramework/
├─ AGENTS.md                  # Codex 唯一人工入口；向下按目录继承规则
├─ .agents/skills/            # lx-dev 与 lx-codex-workflow
├─ .codex/                    # 默认模型、Project Knowledge、临时工作状态
├─ Books/                     # 工作流与兼容性报告
├─ game_design/               # Luban 上游 schema、JSON 数据与双击转表
├─ godot_project/             # 唯一 Godot 工程根
│  ├─ addons/lx_tools/        # 人工编辑器面板
│  ├─ content/                # 运行时事实源清单和生成的二进制表
│  ├─ scene/                  # 固定入口、世界、功能与 UI 场景
│  ├─ src/LXFramework.Core/   # 不依赖 Godot 的纯 C# 核心
│  ├─ src/LXFramework/        # Godot 适配层
│  ├─ tests/                  # 单元测试和视觉基准
│  └─ tools/                  # lx 命令实现、生成器和验证器
└─ lx.ps1                     # 所有人与 AI 共用的稳定命令入口
```

`game_design/` 必须与 `godot_project/` 同级。生成结果进入 `godot_project/content/data/luban/` 和产品 `Generated/Luban/`；不要手改生成目录。

## 五分钟开始

克隆后在仓库外层执行：

```powershell
.\lx.ps1 doctor
.\lx.ps1 inspect
.\lx.ps1 validate
```

创建一个产品层：

```powershell
.\lx.ps1 create game MyGame
.\lx.ps1 validate
```

随后用 Godot .NET 打开 `godot_project/project.godot`。固定入口为 `godot_project/scene/main.tscn`，不要改入口 UID。

## 人工开发工作流

### Godot 编辑器

打开工程后，底部 `LX Tools` 面板提供：

- `Validate`：显示结构化问题；双击 `res://` 路径可定位脚本或场景。
- `Generate Bindings`：从清单重建类型化目录、输入、资源和 UI 绑定。
- `Luban Data`：编译策划 schema，验证生成确定性、编译生成代码并执行负向引用测试。
- `Create`：选择 `game/world/feature/screen/content/input/res/node` 并填写参数。
- `Dependencies`：分析当前场景的资源依赖。
- `Visual Compare`：将通用 UI 示例与已批准基准逐像素比较。
- `Visual Approve`：显式更新基准；它是审阅动作，不应在未知差异时盲目点击。
- `Open game_design`：打开 Godot 工程外的策划源目录。

面板只调用公开 `lx.ps1 --json` 协议，和 Codex/CI 使用同一实现，不存在一套只在编辑器生效的隐藏逻辑。

### 命令行

```powershell
.\lx.ps1 inspect
.\lx.ps1 create screen InventoryScreen inventory
.\lx.ps1 check godot_project/content/ui/ui-manifest.json godot_project/scene/ui/inventory.tscn
.\lx.ps1 validate
```

任意命令末尾可追加 `--json`。自动化读取 `lx.command-report/v1` 的 `success`、`exitCode`、`code` 和 `diagnostics`，不要解析彩色控制台文本。

### 脚手架命令

| 命令 | 用途 |
|---|---|
| `.\lx.ps1 create game <Name>` | 创建产品项目、入口根节点与初始世界 |
| `.\lx.ps1 create world <Name> [id]` | 创建并注册世界场景 |
| `.\lx.ps1 create feature <Name> [id]` | 创建可装卸功能场景 |
| `.\lx.ps1 create screen <Class> [id]` | 创建 `UIScreen` 与场景并注册清单 |
| `.\lx.ps1 create input <Name> <action>` | 新增类型化输入动作 |
| `.\lx.ps1 create res <id> <type> <path>` | 注册类型化资源引用 |
| `.\lx.ps1 create content <Name> [table]` | 创建普通 JSON 内容表 |
| `.\lx.ps1 create node <Class> <GodotBase> [id]` | 创建任意 Godot 节点并保留 LX 上下文注入 |

## Codex 开发工作流

把仓库交给 Codex 时只需让它读取根 `AGENTS.md`。Codex 会按目标路径加载最近规则，并在任务命中时使用 `lx-dev` 或 `lx-codex-workflow`。开发者不需要复制一大段提示词，也不需要先读内部 Skill。

推荐请求方式：

```text
给 MyGame 新增背包纵向切片：InventoryFeature、InventoryScreen、OpenInventory 输入和资源注册。
使用框架脚手架，完成实现并验证。
```

对会实质改变结果且仓库无法消除的歧义，Codex 会集中提问；明确的修改任务会直接实现。完成标准不是“代码看起来正确”，而是结果存在且 `check`/`validate` 证据通过。

模型工作流的说明见 [AI 开发工作流](Books/AI-Development-Workflow.md)，真实兼容性证据见 [模型兼容性报告](Books/Model-Compatibility-Report.md)。

## Luban 策划数据

上游事实源：

- `game_design/schema/`：XML 类型、bean、enum 和 table 定义。
- `game_design/data/`：人工可审查的 JSON 数据。
- `game_design/toolchain.json`：官方 Luban 仓库、版本和 40 位 commit 固定值。

一键生成：

```powershell
.\lx.ps1 data
```

Windows 策划也可直接双击 `game_design/build.bat`。一次成功生成会：

1. 运行固定版本 Luban，生成 C# 和 `.bytes`。
2. 再生成一次并比较哈希，防止非确定性输出。
3. 在隔离项目中编译生成的 C#。
4. 使用缺失引用 fixture 验证错误数据确实被拒绝。
5. 写入 `.lx/luban/report.json` 和运行时 `luban-manifest.json`。

示例表覆盖基础数值、`long`、布尔、字符串、枚举、嵌套 bean、list、set、map、nullable 和跨表引用。产品启动时通过 `LX.Content.LoadLubanTables(...)` 读取，不建立全局表单例。

## 常用运行时 API

产品节点继承 `LXNode`，页面继承 `UIScreen`。框架通过注入的 `LXContext` 暴露服务；不要创建全局上下文或服务定位器。

### 生命周期、事件与时间

```csharp
protected override void OnLXInitialized()
{
    LX.Events.Subscribe<PlayerDied>(OnPlayerDied, Lifetime);
    LX.Scheduler.Schedule(TimeSpan.FromSeconds(1), Tick, Lifetime);
}
```

`LifetimeScope` 释放时会取消 Token 并逆序释放绑定对象。事件、调度器、资源和功能都应绑定最窄生命周期。

### 资源

```csharp
using var icon = LX.Res.Acquire(ResCatalog.InventoryIcon);
var texture = icon.Resource;

using var preload = await LX.Res.PreloadAsync(new AssetPreloadSet<Texture2D>(
    "inventory",
    [new AssetLoadRequest<Texture2D>("inventory_icon", ResCatalog.InventoryIcon)]));
```

`AssetCachePolicy.Transient/Cached/Resident` 决定最后一个租约归还后的缓存行为。`AnalyzeDependencies` 在加载前报告缺失依赖和环。

### 场景

```csharp
Action<SceneLoadProgress> progress = value => UpdateLoading(value.Ratio);
using var preloaded = await LX.Scenes.PreloadAsync(WorldCatalog.Hangar.Id, progress);
await LX.Scenes.ChangeAsync(
    WorldCatalog.Hangar.ScenePath,
    SceneTransitionMode.KeepPreviousUntilReady,
    progress);
```

`KeepPreviousUntilReady` 保留旧世界直到新世界就绪；`ReleasePreviousBeforeLoad` 降低峰值内存，但失败时可能没有活动世界。

### UI

```csharp
var handle = await LX.UI.OpenAsync(UICatalog.Inventory, payload);
UIResult<InventoryChoice> choice = await handle.WaitForResultAsync<InventoryChoice>();
await handle.CloseAsync();
```

页面描述支持 `UILayer`、`UICachePolicy`、`UICoverPolicy`、`UIInputPolicy` 和 `UIFocusPolicy`。`OnTransitionAsync` 处理进入/退出动画；页面内部用 `RequestClose(result)` 返回强类型结果。

框架自带白底和彩色文字组合的 UI 示例：Toast、确认对话框、Loading/Progress、Tooltip、虚拟列表和组合展示。场景为 `res://scene/ui/examples/ui_components_showcase.tscn`。

### 输入

```csharp
using var menu = LX.Input.PushContext(new InputContextDescriptor(
    "inventory",
    new HashSet<InputActionId> { InputCatalog.Confirm, InputCatalog.Cancel },
    InputContextMode.Exclusive));

InputPrompt prompt = LX.Input.Prompt(InputCatalog.Confirm);
var conflicts = LX.Input.FindBindingConflicts();
```

Exclusive 上下文拦截未列出的动作；Passthrough 允许继续向下查询。输入设备切换通过 `InputModalityChanged` 事件发布。

### 存档、设置和本地化

`SaveStore<T>` 提供版本迁移、原子替换、主文件损坏时备份回退、`ListSlots()` 和 `DeleteAsync()`。设置使用 `SettingsService`，不要把机器偏好混进游戏存档。

```csharp
using var titleKey = new StringName("inventory.title");
string title = LX.Localization.Text(titleKey);
string localizedAsset = LX.Localization.ResolveVariant(new Dictionary<string, string>
{
    ["zh_CN"] = "res://art/title.zh_CN.png",
    ["en"] = "res://art/title.en.png",
});
```

本地化会记录缺失 key；伪本地化可检查截断和硬编码文本。资源变体按完整 locale、语言和 fallback 顺序选择。

### 统一诊断

```csharp
LX.Diagnostics.Log(DiagnosticSeverity.Information, "inventory", "opened");
string path = LX.Diagnostics.WriteSnapshot();
```

`DiagnosticsService` 的 `lx.runtime-snapshot/v1` 快照统一包含生命周期、场景、指标、资源、UI、Feature、音频、输入、本地化、设置和近期结构化日志。它是后续可视化 runtime debugger 的稳定数据入口。

## 验证、视觉和导出

迭代时把本次明确变更路径一次交给：

```powershell
.\lx.ps1 check godot_project/src/LXFramework/UI/UIScreen.cs
```

交付前运行：

```powershell
.\lx.ps1 validate
```

最终门禁依次检查工作流、Luban、静态事实源与架构、构建、测试、Godot headless 场景断言和 UI 视觉基准。

视觉命令：

```powershell
.\lx.ps1 visual capture ui_components
.\lx.ps1 visual compare ui_components
.\lx.ps1 visual approve ui_components
```

Godot 的 Dummy headless renderer 不提供可靠 ViewportTexture，因此视觉运行器实例化真实 Control 场景后，用 CPU 确定性渲染布局、颜色、进度值和文本指纹。文本或布局的语义变化仍会形成像素 diff。

Windows Release 导出：

```powershell
.\lx.ps1 export windows
```

需要先在 Godot 安装与编辑器同版本的 export templates。命令会构建 Release、导出、记录哈希并启动产物执行 smoke；报告写入 `.lx/export.json`。

性能基线：

```powershell
.\lx.ps1 benchmark
```

报告写入 `.lx/benchmark.json`，覆盖诊断日志、事件分发和对象池。性能报告用于比较，不把不稳定的机器绝对值设为 CI 硬阈值。

## 架构红线

- `LXFramework.Core` 不依赖 Godot。
- `LXFramework` 不依赖任何产品命名空间；产品只能反向依赖框架。
- 禁止反射发现、服务定位器、静态全局服务状态。
- 禁止第二套事件总线、时钟、资源注册表、生命周期容器、对象池、场景管理器或 UI 管理器。
- 游戏代码禁止 `GD.Load` 与 `ResourceLoader.Load*`；资源必须由清单生成类型化引用并经 `LX.Res` 获取。
- `content/` 和 `game_design/` 是事实源，生成目录不可手改。
- 框架公开枚举、枚举成员和常量必须写清语义；`LX_DOC_001` 会阻止缺少注释的提交。

## 发布与贡献

`main` 是可验证基线。版本发布前运行 `validate`；只有安装了 export templates 才追加 `export windows`。变更说明写入 [CHANGELOG.md](CHANGELOG.md)，贡献规则见 [CONTRIBUTING.md](CONTRIBUTING.md)。

许可证：[MIT](LICENSE)。
