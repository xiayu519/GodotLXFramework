---
name: lx-codex-workflow
description: 维护或审查 LXFramework 的 Codex 原生工作流。用于 AGENTS.md 分层、.agents/skills、Skill 触发与 reference、.codex/config.toml、Project Knowledge、工作流提示词、验证脚本，以及 Terra/Sol 路由或 outcome eval。普通 Godot/C# 功能开发使用 lx-dev；仅修业务代码时不触发。
---

# LXFramework Codex 工作流

目标是让 `gpt-5.6-terra/high` 以最少常驻上下文可靠完成任务，同时不限制 Terra/xhigh 与 Sol。

## 权威与边界

1. 修改 Codex、模型或提示行为前使用 `openai-docs` 核验当前官方文档；修改 Skill 使用 `skill-creator`。
2. 根 `AGENTS.md` 只放全仓库稳定规则；目录特有规则放最近的嵌套 `AGENTS.md`；主题知识与执行细节放 Skill reference。
3. 每条行为规则只维护一个来源。保留结果、约束、权限、证据、成功与停止条件；删除同义重复、仪式步骤和不会改变行为的说明。
4. 只读审查不改文件。实施任务只同步由本次语义变化直接影响的入口、reference、公开文档和 eval。

## 按需读取

- Codex 发现、模型基线、提示结构和仓库映射：`references/codex-native-workflow.md`
- Project Knowledge 读取、写入和失效规则：`references/project-knowledge.md`
- 真实模型矩阵、指标和结果判定：`references/model-evaluation.md`

## 文件职责

- `AGENTS.md` / 嵌套 `AGENTS.md`：自动加载的强制规则。
- `.agents/skills/*/SKILL.md`：依 description 触发的领域入口；`references/` 渐进加载。
- `.codex/config.toml`：仓库默认模型与 reasoning。
- `.codex/memory/`：版本化项目知识，不是官方 Codex Memories。
- `.codex/work/`：仅保存跨会话或等待外部验证的临时状态，完成后删除。
- `Books/AI-Development-Workflow.md`：面向开发者的公开说明，不是指令源。

## 验证

1. 对每个修改的 Skill 运行官方 `quick_validate.py`。
2. 运行 `scripts/check-workflow.ps1` 检查分层、入口、配置、重复规则与 eval schema。
3. 路由、授权、Skill 触发或完成语义改变时运行 smoke model eval；发布工作流或明确要求时运行完整矩阵。
4. eval 失败只按观测到的行为做窄修正，不为单个措辞堆叠全局规则。
