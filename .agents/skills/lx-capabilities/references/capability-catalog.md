# Capability 目录契约

`./lx.ps1 inspect` 刷新 `.lx/capabilities.json`；只查询单个领域时使用 `./lx.ps1 capabilities <id> --json`，结果写入 `.lx/capability-<id>.json`。目录由 `CapabilityCatalog.cs` 的类型化元数据生成，不复制到 `AGENTS.md`、其他 Skill 或手写清单。

每条命令声明调用形式、种类、副作用、前置条件和 `verifyRecipe`。新增、删除或改变根命令时同步元数据，并验证 C# 或 PowerShell 路由真实存在。

副作用按命令可能造成的最大变化声明：完全不写入才是 `ReadOnly`；只写忽略目录 `.lx/` 是 `LocalArtifact`；可能更新生成输出、UID、视觉/API 基线或导出产物是 `ProjectMutation`。同一根命令的 plan/apply/rollback 必须拆成独立条目。

能力查询只描述已有入口，不能扩大当前任务授权。普通会话只查需要的单个 capability；只有跨模块控制面诊断才读取完整目录。
