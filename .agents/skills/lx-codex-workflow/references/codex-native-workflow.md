# Codex 原生工作流映射

最近核验：2026-08-27。

## 官方事实

- Codex 启动时从仓库根目录到当前工作目录逐层加载 `AGENTS.md`；离目标文件更近的规则优先。根文件只保留全仓库规则，目录特例放在最近的嵌套文件中。
- Skill 由 `SKILL.md` 的 `name` 与 `description` 发现；命中后再读取正文和当前任务需要的 reference。描述必须说清“做什么”和“何时触发”。
- GPT-5.6 更适合精简提示：提供领域上下文、硬约束、授权边界、成功标准和证据要求；每条规则只声明一次，不重复堆叠同义指令。
- 代表性任务评测必须同时观察任务成功、完整性、证据质量、token、延迟、成本和重试，不能只比较语言风格。

官方入口：

- [GPT-5.6 Prompt Guidance](https://developers.openai.com/api/docs/guides/prompt-guidance-gpt-5p6)
- [AGENTS.md 分层项目指令](https://developers.openai.com/codex/guides/agents-md#layer-project-instructions)
- [Codex Skills](https://developers.openai.com/codex/skills)

## LXFramework 映射

- `.codex/config.toml`：最低保证配置为 `gpt-5.6-terra/high`，Plan mode 为 `xhigh`；Terra/xhigh 与 Sol 使用同一工作流契约。
- 根 `AGENTS.md`：只保存授权边界、Skill 路由、全局架构红线和完成门禁。
- `godot_project/`：唯一 Godot 工程根；外层仓库根通过 `lx.ps1` 包装器提供稳定命令入口，并为 Luban 等外部工具保留同级边界。
- 嵌套 `AGENTS.md`：只保存对应目录的职责、依赖方向和局部验证要求。
- `.agents/skills/lx-dev`：Godot C# 与 LX 产品开发知识。
- `.agents/skills/lx-codex-workflow`：Codex 分层、提示、项目知识和模型评测知识。
- `.codex/memory`：随仓库版本化的 Project Knowledge，不冒充 Codex 产品自带的 Memories。
- `Books/AI-Development-Workflow.md`：面向中文开发者的说明，不作为模型强制指令源。

旧的 `.codex/framework.json`、`.codex/validation-map.json` 不属于 Codex 原生发现入口，已移除。机器可验证的工作流约束由 Skill 内脚本和 eval schema 承担。

## 提示编写检查

一个任务提示只需包含会影响行为的信息：目标、必要上下文、硬约束、可执行范围、验收证据、成功与停止条件。已有 `AGENTS.md` 或 Skill 明确的内容不要在任务提示里重抄；无需规定固定工具调用数、固定计划长度或强制多代理。失败时先依据观察结果做一次局部修正，再决定是否升级 reasoning 或模型。
