# LX AI Control Plane

只在维护 Capability、运行时桥、结构化快照或事务化维护命令时读取。

## 能力目录

`./lx.ps1 inspect` 会刷新 `.lx/capabilities.json`；只需单个领域时运行 `./lx.ps1 capabilities <id> --json`，再读取 `.lx/capability-<id>.json`。能力目录由 `CapabilityCatalog.cs` 的类型化命令元数据生成，不把目录复制到 `AGENTS.md`、Skill 或手写清单。

每条命令声明调用形式、种类、副作用、前置条件和 `verifyRecipe`。新增、删除或改变根命令时同步元数据，并让静态验证确认 C# 或 PowerShell 路由真实存在。能力查询本身不能扩大用户授权。

旧游戏升级、其他项目移植或跨引擎复刻先查询 `migration` capability，再用 `migrate plan` 生成只读来源盘点。计划只能写 `.lx/migration/`，不得切分支、复制来源或自动翻译手写代码；确定性当前 checkout 派生状态仍由 `upgrade --plan` 负责。

副作用分类按“命令可能造成的最大变化”声明：完全不写入才是 `ReadOnly`；只写忽略目录 `.lx/` 是 `LocalArtifact`；可能更新生成输出、UID、视觉基准、API 基线或导出产物是 `ProjectMutation`。同一根命令的 plan/apply/rollback 等形态必须拆成独立条目，静态验证会拒绝 `ReadOnly` 携带副作用、`LocalArtifact` 写项目文件或 `ProjectMutation` 只写 `.lx/` 的错误分类。

## 运行时桥

桥只在 Godot Editor/Debug 构建启用，当前只接受只读 snapshot。CLI 先验证活动进程、心跳、`sessionId` 与 `generation`，再读取对应响应；不能把旧会话文件当成当前证据。快照必须有界，按 `runtime|events|scheduler|actions|metrics|resources|ui|features|audio|input|localization|settings|logs` 分区，普通任务只读取需要的一份。

操作入口与拒绝契约如下：

```powershell
.\lx.ps1 capabilities runtime --json
.\lx.ps1 runtime status --json
.\lx.ps1 runtime snapshot ui --json
```

第一条用于按需发现运行时命令、副作用、前置条件和验收 recipe；第二条查当前会话；第三条只查 UI 分区。客户端必须同时匹配 `sessionId` 和 `generation`，任一不匹配就拒绝旧运行响应。只读问题若只询问能力发现、分区快照或旧会话字段，以本段作为直接仓库契约；要点齐全后停止，不再枚举 `AGENTS.md`、全仓搜索或读桥的完整实现。只有诊断具体协议故障时才读 `RuntimeBridgeClient.cs` 或 `RuntimeBridgeService.cs`。

`.lx/runtime/session.json`、`request.json` 和 `response.json` 是内部传输文件，不是 Codex 公开 API。Codex 禁止直接读写它们来替代命令；即使当前没有运行会话，也应说明上述 CLI 用法，而不是尝试读内部 JSON。

不要通过桥引入第二套事件总线、服务定位器或任意代码执行。未来增加 mutation 时必须逐项声明权限、副作用、前置条件和可执行后验验证，默认仍为只读。

## 事务化维护

`doctor --plan` 同时报告环境阻塞和当前 checkout 可确定修复的派生文件；`upgrade --plan` 只表示把产品派生状态升级到当前 LX checkout，不宣称下载或替换框架源码。`--apply` 只处理计划中带前后哈希的项目内生成文件：先准备备份和每项哈希，写入 `Prepared/Applying` journal 后才逐项原子修改，验证成功进入 `Applied`，失败安全回滚。

进程被终止后使用 `--recover <plan-id>` 恢复 `Prepared`、`Applying` 或 `RecoveryRequired` 事务。`--rollback` 与 `--recover` 都先比较当前哈希；文件若在 apply 后被人工继续修改，事务进入 `RecoveryRequired` 并停止，禁止覆盖该修改。事务终态为 `Applied` 或 `RolledBack`，任何非终态都不能作为维护成功证据。

.NET、Godot 或其他系统安装属于外部 mutation：计划可以报告，但没有明确授权不得自动应用。不要让事务引擎接管产品资产、手写源码或未知第三方文件。

## 上下文与成本

能力目录和快照位于忽略目录 `.lx/`，不进入常驻提示。优先查询单个 capability 或 snapshot section；只有跨模块诊断才读取完整目录或 `all`。评估变化时分别记录根/嵌套 `AGENTS.md`、Skill 入口字节数、按需 reference 字节数、命令输出大小和真实模型 eval token，不能用文件数量代替 token 结论。

公开 API 由 `lx api check|update` 的版本化基线约束；`validate` 固定运行严格 EventHub allocation benchmark，但多轮 `lx soak` 与 Windows Release export 留在定时、标签或人工 CI，不增加普通本地迭代成本。
