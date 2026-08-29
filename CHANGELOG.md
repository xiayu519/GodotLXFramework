# Changelog

本项目遵循 Keep a Changelog 的组织方式；版本号遵循语义化版本。

## [Unreleased]

### Changed

- Codex 唯一保证配置收敛为 `gpt-5.6-sol/high`；普通任务与 Plan mode 不再维护其他模型或 reasoning 的兼容分支。
- 根 `AGENTS.md` 删除模型、原生发现机制和显式 Skill 路由，改由 Skill description 完成语义发现；当前 11 项 Sol/high outcome eval 通过 11/11。

### Added

- 版本化公开 API 基线、push/PR CI、可选多轮 Godot soak，以及标签/手动 Windows Release export。
- 可恢复维护事务状态机与 `doctor|upgrade --recover`。

### Fixed

- PackedScene 池、ActionRunner、GameFlow、LXHost 与 WorldChunkStreamer 的关闭、清理和所有权边界。
- AssetRegistry 共享 inflight 进度观察者隔离、RuntimeBridge I/O 容错、诊断分区按需采集和设置按键默认值恢复。
- Capability 副作用分类、Mono export template 版本识别和无 .NET SDK 时的 PowerShell 前置诊断。

## [0.1.0] - 2026-08-28

### Added

- Codex 原生分层工作流、Project Knowledge、Skill 和隔离模型评测。
- Godot 编辑器 `LX Tools` 面板与统一 `lx.ps1 --json` 命令协议。
- 生命周期、事件、调度、状态机、对象池、资源、场景、UI、输入、存档、设置、本地化和统一诊断。
- 固定版本 Luban C# + `.bytes` 生成、确定性检查、生成代码编译和负向引用 fixture。
- 通用 UI 组件示例、确定性视觉回归、Windows 导出 smoke 和性能基线。
- `sample` 分支发布完整飞机大战第一关示例及可运行 Windows PC 包。
- 静态架构、生成漂移和公开枚举/常量注释门禁。

### Deferred

- 网络、下载、热更新与可视化 runtime debugger。
