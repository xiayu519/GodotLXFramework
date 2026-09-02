---
name: lx-migrate
description: 将旧 LX、其他 Godot 或其他引擎游戏升级、移植或复刻到当前 LX；不用于从零开发。
---

# LX 游戏迁移

完整读取 `references/migration-workflow.md`。先选择 `upgrade`、`port` 或 `remake`，运行只读 `./lx.ps1 migrate plan`；来源不得覆盖当前框架、工具或 Codex 工作流。

只读计划阶段执行一次 `migrate plan`；源码可读时再有界检查启动入口、模块组合、脚本加载/解释、内容 schema、状态与存档等架构承载文件，不逐内容枚举。最终摘要先回显实际命令，再按原始分类名、引擎、模式、来源驱动架构、目标骨架决策和批量门禁汇报并停止；不加载产品 Skill，不运行 `inspect`、runtime、smoke 或 `validate`。

进入实现后追加 `$lx-game` 并读取产品驱动架构 reference：有合理源码架构时先语义复刻架子，再实现模块，然后批量转换脚本/数据；无源码或原架构不可复用时先建立目标驱动骨架。只为实际职责追加 `$lx-ui` 或 `$lx-input`；涉及资源闭环、存档格式或 Luban 上游事实才分别使用 `$lx-resources`、`$lx-persistence`、`$lx-data`。

完成以来源分类、授权边界、驱动架构、模块契约、脚本/数据全量验证、产品 smoke、所需实时快照、视觉证据和最终 `validate` 为准。
