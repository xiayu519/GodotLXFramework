---
name: lx-persistence
description: 处理游戏存档、版本迁移、备份、槽位与删除；不处理普通玩法状态。
---

# LX 游戏存档

完整读取 `references/persistence-workflow.md`。只读契约问题依据该 reference 直接回答并停止；实现任务只在确实修改产品源码时额外使用 `$lx-game`。

保持存档迁移显式、原子替换和备份回退，不把设备设置混入游戏进度。删除或覆盖真实存档需要用户明确授权。修改后运行相关 `check` 和最终 `validate`。
