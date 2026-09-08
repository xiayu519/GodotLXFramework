# Changelog

本项目遵循 Keep a Changelog 的组织方式；版本号遵循语义化版本。

## [Unreleased]

### Changed

- Codex 工作流迁移至 GPT-6 Astra，默认 `low`（Light），配置支持 `medium/high/xhigh/max`；普通与 Plan 档位可通过项目启动器显式统一，不自动修改全局配置或切换已有会话。
- 精简并明确 `AGENTS.md` 与语义 Skill 的主动执行、只读边界、脚手架范围和按风险验证规则；保留按需 Project Knowledge，旧 Sol 基线归档为已替代的历史证据。
- 模型评测扩展为 16 项路由、6 项实施和 2 项独立行为用例。2026-09-08 的 Astra Light 基础套件最终 23/23，xhigh 复杂代表 3/3 功能通过；包含原实现重放及一次 Light 同档位复测，xhigh 脚手架用例保留效率预算告警。medium/high/max 仅配置验证，未新增 Sol 回归或 token 性价比承诺。
- 同步项目介绍、开发工作流和贡献文档，明确当前启动器/验收的 Astra 范围、Sol 客户端使用边界及版本化证据与本地日志的区别。

### Added

- 版本化公开 API 基线、push/PR CI、可选多轮 Godot soak，以及标签/手动 Windows Release export。
- 可恢复维护事务状态机与 `doctor|upgrade --recover`。
- Astra 项目启动器、冻结评测输入和逐文件指纹、原始改动归档、无模型调用的确定性重放及跨批次验收汇总。
- 回合状态与异步奖励的独立 C# 行为断言，以及 25 项离线验收契约回归；功能正确性和 token/工具效率预算分别报告。

### Fixed

- PackedScene 池、ActionRunner、GameFlow、LXHost 与 WorldChunkStreamer 的关闭、清理和所有权边界。
- AssetRegistry 共享 inflight 进度观察者隔离、RuntimeBridge I/O 容错、诊断分区按需采集和设置按键默认值恢复。
- Capability 副作用分类、Mono export template 版本识别和无 .NET SDK 时的 PowerShell 前置诊断。
- 模型验收不能再以 `validate` 成功覆盖实际改动路径的 `check` 失败；修复自然语言等价表达、生成 `.uid`、PowerShell 参数绑定和 UTF-8 造成的误判，保留原始失败与重放凭据。

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
