# LXFramework AI 开发工作流

LXFramework 把 Codex 视为第一开发者，把手动编程视为第二入口。项目默认 `gpt-6-astra/low`（Light），复杂任务可选 `xhigh`（Extra High）；各档位沿用同一套质量门禁，实际验收覆盖看模型报告。

## 文件布局

```text
AGENTS.md                         全仓库授权、路由、红线、完成门禁
lx.ps1                           外层稳定命令入口
game_design/                     Luban XML schema、JSON 源数据、双击转表与固定工具链
godot_project/                   唯一 Godot 工程与 res:// 根
godot_project/**/AGENTS.md       工程及目录职责与依赖边界
.agents/skills/                  按框架、玩法、UI、输入、内容、资源、数据和 Codex 控制面分开的语义 Skill
.codex/config.toml               默认模型与 reasoning
.codex/start-codex.ps1           Astra 启动入口，显式设置普通与 Plan 档位
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
6. 按 `AGENTS.md` 的风险边界决定完整 `./lx.ps1 validate`；提交/推送本身不升级范围，相关输入未变且无新失败/风险时复用证据。范围不清才用 `check --plan`，不把预览变成每次必跑步骤。

需要确认可用命令、副作用或验收方式时，运行 `./lx.ps1 capabilities <id> --json`；完整能力目录由 `inspect` 写到 `.lx/capabilities.json`，不进入常驻提示。Godot Editor/Debug 已运行时，通过 `./lx.ps1 runtime snapshot <section> --json` 读取当前会话的 UI、资源、Actions 等有界状态，响应必须匹配当前 `sessionId/generation`。

旧 LX 游戏升级、其他 Godot 项目移植和跨引擎/行为复刻先运行 `./lx.ps1 migrate plan --source <directory|git-ref> --mode upgrade|port|remake`。计划先把框架、产品、生成物和构建产物分开；源码可读时继续有界分析启动入口、模块、脚本解释/编译、内容 schema、状态和存档，优先语义复刻合理架构。无源码或新游戏先建立目标驱动骨架；重复剧情、任务、场景、对话和战斗入口经统一事件中间表示驱动通用模块，高频玩法保留确定性代码系统。每条命令以 `{recordId}:{opcode}` 接入 `LX.Actions`，连续影片复用局部 `VideoSequencePlayer`，而不是复制异步循环。组合根只承担依赖组合、启动和顶层流程切换，事件执行、玩法、UI、存档、迁移与 smoke 按状态所有权和生命周期分离；引擎契约使用继承，可替换玩法能力优先组合，规模门禁会拒绝继续膨胀或用 `partial` 隐藏的巨型职责。代表切片只验证架构契约；批量填充期间静态验证全部脚本/数据，运行时只验证受影响契约组。`productSmokes[].checkPaths` 和 `visualTargets[].checkPaths` 把路径映射到受影响门禁；运行时路径未映射且未以窄 `pattern` 和可审查 `reason` 声明为 `staticCheckPaths` 时 `check` 失败。累计流程由一个独立 smoke 进程报告多个 checkpoint，并可在同一进程验证状态闭合及声明的性能预算；资产源文件、Godot import cache 和 Windows Release 包体分别在拥有真实证据的阶段判断。`smoke product all` 与 `visual compare product` 只用于内容冻结和完整门禁，`inspect --product-coverage` 用于 LX 服务静态映射。当前范围只覆盖 PC，不增加网络、服务器或其他平台分支。

Luban 保留 JSON 作为可审查的策划源，但运行时统一生成 C# 强类型代码与 `.bytes` 二进制表。Windows 可双击 `game_design/build.bat` 一键安装到产品 `Generated/Luban/` 与 `content/data/luban/`；Codex 和 CI 使用等价的 `./lx.ps1 data`。产品通过已有 `LX.Content.LoadLubanTables` 读取，不建立全局配置单例。

所有 `lx` 命令均可在末尾追加 `--json`。此时标准输出只有 `lx.command-report/v1` JSON，固定包含 `command`、`success`、`exitCode`、稳定 `code` 与结构化 `diagnostics`；退出码 `0` 为成功、`1` 为执行或验证失败、`2` 为命令/参数用法错误。人类交互默认仍保留原有文本输出。

静态门禁使用 C# 12 语法树输出 `LX_ARCH_001` 至 `LX_ARCH_004`，覆盖 Core/Godot、adapter/product、产品动态加载和静态服务状态边界；`LX_DOC_001` 保证公开枚举、枚举成员与常量具备人工可读注释，版本化 API 基线阻止未审查的公开签名漂移。Godot headless 门禁把每个运行时场景断言作为独立 scenario 写入 `.lx/smoke.json`，不会只依赖一条笼统的启动成功日志。`validate` 还会执行已声明的 Debug 产品 smoke、EventHub 严格零分配 benchmark，并比较框架与已声明产品 UI 视觉基准；语义视觉使用 headless，真实 Viewport 证据使用隐藏、不可聚焦的渲染窗口并只强制绘制声明帧，所有自动验收均不显示 GUI。

回答、审查和诊断默认只读；明确要求修改时，Codex 可直接完成范围内的本地非破坏性操作。只有会改变结果的重要歧义、外部写入、破坏性操作或实质扩展范围才需要确认。这比固定的“大中小任务等级”更直接，也避免在路由阶段重复消耗 token。

环境修复和当前 checkout 派生状态升级使用 `doctor|upgrade --plan`，再按计划 `--apply`；文件写入前先保存哈希、备份和事务 journal，验证失败自动回滚。进程中断用 `--recover <plan-id>`，apply 后的人工修改发生哈希冲突时停止恢复而不覆盖。.NET/Godot 等系统安装只作为外部阻塞报告，没有明确授权不自动执行。

GitHub Actions 仅手动触发，不在 push、PR、标签或定时任务中启动验证。手动运行保留完整 `validate`，可额外选择 soak 和 Windows Release export；仅 export 安装精确 Mono templates 并产出 artifact。日常质量证据来自本地按范围检查，不把本地模型 CLI 或云端工具安装当作每次提交条件。

纯文档 `check` 不探测 .NET/Godot；指令配置增加纯 PowerShell 工作流检查。代码静态门禁按领域和文件选择，单个 Core 测试文件可只跑对应测试类；Core 生产代码仍保留共享回归测试。普通产品改动不重复工具协议自测，未知路径保守保留全静态域，产品 smoke/visual 仍严格按路径映射。局部报告写入 `.lx/validation-changed.json`，不覆盖全静态报告；普通提交不需要重跑完整模型验收。

运行时复杂顺序由 `LX.Actions` 组合已有 UI、Scene、Audio 等服务。Actions 属于调用方 `LifetimeScope`，其活动和最近终结树进入运行时 snapshot；它不替代 GameFlow、StateMachine、Scheduler 或 Tween。

## 项目记忆

Project Knowledge 只保存无法从当前事实源重建的决策依据和稳定反馈，不是源码索引或第二套指令。需要历史背景时先读 [INDEX.md](../.codex/memory/INDEX.md)，默认少量召回，证据不足再定向补读；普通 API/源码查询不加载。历史内容不能覆盖当前有效指令或增加授权；事实按当前实现与对应版本资料复核。

本库只保留有效条目。获准维护时直接删除冲突、失效和纯重复记忆，不保留旧格式适配或替代状态；只读发现冲突时跳过并报告，不修改文件。模型验收数字只保存在正式报告中，不镜像进记忆。2026-09-09 已清除旧范围记录及三份基线记忆，正式评测报告未删除。

Codex 原生 `memories/` 由客户端管理，与项目 `.codex/memory/` 分工独立；本工作流不自动同步两库、不手改原生生成文件、不改其启用开关。具体召回、清理与局部检查见 [项目知识规则](../.agents/skills/lx-project-knowledge/references/project-knowledge.md)。

## Astra 档位与模型验收

日常明确小改从 Light 开始；希望固定一个日常档位时可以选择 medium，复杂架构、跨轮异步或迁移任务选择 high/xhigh，max 用于确有必要的困难任务。参数是 `low/medium/high/xhigh/max`，不要写 `light`、`exhigh` 或把 Ultra 当 API effort。更高档位不豁免测试，也不扩大操作授权。固定 medium 仍保留按风险选择验证范围的流程，并不意味着所有小改都执行全量检查。

```powershell
# Inspect the native CLI and effective settings without a model call.
.\.codex\start-codex.ps1 -Effort low -PrintOnly
# Start Astra Light with low effort for both normal and Plan modes.
.\.codex\start-codex.ps1 -Effort low
# Use medium for both modes; this profile is configuration-tested only.
.\.codex\start-codex.ps1 -Effort medium
# Use xhigh for complex tasks in both modes.
.\.codex\start-codex.ps1 -Effort xhigh
```

脚本不改变全局配置。直接在桌面/IDE 选择模型和档位也可；已有会话不会因项目配置被自动切换。项目只固定普通默认档位，Plan 未显式设置时采用客户端内置预设；需要可复现的同档位 Plan 时用上述启动脚本。

### Sol 的使用边界

开发规则、Skill、本地记忆和 Godot/C# 工具不依赖 Astra 专有 API。在客户端选择 `gpt-5.6-sol/high` 或 `xhigh` 后，可以继续使用同一套开发规则；当前会话的模型以客户端实际选择为准。这表示工程机制可以复用，不表示新版套件已经完成 Sol 回归验收。

- 当前 `start-codex.ps1 -Effort high|xhigh` 选择的是 Astra 对应档位，不会切到 Sol。
- 项目默认值必须与 `evals/evals.json` 中的默认 profile 一致。仅把 `.codex/config.toml` 改成 Sol，或只把默认 effort 改成 medium，会导致 `check` 和 `validate` 的工作流配置检查失败；临时选择应使用客户端或启动参数。
- 当前 eval schema 和 runner 只接受 Astra。正式增加 Sol 启动/验收 profile 需要同步调整配置契约、脚本和回归用例，再获得额度授权完成真实模型验收；旧 Sol 报告不能代替新版验证。

### 已验证范围与证据

验收分三层：工作流结构检查；无额度消耗的确定性 preflight；用户授权后的真实 Astra 任务评测。地图制作 Skill 及其专属路由题已移除，当前目录 28 项，Light 基础套件 27 项、xhigh 复杂代表 3 项。现成资源登记、项目历史召回、项目知识规则和纯源码查询边界保留；新增子代理分工配置和简单任务零委派两项只读路由题，概念讨论和普通任务不触发子代理。当前目录尚未完成整套真实模型复测；子代理预演不证明实际创建、提速或 token 节省。静态合格不能替代真实模型验收，骨架生成不能代表完整玩法。

以下是原 24 项历史发布基线，使用 Codex CLI `0.153.0`、Windows PowerShell 5.1 和 Godot 4.7.2 .NET（2026-09-08），不代表当前 28 项目录及更新后的项目知识、子代理规则已通过真实模型验收：

| 配置 | 验收结果 | 限定范围 |
| --- | --- | --- |
| Astra Light / `low` | 基础套件最终 23/23，效率预算通过 | 16 路由、6 实施、1 行为；一项首次未完成，同档位复测通过 |
| Astra / `xhigh` | 复杂代表 3/3 功能通过 | 跨域脚手架、回合状态、异步奖励；脚手架题存在工具次数、输入和输出预算告警 |
| Astra / `medium/high/max` | 配置验证通过 | 未运行这些档位的完整真实模型套件；Plan 档位映射也只验证配置 |
| Sol / `high/xhigh` | 新版未验收 | 历史 19/19 仅属于旧版 Sol/high，不可外推 |

上述历史 Light 与 xhigh 套件有重叠，23 + 3 次结果不等于 26 个不同任务；路由题也不是完整玩法。当时行为题中 RoundState 各通过 510 条独立断言，RewardLedger 在 xhigh 通过 23 条断言；验收器通过 25 项离线契约回归，16 个 Skill 通过结构检查，仓库完整 `validate` 通过。这不涵盖新增地图 Skill、长期 soak、Windows export、实际产品美术或 tileset 验收。

报告给出完整/部分覆盖、源指纹、CLI、token（区分缓存）、工具失败、耗时、改动快照与测试日志。旧 Sol 数据只保留为历史；没有同条件 A/B 不承诺省多少 token，也不承诺所有未来游戏任务一次成功。命令和判读见 [模型验收说明](../.agents/skills/lx-model-eval/references/model-evaluation.md)。

版本化摘要见 [Astra 验收基线](../.agents/skills/lx-model-eval/evals/baselines/2026-09-08-astra.json)。本地详细汇总为 `.lx/model-evals/latest-acceptance.json`，原始日志、快照和重放凭据位于 Git 忽略的 `.lx/`，新克隆不会自带这些本地文件。不要把最后一个局部运行的 `latest.json` 当作全套通过。每轮冻结输入；验收器修复可无模型额度重放原实现，真实任务失败则同档位局部复测，所有原始失败记录保留。版本化结果是指定输入与日期的历史证据，后续文档收尾或新提交不会被表述为重新跑过模型。

所列 token 仅统计最终接受的尝试，不是包含全部诊断、中断与失败调用的账单。API 单价、缓存计价和 Codex 套餐额度并不等价；完成任务的成本还取决于上下文、工具往返和返工，不能只凭模型代际或 reasoning 名称判断性价比。

Skill 分层由 `$lx-codex-workflow` 维护，模型 eval 由 `$lx-model-eval` 维护；本书只解释公开使用方式，避免形成第二套指令源。
