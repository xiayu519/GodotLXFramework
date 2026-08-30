---
name: lx-codex-workflow
description: 维护 AGENTS、Skill 语义边界和提示上下文预算；不处理模型评测或运行时桥。
---

# LX Codex 指令架构

完整读取 `references/codex-native-workflow.md`。

- 修改 Codex 行为前用 `openai-docs` 核验官方文档；修改 Skill 使用 `skill-creator`。
- 根 `AGENTS.md` 只放稳定全仓规则；目录特例放最近的嵌套文件；主题细节放语义 Skill 的 reference。
- 每个 Skill 只覆盖一个可独立请求的领域。description 保持短且有边界，正文只保留共享约束，不用 catch-all 路由汇总独立功能。
- 每个任务激活完成交付所需的最小充分 Skill 集合：真实跨域就组合全部必要 Skill，同时禁止加载任何无关 Skill；不得把“语义隔离”误写成“任务只能激活一个 Skill”。
- 每条规则只维护一个来源；保留授权、硬约束、成功与证据，删除同义重复。
- 产品内容登记、Capability 目录、运行时观测、doctor/upgrade 事务、模型 eval 和 `.codex/memory` 分别使用 `$lx-content`、`$lx-capabilities`、`$lx-runtime-observe`、`$lx-maintenance`、`$lx-model-eval`、`$lx-project-knowledge`。

修改后对全部 Skill 运行 `quick_validate.py`，再运行 `scripts/check-workflow.ps1`。路由变化必须在 `$lx-model-eval` 增加正向和负向用例。
