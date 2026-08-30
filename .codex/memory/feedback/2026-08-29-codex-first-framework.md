---
title: Codex 是 LXFramework 第一开发者
kind: feedback
status: active
verified: 2026-08-29
sources:
  - README.md
  - Books/AI-Development-Workflow.md
  - .agents/skills/lx-capabilities/references/capability-catalog.md
  - .agents/skills/lx-runtime-observe/references/runtime-observation.md
---

LXFramework 的首要用户是 Codex，人工开发是兼容和接管入口。后续取舍优先减少模型决策分支、隐藏状态、重复事实源和无法机器验收的步骤；模块数量与人工可视化工具不能凌驾于机器发现、运行时可观测、确定性生成、生命周期闭环、结构化证据和安全恢复。

AI Control Plane 保持按需和有界：能力目录与运行时 snapshot 不进入 `AGENTS.md` 常驻提示；只读观察先于运行时 mutation；事务化维护只能操作当前 checkout 可证明的派生文件，外部安装仍服从授权边界。新增游戏模块只有在真实产品需求或重复模式证明收益后才进入默认框架。
