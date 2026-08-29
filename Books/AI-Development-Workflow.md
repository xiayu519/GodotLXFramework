# LXFramework AI 开发工作流

LXFramework 把 Codex 视为第一开发者，把手动编程视为第二入口。唯一保证环境是 `gpt-5.6-sol/high`，普通任务与 Plan mode 使用同一配置；其他模型或 reasoning 不进入兼容承诺。

## 文件布局

```text
AGENTS.md                         全仓库授权、路由、红线、完成门禁
lx.ps1                           外层稳定命令入口
game_design/                     Luban XML schema、JSON 源数据、双击转表与固定工具链
godot_project/                   唯一 Godot 工程与 res:// 根
godot_project/**/AGENTS.md       工程及目录职责与依赖边界
.agents/skills/lx-dev/           LX/Godot C# 开发入口与按需知识
.agents/skills/lx-codex-workflow Codex 工作流维护与模型评测入口
.codex/config.toml               默认模型与 reasoning
.codex/memory/                   可版本化 Project Knowledge
.codex/work/                     未完成的跨会话临时状态
```

这套布局使用 Codex 原生可发现入口。Git 与 Codex 工作流位于外层，Godot 工程固定在 `godot_project/`，Luban 策划源固定在同级 `game_design/`。根规则保持短小；目标目录的特殊约束放在最近的 `AGENTS.md`；主题知识在 Skill 命中后再按需加载。

## 开发路径

1. Codex 根据请求与目标目录自动获得根规则和最近的局部规则。
2. LX/Godot C# 任务触发 `$lx-dev`；工作流、提示、记忆或模型评测任务触发 `$lx-codex-workflow`。
3. 跨模块且需要结构概览时运行 `./lx.ps1 inspect`；新结构统一使用 `./lx.ps1 create ...`。
4. 修改事实源和非生成代码；生成目录由工具维护。
5. 迭代时把本次明确变更路径一次传给 `./lx.ps1 check`。
6. 修改任务交付前运行 `./lx.ps1 validate`，以行为结果和命令证据判断完成。

需要确认可用命令、副作用或验收方式时，运行 `./lx.ps1 capabilities <id> --json`；完整能力目录由 `inspect` 写到 `.lx/capabilities.json`，不进入常驻提示。Godot Editor/Debug 已运行时，通过 `./lx.ps1 runtime snapshot <section> --json` 读取当前会话的 UI、资源、Actions 等有界状态，响应必须匹配当前 `sessionId/generation`。

Luban 保留 JSON 作为可审查的策划源，但运行时统一生成 C# 强类型代码与 `.bytes` 二进制表。Windows 可双击 `game_design/build.bat` 一键安装到产品 `Generated/Luban/` 与 `content/data/luban/`；Codex 和 CI 使用等价的 `./lx.ps1 data`。产品通过已有 `LX.Content.LoadLubanTables` 读取，不建立全局配置单例。

所有 `lx` 命令均可在末尾追加 `--json`。此时标准输出只有 `lx.command-report/v1` JSON，固定包含 `command`、`success`、`exitCode`、稳定 `code` 与结构化 `diagnostics`；退出码 `0` 为成功、`1` 为执行或验证失败、`2` 为命令/参数用法错误。人类交互默认仍保留原有文本输出。

静态门禁使用 C# 12 语法树输出 `LX_ARCH_001` 至 `LX_ARCH_004`，覆盖 Core/Godot、adapter/product、产品动态加载和静态服务状态边界；`LX_DOC_001` 保证公开枚举、枚举成员与常量具备人工可读注释，版本化 API 基线阻止未审查的公开签名漂移。Godot headless 门禁把每个运行时场景断言作为独立 scenario 写入 `.lx/smoke.json`，不会只依赖一条笼统的启动成功日志。`validate` 还会执行 EventHub 严格零分配 benchmark，并比较通用 UI 示例的确定性视觉基准。

回答、审查和诊断默认只读；明确要求修改时，Codex 可直接完成范围内的本地非破坏性操作。只有会改变结果的重要歧义、外部写入、破坏性操作或实质扩展范围才需要确认。这比固定的“大中小任务等级”更直接，也减少 Sol/high 在路由阶段消耗的 token。

环境修复和当前 checkout 派生状态升级使用 `doctor|upgrade --plan`，再按计划 `--apply`；文件写入前先保存哈希、备份和事务 journal，验证失败自动回滚。进程中断用 `--recover <plan-id>`，apply 后的人工修改发生哈希冲突时停止恢复而不覆盖。.NET/Godot 等系统安装只作为外部阻塞报告，没有明确授权不自动执行。

普通 push/PR CI 运行完整 `validate`。多轮 `./lx.ps1 soak` 只在定时或手动 CI 运行；Windows Release export 只在版本标签或手动触发时安装精确 Mono templates 并产出 artifact，因此不会拖慢日常 Codex 迭代。

运行时复杂顺序由 `LX.Actions` 组合已有 UI、Scene、Audio 等服务。Actions 属于调用方 `LifetimeScope`，其活动和最近终结树进入运行时 snapshot；它不替代 GameFlow、StateMachine、Scheduler 或 Tween。

## 项目记忆

Project Knowledge 不是源码索引。能够从源码、清单或 `inspect` 重新得到的信息不记录；只有无法直接推导、但会影响未来决策的问题、取舍、用户反馈和外部参考结论才进入 `.codex/memory/`。模型先读索引，再按任务最多读取少量相关条目。

## 模型兼容验证

工作流静态检查负责发现文件缺失、错误默认模型、超长常驻提示、失效链接和旧入口残留；真实模型评测只运行 Sol/high。通过与否由仓库状态和 `./lx.ps1 validate` 决定，同时记录 token、工具调用、重试和延迟，不能用模型自评代替。当前 schema 有 11 个用例；完整套件消耗外部模型额度，未确认时只运行 `-PreflightOnly`。

内部细节由 `$lx-codex-workflow` 的 references 与 eval schema 维护，本书只解释公开使用方式，避免形成第二套指令源。
