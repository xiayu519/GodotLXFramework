# Codex 原生工作流映射

最近核验：2026-08-27。

## 官方事实

- Codex 启动时从仓库根目录到当前工作目录逐层加载 `AGENTS.md`；越接近当前工作目录的规则越晚生效。根文件只保留全仓库规则，目录特例放在最近的嵌套文件中；从仓库根运行时，修改更深目录前按根规则补读对应局部文件。
- Codex 初始暴露 Skill 的 `name`、`description` 与路径；隐式触发按 `description` 语义匹配，命中后再读取正文和当前任务需要的 reference。描述必须前置“做什么”“何时触发”和禁止范围。
- GPT-5.6 更适合精简提示：提供领域上下文、硬约束、授权边界、成功标准和证据要求；每条规则只声明一次，不重复堆叠同义指令。
- 代表性任务评测必须同时观察任务成功、完整性、证据质量、token、延迟、成本和重试，不能只比较语言风格。

官方入口：

- [GPT-5.6 Prompt Guidance](https://developers.openai.com/api/docs/guides/prompt-guidance-gpt-5p6)
- [AGENTS.md 分层项目指令](https://developers.openai.com/codex/guides/agents-md#layer-project-instructions)
- [Codex Skills](https://learn.chatgpt.com/docs/build-skills)

## LXFramework 映射

- `.codex/config.toml`：唯一保证配置为 `gpt-5.6-sol/high`，普通任务与 Plan mode 使用同一基线。
- 根 `AGENTS.md`：只保存 LX 的全仓库结构、不变量、命令与验证门禁；模型选择、AGENTS 自发现机制和 Skill 语义路由不重复写入常驻指令。
- `godot_project/`：唯一 Godot 工程根；外层仓库根通过 `lx.ps1` 包装器提供稳定命令入口，并为 Luban 等外部工具保留同级边界。
- 嵌套 `AGENTS.md`：只保存对应目录的职责、依赖方向和局部验证要求。
- `.agents/skills/lx-dev`：Godot C# 与 LX 产品开发知识。
- `.agents/skills/lx-codex-workflow`：Codex 分层、提示、项目知识和模型评测知识。
- `.lx/capabilities.json` 与按领域文件：由工具生成的机器能力目录，不进入常驻上下文。
- `.lx/runtime/`：Editor/Debug 当前会话、请求和有界 snapshot；`sessionId/generation` 是时效证据。
- `.codex/memory`：随仓库版本化的 Project Knowledge，不冒充 Codex 产品自带的 Memories。
- `Books/AI-Development-Workflow.md`：面向中文开发者的说明，不作为模型强制指令源。

旧的 `.codex/framework.json`、`.codex/validation-map.json` 不属于 Codex 原生发现入口，已移除。机器可验证的工作流约束由 Skill 内脚本和 eval schema 承担。

## 提示编写检查

一个任务提示只需包含会影响行为的信息：目标、必要上下文、硬约束、可执行范围、验收证据、成功与停止条件。已有 `AGENTS.md` 或 Skill 明确的内容不要在任务提示里重抄；无需规定固定工具调用数、固定计划长度或强制多代理。失败时依据观察结果做一次局部修正，再重跑同一 Sol/high 用例。
