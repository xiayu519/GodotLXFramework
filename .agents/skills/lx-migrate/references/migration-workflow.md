# 游戏升级、移植与复刻

当任务要把旧 LX 游戏、其他 Godot 项目或其他引擎游戏带到当前 LXFramework 时读取。本流程迁移游戏语义与产品所有内容，不用旧项目覆盖当前框架、工具或 Codex 工作流。

## 选择模式

- `upgrade`：源是旧 LXFramework 或旧 Godot 产品；保留产品事实、源码、场景和授权资产，在当前 checkout 重新生成并适配 API。
- `port`：源代码和资源可读，但使用其他 Godot 架构或其他引擎；提取玩法、UI、输入、数据、存档、音频和资源语义，再映射到 LX。
- `remake`：主要依据成品、行为记录或跨引擎设计复刻；不机械翻译引擎代码，只复现已确认且获授权的行为与内容。

来源、模式或资产授权会改变结果且用户未给出时，先集中确认；否则直接生成只读计划。来源与模式已知时只执行第二条，不先查询 Capability：

```powershell
.\lx.ps1 capabilities migration --json
.\lx.ps1 migrate plan --source <directory|git-ref> --mode upgrade|port|remake
```

从外层包装器运行时，命令显示的 `.lx/migration/` 相对 `godot_project/`，无需搜索报告路径。stdout 已给出 plan ID、`mode`、`engine`、文件总数和非空分类计数，足以完成只读规划。

计划阶段必须保留原始分类名：`ProductFact`、`ProductSource`、`Data`、`Scene`、`Asset`、`Integration`、`Generated`、`BuildArtifact`、`FrameworkOwned`、`DocumentationOrLicense`、`ManualReview`；只汇报本次出现的分类，并明确 `Generated`/`BuildArtifact` 丢弃、`FrameworkOwned` 不覆盖。计划只写忽略的 `.lx/migration/`，不切换分支、不复制文件、不改源项目。当前 checkout 的确定性派生文件仍使用 `upgrade --plan|--apply`，不要把两种 upgrade 混为一谈。

执行恰好一次 `migrate plan` 后，根据 stdout 判断是否存在可读 `ProductSource`。有源码时只检查架构承载文件：启动/组合入口、主循环或状态切换、模块接口、脚本加载器/编译器/解释器/操作码注册、内容 schema、状态与存档，以及每种脚本格式的一份代表样本；用 `rg --files` 有界定位，不逐地图、任务或剧情枚举。记录来源由什么驱动、覆盖哪些模块、脚本和数据如何进入运行时，以及命令的立即完成、等待、并发启动、汇合/停止、取消、异常和生命周期所有权如何持续；不要把来源 `async` 方法或返回类型直接等同于脚本是否等待。把每个架构部分标为 `Preserve`、`AdaptToLX` 或 `Replace`。架构合理且不违反 LX 不变量时优先语义复刻，不因引擎不同而从第一段内容重写。

无源码、只有成品/行为记录，或来源架构不可复用时，先提出目标驱动骨架；具有大量剧情、过场、任务、场景、人物、对话或战斗入口等同构编排时，默认评估统一事件脚本运行时，而不是逐步骤硬编码。`bin`、`json`、`txt`、Luban 或自定义格式应通过导入/编译归一到单一中间表示，脚本调用类型化产品模块；逐帧移动、高频战斗、AI 和网络权威仍由确定性模块负责。

只读计划的停止条件是：最终摘要回显实际命令，报告引擎/模式与非空分类、授权边界、来源驱动架构或无源码判断、目标骨架和批量验收门禁，然后立即停止。不读取计划 JSON，不加载产品 Skill，不运行 `git status`、`inspect`、runtime、smoke、视觉比较、构建或 `validate`；这些属于实现阶段。

## 实施边界

1. 新目标从当前 `main` 建产品分支；框架、工具、根文档和 Codex 工作流以目标 checkout 为准。
2. `upgrade` 迁移产品所有输入，丢弃生成物和构建产物；生成后编译定位手写 API 漂移。
3. `port`/`remake` 先形成来源驱动架构、LX 能力映射和目标产品骨架；禁止把来源的服务定位器、事件总线、生命周期、资源、场景、对象池或 UI 管理器复制成第二套系统。
4. 跨引擎场景和脚本是语义输入，不是可直接提交的 LX 结构；资产只有在来源与许可明确时复用。
5. 存档格式、坐标/单位、时钟、随机性、输入边沿、动画时序和碰撞规则必须显式记录，否则只能标为未验证差异。
6. 有合理源码架构时按“架子、模块、脚本/数据本地化”实施；无源码时按 `$lx-game` 的产品驱动架构建立执行器与模块。代表切片只证明架构契约，禁止成为逐内容手写的开发顺序。

复刻实施中的缺陷按 `$lx-game` 产品架构 reference 的公共机制缺陷门禁归因，并额外把来源引擎行为与目标 Godot/LX 本地化差异作为共享层候选。无法从源码、成品行为、设计说明或代表样本判断原作预期及目标等价语义时，必须暂停并向开发者列出证据、未决选择和影响；确认前不得用当前样本硬编码，也不得擅自改变公共契约。

需要跨模块映射时运行 `inspect --product-coverage`。结果只说明静态使用情况；未使用的服务不是缺陷。目标为框架展示时，把非自然能力放入独立 Framework Lab 或 smoke，不污染主玩法。

## 验收闭环

迁移先完成驱动骨架和一个代表架构契约，再批量转换全部脚本/数据并统一验证：

```powershell
.\lx.ps1 check <changed-path> [...]
.\lx.ps1 smoke product all
.\lx.ps1 visual compare product
.\lx.ps1 validate
```

状态型产品应通过现有 `LX.Metrics`/结构化日志暴露少量业务事实，例如游戏状态、生命、分数、关键库存/冷却、活动实体和池借出数；不新建诊断服务。用户或开发会话已经启动可持续运行的 Editor/Debug 实例时，AI 应主动运行 `runtime status --json`，再按任务读取 `runtime snapshot ui|features|resources|input|metrics --json`，而不是只凭截图或日志推测；运行时桥只读并要求当前 `sessionId/generation`。没有活动会话时明确说明未取得实时快照，并以可退出的 product smoke 验证确定场景，不能声称已经观察运行状态。

产品 smoke 由 `game-manifest.json` 的 `productSmokes` 声明，Debug 与 export 共用同一 marker/timeout 契约；旧清单的 `exportSmokes` 仍兼容，但不能与新字段同时声明。产品视觉由 `visualTargets` 声明，基准只有人工确认后才能 approve。

交付证据至少包括：框架所有文件未被来源覆盖、生成/构建产物未迁移、来源与目标驱动架构有据可查、架构契约行为通过、同类第二份内容可仅靠脚本/数据接入、全部脚本可解析且引用闭合、批量行为验证、产品 smoke、需要的实时快照、产品视觉结果、重开资源闭合和最终 `validate`。无法合法读取来源、关键行为无判定标准或资产授权不明时停止并请求方向。
