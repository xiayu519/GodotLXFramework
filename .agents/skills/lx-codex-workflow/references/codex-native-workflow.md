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
- `.agents/skills/lx-framework`：框架内核、公开 API 与运行时契约。
- `.agents/skills/lx-game`、`lx-ui`、`lx-input`、`lx-content`：分别负责玩法/产品结构、产品 UI、产品输入和普通内容/静态资源登记。
- `.agents/skills/lx-resources`、`lx-data`、`lx-persistence`：分别负责资源生命周期、Luban 和存档。
- `.agents/skills/lx-migrate`、`lx-editor-tools`：分别负责既有游戏迁移和 Godot 编辑器 LX Tools。
- `.agents/skills/lx-capabilities`、`lx-runtime-observe`、`lx-maintenance`：分别负责 Capability 目录、运行时观测和 doctor/upgrade 事务。
- `.agents/skills/lx-codex-workflow`、`lx-model-eval`、`lx-project-knowledge`：分别负责指令架构、模型评测和版本化项目知识。
- `.lx/capabilities.json` 与按领域文件：由工具生成的机器能力目录，不进入常驻上下文。
- `.lx/runtime/`：Editor/Debug 当前会话、请求和有界 snapshot；`sessionId/generation` 是时效证据。
- `.codex/memory`：随仓库版本化的 Project Knowledge，不冒充 Codex 产品自带的 Memories。
- `Books/AI-Development-Workflow.md`：面向中文开发者的说明，不作为模型强制指令源。

旧的 `.codex/framework.json`、`.codex/validation-map.json` 不属于 Codex 原生发现入口，已移除。机器可验证的工作流约束由 Skill 内脚本和 eval schema 承担。

## 提示编写检查

一个任务提示只需包含会影响行为的信息：目标、必要上下文、硬约束、可执行范围、验收证据、成功与停止条件。已有 `AGENTS.md` 或 Skill 明确的内容不要在任务提示里重抄；无需规定固定工具调用数、固定计划长度或强制多代理。失败时依据观察结果做一次局部修正，再重跑同一 Sol/high 用例。

每个 Skill 只覆盖一个可以被用户独立请求的语义领域。所有 Skill 的短 `name/description` 会参与发现，因此描述总量受预算约束；命中后只加载该 Skill 的短入口，再读取当前任务明确需要的 reference。新能力若拥有不同的授权、输入事实源、完成证据或高概率独立请求，应拆成新 Skill，不得继续扩张现有 catch-all 入口。

任务路由目标不是“恰好一个 Skill”，而是完成请求所需的最小充分集合。单领域请求只加载一个；纵向切片、迁移实施等真实跨域请求加载全部必要 Skill。eval 必须同时声明正向 `expected_skills` 和负向 `forbidden_skills`，既防遗漏必要领域，也防加载额外提示。
