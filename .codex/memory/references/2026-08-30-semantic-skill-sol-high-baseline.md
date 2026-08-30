---
title: 语义隔离 Skill 的 Sol/high 19/19 基线
kind: reference
status: active
verified: 2026-08-30
sources:
  - .codex/config.toml
  - .agents/skills/lx-model-eval/evals/evals.json
  - .agents/skills/lx-model-eval/scripts/run-model-evals.ps1
  - .agents/skills/lx-model-eval/references/model-evaluation.md
---

LXFramework 的 Skill 路由采用“完成请求所需的最小充分集合”：单领域任务只激活对应 Skill，真实跨域任务激活全部必要 Skill，同时禁止加载无关 Skill。当前 16 个仓库 Skill 的 discovery description 合计 1,713 bytes；命中后的入口为 470–1,380 bytes，替代了原先约 3,876 bytes 的宽泛开发入口。

Codex CLI `0.151.0-alpha.7.1` 下，`gpt-5.6-sol/high` 完整 19 项 outcome eval 通过 19/19。输入 1,480,994 tokens（缓存 1,072,128、未缓存 408,866），输出 31,529，工具调用 85，重试 0，总时长 1,519.73 秒。评测报告生成于 `.lx/model-evals/20260830-060707/summary.json`。

19 项覆盖所有 16 个 Skill，并同时检查 `expected_skills` 与 `forbidden_skills`、只读/写入边界、确定性文件结果、最终 `validate` 和效率预算。路由判定只把实际命令读取或输出完整 Skill 正文计为加载；文档仅提到 `SKILL.md` 路径不会产生假阳性。

修改根/嵌套 AGENTS、Skill description/入口/reference、最小充分集合规则、模型配置、eval schema/runner，或升级 Codex CLI 行为版本后，先跑全部 Skill 结构校验与 `-PreflightOnly`，再重新建立完整 19/19 Sol/high 基线。
