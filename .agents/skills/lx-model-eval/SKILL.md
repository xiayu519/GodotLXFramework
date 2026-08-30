---
name: lx-model-eval
description: 维护 Codex 模型配置、reasoning、路由/outcome eval 与基线；不处理 Skill 编写。
---

# LX 模型评测

完整读取 `references/model-evaluation.md`。保持唯一保证的模型/profile，使用隔离 fixture 和确定性验收器；模型自述不能代替仓库结果。

只读询问保证 profile、preflight 命令或额度授权边界时，依据 reference 和必要的 `.codex/config.toml` 直接回答后停止；不读取 runner、eval schema、历史基线或 Project Knowledge。

修改 eval schema 或 runner 后先运行 `scripts/run-model-evals.ps1 -Suite full -PreflightOnly`。真实 Sol/high 用例会消耗外部额度，只有获得明确授权才运行；结果按用例保存 token、工具、重试、延迟和失败原因。
