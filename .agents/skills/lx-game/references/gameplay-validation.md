# 玩法与产品验收

产品代码通过注入的 `LX` 使用场景、Feature、内容、音频、时钟、随机、设置等能力；产品类型只承载游戏语义，不建立转发框架 API 的管理器。高频节点使用 `NodePool<TNode>`，池由所属 Feature 或世界的 `Lifetime` 持有；复杂资源所有权改用 `$lx-resources`。

跨模块结构确实不清楚时才运行 `./lx.ps1 inspect`。以框架 API 展示为目标时，用 `api/LXFramework.PublicApi.txt` 与 `inspect --product-coverage` 把能力分成主玩法、独立 Framework Lab、product smoke 和不适用；不要为表面覆盖把危险维护入口或互斥策略塞进主玩法。

具有重复流程或批量内容时，先按 `product-architecture.md` 搭好驱动骨架，再交付覆盖启动、核心操作、状态变化、死亡或胜利、重开的架构契约切片；禁止把切片扩展成逐剧情、逐地图或逐任务的手写实现。连续重开覆盖 UI、Feature、音频、事件运行时、资源租约和池借出闭合，预热后产品节点、资源和资产不持续增长。用现有 `LX.Metrics` 或结构化日志暴露少量业务事实，不新建诊断服务。

`content/game/game-manifest.json` 可声明会自行退出的 `productSmokes`。每项使用 `checkPaths` 登记会影响该场景的 Godot 根相对 glob，例如公共事件运行时可登记 `script/MyGame/EventRuntime/**`，某条剧情可登记 `content/story/chapter_01/**`；同一公共路径可以映射多个 smoke。独立测试入口用 `scenePath` 与正式 `GameRoot` 分离；一条累计路线用 `checkpoints` 在一个进程报告多个稳定阶段，避免反复执行相同前缀。需要闭合所有权时通过 `ProductSmokeProbe.Snapshot` 发出前后快照，并在 `statePolicy` 选择 `resources/ui/features/audio/input/actions` 及产品池的 `LX.Metrics` gauge。运行器流式扫描日志，只在报告保留有界 tail、耗时、日志大小和失败阶段。

`visualTargets[].checkPaths` 以同样方式映射受影响视觉目标。`check` 会输出每个产品运行时路径对应的 smoke/visual 门禁；确实只有生成或静态闭合要求的路径才可登记到有边界的 `staticCheckPaths`，每项必须给出窄 glob 和可审查的 `reason`。产品存在时，运行时路径若三者均未命中会失败，不允许以“没有受影响场景”为由成功跳过。`all` 只用于内容冻结和仓库级门禁。

有活动 Editor/Debug 会话且任务需要实时状态时使用 `$lx-runtime-observe` 读取对应 snapshot；没有活动会话时只报告未取得实时证据，并运行确定性的受影响 product smoke。验证按阶段收窄：架构期运行代表契约场景；批量填充期运行路径级 `check` 和受影响契约组；单点缺陷先运行失败场景、同契约代表样本和一个不受影响对照，共享层修复才扩到该层映射的全部场景。失败后只重跑失败项与受影响集合，不因局部修改重跑全部产品 smoke 或全部视觉目标。

```powershell
.\lx.ps1 check <changed-path> [...]
.\lx.ps1 smoke product <affected-id> # 仅在需要显式复现某个行为时补跑
.\lx.ps1 validate                    # 仅在根 AGENTS.md 定义的仓库级门禁运行
```

涉及页面布局与视觉基准使用 `$lx-ui`，涉及输入动作使用 `$lx-input`。`validate` 会运行全部产品 smoke 与视觉目标；Windows export 仅在已安装精确模板且任务要求交付包时运行。
