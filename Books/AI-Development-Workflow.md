# LXFramework AI 开发工作流

LXFramework 把 Codex 视为第一开发者，把手动编程视为第二入口。唯一保证环境是 `gpt-5.6-sol/high`，普通任务与 Plan mode 使用同一配置；其他模型或 reasoning 不进入兼容承诺。

## 文件布局

```text
AGENTS.md                         全仓库授权、路由、红线、完成门禁
lx.ps1                           外层稳定命令入口
game_design/                     Luban XML schema、JSON 源数据、双击转表与固定工具链
godot_project/                   唯一 Godot 工程与 res:// 根
godot_project/**/AGENTS.md       工程及目录职责与依赖边界
.agents/skills/                  按框架、玩法、UI、输入、内容、资源、数据和 Codex 控制面分开的语义 Skill
.codex/config.toml               默认模型与 reasoning
.codex/memory/                   可版本化 Project Knowledge
.codex/work/                     未完成的跨会话临时状态
```

这套布局使用 Codex 原生可发现入口。Git 与 Codex 工作流位于外层，Godot 工程固定在 `godot_project/`，Luban 策划源固定在同级 `game_design/`。根规则保持短小；目标目录的特殊约束放在最近的 `AGENTS.md`；主题知识在 Skill 命中后再按需加载。

## 开发路径

1. Codex 根据请求与目标目录自动获得根规则和最近的局部规则。
2. Codex 激活完成请求所需的最小充分 Skill 集合，而不是强制每个任务只能有一个 Skill：框架内核、玩法/产品结构、产品 UI、产品输入、普通内容/静态资源登记分别使用 `$lx-framework`、`$lx-game`、`$lx-ui`、`$lx-input`、`$lx-content`；资源生命周期、Luban、存档、迁移和编辑器工具分别使用 `$lx-resources`、`$lx-data`、`$lx-persistence`、`$lx-migrate`、`$lx-editor-tools`。真实跨域任务组合全部必要 Skill，同时不加载无关 Skill。Capability 目录、运行时观测、doctor/upgrade 事务、Codex 指令架构、模型评测和 Project Knowledge 也各自使用独立 Skill。
3. 跨模块且需要结构概览时运行 `./lx.ps1 inspect`；新结构统一使用 `./lx.ps1 create ...`。
4. 修改事实源和非生成代码；生成目录由工具维护。
5. 迭代时把本次明确变更路径一次传给 `./lx.ps1 check`；产品 smoke 由清单路径映射自动收窄，缺陷复现只补跑失败场景和同契约代表样本。
6. 提交/推送、内容冻结/发布、公共框架或验证基础设施变更时运行一次 `./lx.ps1 validate`；普通内容填充和局部缺陷修复不重复全量验证。

需要确认可用命令、副作用或验收方式时，运行 `./lx.ps1 capabilities <id> --json`；完整能力目录由 `inspect` 写到 `.lx/capabilities.json`，不进入常驻提示。Godot Editor/Debug 已运行时，通过 `./lx.ps1 runtime snapshot <section> --json` 读取当前会话的 UI、资源、Actions 等有界状态，响应必须匹配当前 `sessionId/generation`。

旧 LX 游戏升级、其他 Godot 项目移植和跨引擎/行为复刻先运行 `./lx.ps1 migrate plan --source <directory|git-ref> --mode upgrade|port|remake`。计划先把框架、产品、生成物和构建产物分开；源码可读时继续有界分析启动入口、模块、脚本解释/编译、内容 schema、状态和存档，优先语义复刻合理架构。无源码或新游戏先建立目标驱动骨架；重复剧情、任务、场景、对话和战斗入口经统一事件中间表示驱动通用模块，高频玩法保留确定性代码系统。每条命令以 `{recordId}:{opcode}` 接入 `LX.Actions`，连续影片复用局部 `VideoSequencePlayer`，而不是复制异步循环。组合根只承担依赖组合、启动和顶层流程切换，事件执行、玩法、UI、存档、迁移与 smoke 按状态所有权和生命周期分离；引擎契约使用继承，可替换玩法能力优先组合，规模门禁会拒绝继续膨胀或用 `partial` 隐藏的巨型职责。代表切片只验证架构契约；批量填充期间静态验证全部脚本/数据，运行时只验证受影响契约组。`productSmokes[].checkPaths` 和 `visualTargets[].checkPaths` 把路径映射到受影响门禁；运行时路径未映射且未以窄 `pattern` 和可审查 `reason` 声明为 `staticCheckPaths` 时 `check` 失败。累计流程由一个独立 smoke 进程报告多个 checkpoint，并可在同一进程验证状态闭合及声明的性能预算；资产源文件、Godot import cache 和 Windows Release 包体分别在拥有真实证据的阶段判断。`smoke product all` 与 `visual compare product` 只用于内容冻结和完整门禁，`inspect --product-coverage` 用于 LX 服务静态映射。当前范围只覆盖 PC，不增加网络、服务器或其他平台分支。

