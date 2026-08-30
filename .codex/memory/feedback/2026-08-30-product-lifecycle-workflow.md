---
title: 用真实游戏迁移反哺主干工作流
kind: feedback
status: active
verified: 2026-08-30
sources:
  - .agents/skills/lx-dev/references/migration-workflow.md
  - godot_project/tools/LXFramework.Tools/MigrationPlanner.cs
---

LXFramework 的 Codex 工作流不能只优化单个 sample；新游戏、旧 LX 游戏升级、其他 Godot 项目移植和跨引擎/行为复刻都应成为主干可重复能力。真实产品迁移暴露的分类、API 适配、产品 smoke、运行时可观测和视觉验收缺口，应优先反哺主干工具、Skill 与 outcome eval，再由后续产品分支消费。

迁移工具默认只读来源并生成有界计划，不自动执行 Git 改写或机械翻译跨引擎代码。框架、工具和 Codex 工作流以最新目标 checkout 为权威；产品事实、源码、场景和获授权资产可迁移，生成物与构建产物重建。完成以纵向切片、Debug 产品 smoke、状态型任务的当前会话快照、产品视觉、重开闭合和最终验证为证据。
