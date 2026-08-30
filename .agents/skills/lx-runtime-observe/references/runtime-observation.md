# 运行时观测契约

桥只在 Godot Editor/Debug 构建启用，当前仅接受只读 snapshot。公开入口为：

```powershell
.\lx.ps1 capabilities runtime --json
.\lx.ps1 runtime status --json
.\lx.ps1 runtime snapshot ui --json
.\lx.ps1 runtime sample performance --duration 15 --interval 500 --json
```

第一条发现运行时诊断能力，第二条查询活动会话，第三条只取 UI 分区，第四条持续采样性能。支持的分区为 `runtime|events|scheduler|actions|metrics|resources|ui|features|audio|input|localization|settings|logs|performance`；普通任务只读取所需的一份。

`runtime sample performance` 的窗口为 1–60 秒，默认 15 秒、500 ms 聚合一次。可选门禁为 `--max-p95-ms`、`--max-p99-ms`、`--max-frame-ms`、`--max-heap-growth-mb`。固定容量环形缓冲在游戏进程内记录帧/物理帧 delta 和 LXHost 工作耗时；CLI 在本地汇总内存、GC 与关键所有权指标，并把详细报告写到 `.lx/runtime/performance/`。默认标准输出只有一行摘要；Codex 不读取原始时间序列，只有门禁失败后才按异常指标定向读取本地报告。

普通玩法、UI 或内容任务不得自动运行持续性能采样。只有用户明确要求性能、修改热点路径、准备发布或性能门禁失败时才使用；本地采样和 CI 门禁本身不产生模型 Token，只有把摘要或定向失败信息送入上下文才产生少量 Token。

CLI 必须验证活动进程、心跳、`sessionId` 和 `generation`，任一不匹配就拒绝旧响应。`.lx/runtime/session.json`、`request.json`、`response.json` 是内部传输文件，Codex 禁止直接读写它们替代命令。

只读询问命令或拒绝契约时，依据本页回答后停止，不枚举 `AGENTS.md`、全仓搜索或读取完整实现。只有诊断具体协议故障时才读取 `RuntimeBridgeClient.cs` 或 `RuntimeBridgeService.cs`。

没有活动会话时明确说明未取得实时快照，不能声称已经观察游戏状态；可以改用会自行退出的确定性 product smoke。桥不得引入任意代码执行、第二套事件总线或服务定位器。
