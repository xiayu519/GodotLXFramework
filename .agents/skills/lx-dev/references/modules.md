# 模块

## LX.Core

无 Godot 依赖的纯 .NET 代码。可确定、可脱离引擎测试的逻辑优先放在这里。

## LXFramework

Godot 适配程序集。`LXHost` 只负责显式组合、帧驱动与启动编排；运行时验收在独立 `FrameworkSmokeRunner`，诊断聚合在 `DiagnosticsService`。`LXNode`、`UIScreen` 和 `lx create node` 生成的任意原生节点是上下文注入入口。

- `LX.UI`：manifest 描述层、缓存、覆盖、模态与焦点策略；页面支持进入/退出过渡和 `UIResult<T>` 强类型返回。
- `LX.Res`：租约和 `Transient/Cached/Resident` 缓存策略；批量优先级、并发、进度、命名预热集合以及缺失/循环依赖分析。
- `LX.Scenes`：世界清单、安全替换、后台预载和 `SceneLoadProgress`。
- `LX.Input`：类型化动作、Exclusive/Passthrough 上下文栈、设备提示和绑定冲突。
- `LX.Localization`：缺失 key、伪本地化和 locale 资源变体。
- `LX.Diagnostics`：`lx.runtime-snapshot/v1`，统一聚合生命周期、场景、资源、UI、功能、音频、输入、本地化、设置、指标和近期日志。
- `LX.Audio`：显式 group overload、并发上限、拒绝/抢占、快照与音乐淡变；当前不继续扩展音频能力。

场景、功能、资源、UI 和世界流式对象均由生命周期管理。

## LXFramework.Tools

`lx.ps1` 背后的命令行工作流。游戏/世界、功能、UI、资源、输入和内容清单是事实源；生成目录会检查漂移并清除失去事实源的生成文件。`Validator` 还检查架构边界、公开枚举/常量注释、人工 README、编辑器插件、导出 preset 和视觉基准。`VisualRunner`、`ExportRunner` 与 `BenchmarkRunner` 分别负责 UI 回归、Windows 产物 smoke 和性能报告。

## LXFramework.Core.Tests

纯核心单元测试。引擎集成由 Godot 无窗口场景矩阵验证，并把注入顺序、资源租约/依赖/预热、场景进度、节点池、Feature、UI 覆盖/结果/过渡、输入上下文、本地化 QA、统一诊断、音频组/淡变和异步关闭逐项写入 `.lx/smoke.json`。UI 示例的确定性像素结果写入 `.lx/visual/` 并与 `tests/Visual/Baselines/` 比较。

## 产品层

由 `lx create game` 创建的可选产品层，默认位于 `script/<GameName>`，准确路径由游戏清单的 `sourceRoot` 声明。游戏代码可以依赖 LX；生成的 UI 绑定和内容目录位于产品根目录的 `Generated/`。
