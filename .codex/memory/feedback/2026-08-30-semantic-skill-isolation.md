---
title: Skill 必须按独立语义隔离
kind: feedback
status: active
verified: 2026-08-30
sources:
  - .agents/skills/lx-codex-workflow/SKILL.md
  - .agents/skills/lx-codex-workflow/references/codex-native-workflow.md
---

LXFramework 的仓库 Skill 不能用笼统入口汇总多个可独立请求的功能。每个 Skill 只覆盖一个清晰语义领域；所有会话可见的 description 保持短且有排除边界，正文和 reference 只在命中后加载。新增能力若有独立授权、输入事实源、完成证据或常被单独请求，应拆为新 Skill，并用正向与负向路由用例验证，避免无关提示词与 token 随框架增长。

用户进一步确认：限制的是无关 Skill，而不是 Skill 数量。每个任务应激活完成交付所需的最小充分集合；单领域只激活对应 Skill，真实跨域则同时激活全部必要 Skill，但不得额外加载与交付无关的 Skill。
