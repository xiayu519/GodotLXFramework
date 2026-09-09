---
title: 真实产品反哺通用能力
kind: feedback
status: active
scope: LXFramework 下游迁移缺口的归属判断
verified: 2026-09-09
sources:
  - .agents/skills/lx-migrate/references/migration-workflow.md
---

用户希望用真实下游项目暴露可复现的通用缺口，让后续项目也受益，而不是只修好一个示例。判断缺口归属时，应区分可复用的框架契约/工具能力与产品专属内容，避免把一次游戏需求硬编码进主干。

这是选择通用修复方向的背景，不自动授权修改上游、迁移产品或扩展验证范围；实际实现边界按当前请求和对应 Skill 判断。
