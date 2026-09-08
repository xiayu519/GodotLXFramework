# Codex 原生工作流映射

官方能力核验：2026-09-08。真实通过范围以评测报告为准，不由配置文件推断。

## 配置与档位

- 项目默认 `gpt-6-astra/low`，对应界面 Light；Extra High 的参数是 `xhigh`，不是 `exhigh`。
- 支持 `low/medium/high/xhigh/max`，由 `lx-model-eval/evals/evals.json` 维护唯一档位目录。Light 基础套件和 xhigh 复杂代表套件是发布验收目标；其他档位仅表示配置支持，不能冒称已经实测。
- `.codex/config.toml` 只设置项目模型和普通推理默认值。未设置 Plan effort 时客户端使用内置 Plan 预设，不会自动继承普通任务档位。
- `.codex/start-codex.ps1 -Effort low|medium|high|xhigh|max` 显式设置普通与 Plan effort；`-PrintOnly` 查看原生命令、版本与配置而不消耗模型额度。桌面/IDE 已有会话仍以实际选择为准，改文件不会切换当前对话。
- 不把用户级 named profile、API 的 token/采样参数或网页 API 上下文窗口数字塞进项目 TOML；CLI 负责实际传输与上下文管理。Ultra 不是本仓库的 API effort。
- 本项目不自动切换档位。明确范围的小改先 Light；跨生命周期、异步竞争、公共 API、迁移架构适合更高档。用户选择的档位不改变授权边界和验收标准。

## 指令分层

根 `AGENTS.md` 只保留稳定的仓库不变量、授权与完成门禁；目标目录特例放嵌套文件，修改前读取适用的局部指令。Skill 描述负责语义发现，命中后完整读入口和需要的 reference。真实跨域任务使用最小充分集合，不强制单 Skill，也不把所有正文一次载入。

Astra 对指令、主动程度和停止条件更敏感。提示只保留目标、必要事实、硬约束、可执行范围及成功证据：
- 分析、审查默认只读；明确实施请求完成实现和验证，已明确的选择不重复询问。
- 先复用已有产品与框架约定；可逆细节合理假设，改变玩法、公共契约、数据兼容或授权才询问。
- 不设固定工具调用次数、固定问题数、固定 reference 数或强制多代理。并行仅用于可独立执行的必要工作；代理委派遵循当前上层授权。
- 测试由风险决定：一次聚合明确变更路径运行 `check`；相关改动、新失败或未决风险才重跑，达到根规则条件才运行 `validate`。
- 不以空实现、削弱断言、只创建骨架、模型自述成功替代任务结果。回答说明结果、证据和未验证范围。

## 所有权与成本

`godot_project/` 是唯一 Godot/`res://` 根；`game_design/` 是同级 Luban 上游；外层 `lx.ps1` 是稳定入口。框架、玩法、UI、资源、数据等保持现有语义 Skill 边界，不另建管理层。

Capability 和 runtime snapshot 是按需事实源，不进入常驻提示；`.codex/memory` 只存不能由当前源码重建的决策与经验。面向用户的 `Books/AI-Development-Workflow.md` 不是另一套强制指令。

静态检查严查缺失文件、编码、语法、错误配置和失效引用；正文长度与 reference 数量只提示复审，不用字符数替代语义正确性。新增 Skill 仍必须提供正向和负向路由用例。效率报告分开列输入、缓存、未缓存、输出、工具失败和耗时；不同 CLI、提示、套件与缓存条件的历史数字不能直接宣称节省比例。

## 官方来源

- [Astra prompting best practices](https://developers.openai.com/api/docs/guides/latest-model?model=gpt-6-astra#prompting-best-practices)
- [GPT-6 Astra model](https://developers.openai.com/api/docs/models/gpt-6-astra)
- [Codex models / reasoning levels](https://learn.chatgpt.com/docs/models)
- [Codex configuration reference](https://learn.chatgpt.com/docs/config-file/config-reference)
- [AGENTS.md](https://developers.openai.com/codex/guides/agents-md)
- [Codex Skills](https://learn.chatgpt.com/docs/build-skills)
