# 运行时观测契约

桥只在 Godot Editor/Debug 构建启用，当前仅接受只读 snapshot。公开入口为：

```powershell
.\lx.ps1 capabilities runtime --json
.\lx.ps1 runtime status --json
.\lx.ps1 runtime snapshot ui --json
```

第一条发现运行时诊断能力，第二条查询活动会话，第三条只取 UI 分区。支持的分区为 `runtime|events|scheduler|actions|metrics|resources|ui|features|audio|input|localization|settings|logs`；普通任务只读取所需的一份。

CLI 必须验证活动进程、心跳、`sessionId` 和 `generation`，任一不匹配就拒绝旧响应。`.lx/runtime/session.json`、`request.json`、`response.json` 是内部传输文件，Codex 禁止直接读写它们替代命令。

只读询问命令或拒绝契约时，依据本页回答后停止，不枚举 `AGENTS.md`、全仓搜索或读取完整实现。只有诊断具体协议故障时才读取 `RuntimeBridgeClient.cs` 或 `RuntimeBridgeService.cs`。

没有活动会话时明确说明未取得实时快照，不能声称已经观察游戏状态；可以改用会自行退出的确定性 product smoke。桥不得引入任意代码执行、第二套事件总线或服务定位器。
