# Astra 多档位模型验收

## 契约与范围

`evals/evals.json` 是唯一模型/档位/用例目录。默认 `astra-light = gpt-6-astra/low`；`astra-xhigh = gpt-6-astra/xhigh`。medium/high/max 可配置但未实测前不得宣称兼容验收通过。

26 项分为 18 项路由题、6 项实施题、2 项可执行行为题。基础 foundation 为 25 项（Light），复杂 complex 为 vertical-slice、stateful-round、async-reward-restart 三项（xhigh）。full 是全部 26 项；smoke 仅两项。名称、选择范围和覆盖以 schema 为准。

地图制作 Skill 及其专属路由题已移除；保留现成图片的静态资源登记题和项目历史召回题。项目知识规则题仅保留有效条目、维护授权后删除冲突记忆，纯源码查询保留不读记忆的负向边界。这些变化仅做离线契约和用例选择检查，不触发完整引擎 preflight。原 24 项历史报告不覆盖新增或修改后的路由，也不证明当前 Skill 已完成真实模型验收。

- 路由题检查 Skill 正负路由、只读边界和关键语义，不代表玩法实现正确。
- 原实施题检查脚手架、注册、上下文注入、生成纪律与最终 validate。
- `vertical-slice` 是为兼容旧 ID 保留的跨域脚手架题，不代表已接通可玩流程。运行时接线或新视觉目标不在其请求范围内。
- explicit-ui-followthrough 验证明确需求后真正实施静态全屏帮助页，而非按流程继续询问。
- stateful-round 用独立 C# 程序验证胜负、重开、边界和固定种子的 500 步状态序列。
- async-reward-restart 验证去重、并发、取消、异常回滚、重开代际隔离、溢出与锁外调用。
- 行为断言由父验收器读取本仓库 probe，不能被隔离副本内的模型改写。局部行为题不等于整款游戏性能、内存闭合或视觉验收；产品门禁仍需其自身证据。

## 执行

先做无模型调用的检查：

```powershell
.\.agents\skills\lx-model-eval\scripts\test-eval-contract.ps1
.\.agents\skills\lx-model-eval\scripts\run-model-evals.ps1 -PreflightOnly
```

获得用户额度授权后：

```powershell
.\.agents\skills\lx-model-eval\scripts\run-model-evals.ps1 -Profile astra-light -Suite foundation -AllowModelCalls
.\.agents\skills\lx-model-eval\scripts\run-model-evals.ps1 -Profile astra-xhigh -Suite complex -AllowModelCalls
```

`-PreflightReport <run>/preflight.json` 可复用源码指纹和 CLI 版本完全一致的预检；输入变动自动拒绝复用。`-CaseId <id>` 只跑受影响用例；不代表整套通过。默认累计三项失败停止以保护额度，剩余项明确标缺失；不得自动更换模型掩盖失败。

评测副本隔离于真实产品，禁用用户配置、rules、插件、外部应用和多代理，以固定环境；认证仍由本地 Codex 提供。此隔离仅是工作目录隔离，不是操作系统安全沙箱。不修改全局配置，不提交或推送真实仓库；临时副本中的 baseline commit 只用于计算 diff。

## 报告与通过

报告保留源码/schema 哈希、CLI 路径和版本、所选模型/effort、实际 case IDs、缺失/额外/重复覆盖、Skill 读取、文件 diff、修改后的源文件、独立测试及 validate 日志。

每次先冻结 `inputs/` 并保存逐文件哈希，全部用例使用该快照。写入题由父验收器再次对实际改动运行 `check`，不能用 `validate` 成功覆盖路径级失败；生成的 `.uid` 仍保留在 diff 并由 Godot import 验证，不把未修改脚本的新 sidecar 当成额外玩法变化。Skill 访问和自然语言术语采用日志启发式，只是路由回归信号，不冒充完整语义证明。

`all_passed` 只表示所选用例全通过；`coverage.passed` 才表示指定套件完整通过；`full_coverage.passed` 才是全部 26 项通过。部分运行不会覆盖带套件名的完整报告。缺少 usage 不计为零成本成功；tool 的失败事件不是“模型自动重试次数”。

旧预算作为独立效率告警报告，不混入功能正确率，也不为消掉告警自动增大阈值；标注告警后复审重复读取/失败命令。必须同时关注质量和成本，不能为了 token 省略必要验证。不同模型的节省比例需同提示、同源码、同验收器、相近缓存条件的对照；本次授权只做 Astra 验收，不默认额外跑 Sol A/B。

静态通过不能证明模型兼容，一次真实通过也不保证未来所有任务成功。新基线逐项记录实际结果；老 Sol 报告保留为历史，不作为 Astra 或当前套件的证明。

## 无额度复核与最终汇总

`-PrintPlan` 可离线查看 profile、effort 和用例选择。Windows PowerShell 的直接脚本调用与 `-File` 调用都包含参数绑定回归。

若修复的是验收器而非模型实现，使用 `recheck-model-eval.ps1 -RunId <id> -CaseId <id>`：从原始输入和保留的模型文件重建隔离副本，重新执行响应断言、独立行为 probe、路径 check 和 validate；原始结果不改写，新证据写入 `latest-recheck.json`。重放目前要求本套件使用的干净框架输入；不能拿新实现替换原来的失败代码。

`summarize-acceptance.ps1 -RunId @('<run1>','<run2>',...)` 生成 `.lx/model-evals/latest-acceptance.json`。只允许各快照的验收脚本不同；任务提示、Skill 正文、AGENTS、probe 和产品输入必须一致。同 profile/case 取最后一次实际尝试，并保留全部尝试及重放证据；不挑选较早的成功来遮盖较晚失败。完整性、功能通过与效率告警分别报告。

未来重新发布或切换 CLI 时，先跑离线检查，再在额度许可后复测受影响任务。只有用例和输入完整一致的汇总可作为基线；文档中的历史计数不能代替当前报告。
