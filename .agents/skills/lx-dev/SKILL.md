---
name: lx-dev
description: 开发、审查、诊断或扩展 LXFramework Godot 4.6 C# 框架及其 Game 产品层。用于 LX/LX.Core API、场景、UI、资源、输入、内容、Feature、世界、生命周期、生成清单与脚手架。纯 Codex 工作流、AGENTS、Skill、模型配置、Project Knowledge 或 eval 改动改用 lx-codex-workflow；泛泛 C# 或非本仓库 Godot 问题不触发。
---

# LXFramework 开发

本 Skill 只负责 LXFramework 领域路由和非显而易见的工程约束；仓库级授权、红线和最终门禁以根 `AGENTS.md` 为准。

根与目标目录的 `AGENTS.md` 已由 Codex 自动加载，不要再用 shell 重读。

## 开始

用户已明确列出会改变结构、API 或行为的未决选择时，读完本文件后直接用 1-3 个集中问题确认；确认前不读 reference、不运行 `inspect` 或搜索实现来代替用户决策。

1. 源码、清单和当前工具输出高于 reference。需要跨模块概览时运行 `./lx.ps1 inspect`；只有确需完整文件表才加 `--full`。
2. 根据下表只读当前任务需要的 1-2 个 reference。不要为普通局部修改读取全部文档。
3. 单一 API 定位先读匹配 reference，再把一次 `rg` 限定到已知模块目录或文件；每个结论得到一处直接实现证据后停止，不搜索 `.`、不枚举旁证或输出长文件全文。

## 主题路由

| 任务 | 必读 | 需要时再读 |
|---|---|---|
| 层级、模块、API 定位 | `references/architecture.md` 或 `references/modules.md` | `references/design-decisions.md` |
| 运行时、线程、注入、生命周期、关闭 | `references/runtime-contracts.md` | `references/memory-safety.md` |
| 新抽象、通信或资源所有权取舍 | `references/design-decisions.md` | 对应实现源码 |
| 动态资源、Prefab、图集或释放闭环 | `references/resource-lifecycle.md` | `references/runtime-contracts.md` |
| 创建游戏、世界、Feature、UI、内容、输入或资源 | `references/product-workflow.md` | 对应清单和生成目录 |

## 实施

- 结构单元使用 `lx create`；修改清单和非生成源码，让 `check` 负责必要生成。
- 动态资源只使用 `LX.Res` 及其生命周期句柄；具体选型和闭环验证读取 `references/resource-lifecycle.md`。
- 只同步本次行为变化直接影响的 reference；只读审查发现过期内容时报告，不擅自写文档。

## 收尾

把本次明确文件一次交给 `./lx.ps1 check`。修改任务交付前运行 `./lx.ps1 validate`；报告行为结果、验证和仍无法验证的风险，然后停止。
