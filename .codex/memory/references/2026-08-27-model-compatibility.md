---
title: Terra 与 Sol 工作流兼容性基线
kind: reference
status: active
verified: 2026-08-28
sources:
  - Books/Model-Compatibility-Report.md
  - .agents/skills/lx-codex-workflow/evals/evals.json
  - https://developers.openai.com/api/docs/guides/prompt-guidance-gpt-5p6
---

# Terra 与 Sol 工作流兼容性基线

Codex CLI 0.144.1 在 2026-08-27 的三项隔离矩阵中，Terra/high、Terra/xhigh、Sol/high 均通过只读资源审查、创建新游戏并验证、重要 UI 歧义先确认，结果均为 3/3。当前 schema 已扩展为 11 项，2026-08-28 只验证了确定性 preflight；未重新执行付费模型任务，因此现有证据仍是旧三项矩阵，不能写作 11/11。

最低保证仍是 Terra/high。修改根/嵌套 `AGENTS.md`、`lx-dev` 路由、授权边界、完成语义、默认模型或评测隔离方式后，必须重跑完整矩阵并更新或替代本条目。精确指标见 `Books/Model-Compatibility-Report.md`。
