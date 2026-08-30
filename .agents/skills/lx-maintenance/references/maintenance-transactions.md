# doctor/upgrade 事务契约

`doctor --plan` 同时报告环境阻塞和当前 checkout 可确定修复的派生文件；`upgrade --plan` 只规划把产品派生状态升级到当前 LX checkout，不下载或替换框架源码。

`--apply` 只处理计划中带前后哈希的项目内生成文件：先准备备份和每项哈希，写入 `Prepared/Applying` journal 后逐项原子修改，验证成功才进入 `Applied`，失败安全回滚。

进程中断后用 `--recover <plan-id>` 恢复 `Prepared`、`Applying` 或 `RecoveryRequired`。`--rollback` 与 `--recover` 都先比较当前哈希；文件若在 apply 后被人工修改，事务进入 `RecoveryRequired` 并停止，禁止覆盖该修改。只有 `Applied` 或 `RolledBack` 是成功终态。

.NET、Godot 或其他系统安装属于外部 mutation：计划可以报告，但没有明确授权不得应用。事务引擎不得接管产品资产、手写源码或未知第三方文件。
