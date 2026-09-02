---
name: lx-maintenance
description: 维护或诊断 doctor/upgrade 的计划、应用、回滚与中断恢复事务；环境查询和游戏迁移不触发。
---

# LX 事务维护

完整读取 `references/maintenance-transactions.md`。项目 mutation 必须先有计划、前后哈希、备份、journal 和验证；冲突或非终态不得报告成功。

只读询问中断恢复、哈希冲突或成功终态时，依据该 reference 直接回答并停止；不搜索仓库、不读取事务实现或其他文档。只有诊断实现故障或修改事务代码时才读取源码。

系统安装等外部 mutation 需要用户明确授权。旧游戏/跨引擎来源使用 `$lx-migrate`；Capability 元数据本身使用 `$lx-capabilities`。修改后运行相关 `check` 和事务场景验证；达到根 `AGENTS.md` 的仓库级门禁时才运行 `validate`。
