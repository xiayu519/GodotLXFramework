# 玩法与产品验收

产品代码通过注入的 `LX` 使用场景、Feature、内容、音频、时钟、随机、设置等能力；产品类型只承载游戏语义，不建立转发框架 API 的管理器。高频节点使用 `NodePool<TNode>`，池由所属 Feature 或世界的 `Lifetime` 持有；复杂资源所有权改用 `$lx-resources`。

跨模块结构确实不清楚时才运行 `./lx.ps1 inspect`。以框架 API 展示为目标时，用 `api/LXFramework.PublicApi.txt` 与 `inspect --product-coverage` 把能力分成主玩法、独立 Framework Lab、product smoke 和不适用；不要为表面覆盖把危险维护入口或互斥策略塞进主玩法。

先交付启动、核心操作、状态变化、死亡或胜利、重开的纵向切片。连续重开覆盖 UI、Feature、音频、资源租约和池借出闭合，预热后产品节点、资源和资产不持续增长。用现有 `LX.Metrics` 或结构化日志暴露少量业务事实，不新建诊断服务。

`content/game/game-manifest.json` 可声明会自行退出的 `productSmokes`。有活动 Editor/Debug 会话且任务需要实时状态时使用 `$lx-runtime-observe` 读取对应 snapshot；没有活动会话时只报告未取得实时证据，并运行确定性的 product smoke。

```powershell
.\lx.ps1 check <changed-path> [...]
.\lx.ps1 smoke product all
.\lx.ps1 validate
```

涉及页面布局与视觉基准使用 `$lx-ui`，涉及输入动作使用 `$lx-input`。`validate` 是最终门禁；Windows export 仅在已安装精确模板且任务要求交付包时运行。