Luban 保留 JSON 作为可审查的策划源，但运行时统一生成 C# 强类型代码与 `.bytes` 二进制表。Windows 可双击 `game_design/build.bat` 一键安装到产品 `Generated/Luban/` 与 `content/data/luban/`；Codex 和 CI 使用等价的 `./lx.ps1 data`。产品通过已有 `LX.Content.LoadLubanTables` 读取，不建立全局配置单例。

所有 `lx` 命令均可在末尾追加 `--json`。此时标准输出只有 `lx.command-report/v1` JSON，固定包含 `command`、`success`、`exitCode`、稳定 `code` 与结构化 `diagnostics`；退出码 `0` 为成功、`1` 为执行或验证失败、`2` 为命令/参数用法错误。人类交互默认仍保留原有文本输出。

静态门禁使用 C# 12 语法树输出 `LX_ARCH_001` 至 `LX_ARCH_004`，覆盖 Core/Godot、adapter/product、产品动态加载和静态服务状态边界；`LX_DOC_001` 保证公开枚举、枚举成员与常量具备人工可读注释，版本化 API 基线阻止未审查的公开签名漂移。Godot headless 门禁把每个运行时场景断言作为独立 scenario 写入 `.lx/smoke.json`，不会只依赖一条笼统的启动成功日志。`validate` 还会执行已声明的 Debug 产品 smoke、EventHub 严格零分配 benchmark，并比较框架与已声明产品 UI 视觉基准；语义视觉使用 headless，真实 Viewport 证据使用隐藏、不可聚焦的渲染窗口并只强制绘制声明帧，所有自动验收均不显示 GUI。

回答、审查和诊断默认只读；明确要求修改时，Codex 可直接完成范围内的本地非破坏性操作。只有会改变结果的重要歧义、外部写入、破坏性操作或实质扩展范围才需要确认。这比固定的“大中小任务等级”更直接，也减少 Sol/high 在路由阶段消耗的 token。

环境修复和当前 checkout 派生状态升级使用 `doctor|upgrade --plan`，再按计划 `--apply`；文件写入前先保存哈希、备份和事务 journal，验证失败自动回滚。进程中断用 `--recover <plan-id>`，apply 后的人工修改发生哈希冲突时停止恢复而不覆盖。.NET/Godot 等系统安装只作为外部阻塞报告，没有明确授权不自动执行。

普通 push/PR CI 运行完整 `validate`。多轮 `./lx.ps1 soak` 只在定时或手动 CI 运行；Windows Release export 只在版本标签或手动触发时安装精确 Mono templates 并产出 artifact，因此不会拖慢日常 Codex 迭代。

运行时复杂顺序由 `LX.Actions` 组合已有 UI、Scene、Audio 等服务。Actions 属于调用方 `LifetimeScope`，其活动和最近终结树进入运行时 snapshot；它不替代 GameFlow、StateMachine、Scheduler 或 Tween。

## 项目记忆

Project Knowledge 不是源码索引。能够从源码、清单或 `inspect` 重新得到的信息不记录；只有无法直接推导、但会影响未来决策的问题、取舍、用户反馈和外部参考结论才进入 `.codex/memory/`。模型先读索引，再按任务最多读取少量相关条目。

## 模型兼容验证

工作流静态检查负责发现文件缺失、错误默认模型、超长常驻提示、失效链接、Skill 描述预算和旧入口残留；真实模型评测只运行 Sol/high。通过与否由仓库状态和 `./lx.ps1 validate` 决定，同时记录 Skill 正负路由、token、工具调用、重试和延迟，不能用模型自评代替。当前 schema 有 21 个用例并覆盖全部语义 Skill；完整套件消耗外部模型额度，未确认时只运行 `-PreflightOnly`。

Skill 分层由 `$lx-codex-workflow` 维护，模型 eval 由 `$lx-model-eval` 维护；本书只解释公开使用方式，避免形成第二套指令源。
