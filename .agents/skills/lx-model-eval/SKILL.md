---
name: lx-model-eval
description: 维护 Codex 模型配置、reasoning、路由/outcome eval 与基线；不处理 Skill 编写。
---

# LX 模型评测

完整读取 `references/model-evaluation.md`。模型与档位取自 `evals/evals.json`；使用隔离 fixture 和独立验收器，分别报告路由、实现结果、覆盖范围和效率，模型自述不能代替仓库结果。

只读询问配置、preflight 命令或额度边界时，读取 reference 和必要的配置即可；询问当前通过状态时核对实际报告、schema/hash 和用例覆盖，不能从默认模型推断兼容性。

修改 eval schema 或 runner 后先运行 `scripts/test-eval-contract.ps1` 与 `scripts/run-model-evals.ps1 -PreflightOnly`。真实评测消耗外部额度，确认授权后加 `-AllowModelCalls`；局部变更用 `-CaseId`，完整套件用于工作流发布、基线重建或明确要求。失败按原因修正后只重跑受影响用例，不隐式换模型或降低断言。各档位遵守相同的授权与工程要求。
