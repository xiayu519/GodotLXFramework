---
title: Astra Light/xhigh 工作流验收基线
kind: reference
status: active
verified: 2026-09-08
sources:
  - .agents/skills/lx-model-eval/evals/baselines/2026-09-08-astra.json
  - .lx/model-evals/latest-acceptance.json
  - .agents/skills/lx-model-eval/references/model-evaluation.md
---

在 Codex CLI 0.153.0、Windows PowerShell 5.1、Godot 4.7.2 .NET 环境下，Astra Light（low）基础用例完成 23/23，xhigh 复杂代表完成 3/3。这是跨批次、含确定性重放及一次 Light 同档位复测的验收，不是单轮一次全过。

各冻结副本的任务输入哈希均为 `7F6CBC4FCF7E0155209DA2B069EDFB7AF51AB11D8A413C1B3BC992548D43387B`；差异仅为验收脚本修复。原始报告和失败保留，修复评分器后重放原始模型文件，没有替换失败实现。最后报告文档的写入不冒充重新运行模型。

新增行为证据包括：Light/xhigh 的 RoundState 各通过 510 条独立断言；xhigh 的 RewardLedger 通过 23 条并发、取消、异常、代际隔离和溢出断言。验收器另以两份正确实现和五种注入错误验证正反判定；25 项离线契约检查、16 个 Skill 结构检查与仓库完整 validate 通过。

重要边界：

- Light 的 readonly-skill-isolation 首次返回占位链接与未完成答复，被拦截后同档位重跑通过。因此不能承诺 Light 永远一次成功，不能用自动升档掩盖失败。
- xhigh 的跨域脚手架题功能通过，但超出工具次数、总输入和输出三项旧效率阈值。Light 验收任务均在既定效率预算内；这支持日常默认 Light，不支持所有任务常驻 xhigh。
- Light 已接受尝试：输入 2,698,826（缓存 2,197,760，未缓存 501,066），输出 22,671，117 次工具调用；xhigh：输入 1,743,224（缓存 1,546,496，未缓存 196,728），输出 26,298，63 次工具调用。这不是本次总账：诊断和中断调用不全在内，也不是同条件 Sol/Astra A/B。
- medium/high/max 和普通/Plan 参数映射仅做配置验证。未宣称全部 Godot 玩法、长期 soak、Windows 导出、活动 UI 美术或 tileset 出图已验收；UI 帮助页用例只覆盖未接入世界的静态结构。

迁移中发现旧门禁会把缺少路径映射但 validate 成功的结果算作完成；现已增加独立 changed-path check。自然语言固定措辞、PowerShell 参数绑定/UTF-8、生成 .uid 与长路径等评分器问题分别修复并留下重放证据，不归罪于候选模型，也不删除原始失败记录。
