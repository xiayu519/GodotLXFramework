---
name: lx-migrate
description: 将旧 LX、其他 Godot 或其他引擎游戏升级、移植或复刻到当前 LX；不用于从零开发。
---

# LX 游戏迁移

完整读取 `references/migration-workflow.md`。先选择 `upgrade`、`port` 或 `remake`，运行只读 `./lx.ps1 migrate plan`；来源不得覆盖当前框架、工具或 Codex 工作流。

只读计划阶段执行一次 `migrate plan` 后，最终摘要先回显实际 `migrate plan` 命令，再按输出的原始分类名、引擎、模式、纵向切片和门禁汇报并停止；不加载产品 Skill，不枚举来源，不运行 `inspect`、runtime、smoke 或 `validate`。

进入纵向切片实现后，只为实际职责追加 `$lx-game`、`$lx-ui` 或 `$lx-input`；涉及资源闭环、存档格式或 Luban 上游事实才分别使用 `$lx-resources`、`$lx-persistence`、`$lx-data`。

完成以来源分类、授权边界、纵向切片、产品 smoke、所需实时快照、视觉证据和最终 `validate` 为准。
