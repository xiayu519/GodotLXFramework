---
title: Sol/high 唯一工作流基线
kind: reference
status: active
verified: 2026-08-30
sources:
  - .codex/config.toml
  - .agents/skills/lx-codex-workflow/evals/evals.json
  - .agents/skills/lx-codex-workflow/references/model-evaluation.md
---

LXFramework 只保证 `gpt-5.6-sol/high`，普通任务与 Plan mode 使用同一配置；其他模型或 reasoning 不参与兼容承诺、提示词折中或发布门禁。模型选择只保存在配置与工作流说明中，不进入根 `AGENTS.md`。

Codex CLI `0.151.0-alpha.7.1` 下，2026-08-30 完整 11 项 outcome eval 通过 11/11：输入 1,021,028 tokens（缓存 759,808、未缓存 261,220），输出 20,440，工具调用 64，重试 0，总时长 1,048.81 秒。

根 `AGENTS.md` 只保留 LX 仓库结构、不变量、命令和验证门禁；Skill 完全依赖 description 语义发现。修改根/嵌套 AGENTS、Skill description/路由/reference、模型配置、eval schema/runner，或升级 Codex CLI 的行为版本后，先跑静态/preflight，再重新建立完整 11/11 基线。
