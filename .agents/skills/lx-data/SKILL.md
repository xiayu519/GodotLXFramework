---
name: lx-data
description: 处理 Luban schema、策划数据、生成与强类型表；不处理普通 JSON 内容。
---

# LX Luban 数据

完整读取 `references/data-workflow.md`，以 `game_design/schema` 与 `game_design/data` 为上游事实源。禁止手改生成的 C# 或 `.bytes`；使用 `./lx.ps1 data` 或让 `check` 按路径触发生成。

若任务还修改消费表数据的玩法或 UI，只为对应部分额外使用 `$lx-game` 或 `$lx-ui`。完成后运行相关 `check` 和最终 `validate`。
