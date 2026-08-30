---
name: lx-capabilities
description: 维护 LXFramework Capability 目录、命令元数据、副作用分类与 verifyRecipe；普通命令使用不触发。
---

# LX Capability 目录

完整读取 `references/capability-catalog.md`。命令路由、调用形式、副作用、前置条件和验收 recipe 必须与类型化目录一致；能力查询不扩大用户授权。

运行时快照协议使用 `$lx-runtime-observe`，doctor/upgrade 事务使用 `$lx-maintenance`，旧游戏来源规划使用 `$lx-migrate`。修改后运行相关 `check`、工作流检查和最终 `validate`。
