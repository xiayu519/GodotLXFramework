# 存档契约

只读询问存档迁移、备份、槽位或删除时，本文件就是完整公开契约；回答后停止，不再读取通用模块表、源码、测试或嵌套 `AGENTS.md`。

- `SaveStore<TState>` 构造器通过 `IEnumerable<ISaveMigration>` 注册迁移器；每个迁移器必须从 `FromVersion` 前进到 `ToVersion = FromVersion + 1`。
- `SaveAsync` 原子替换主档，并把旧主档保留为 `.bak.json`。`LoadAsync` 先尝试主档，再自动尝试 `SaveSource.Backup`；成功结果的 `SaveLoadResult<TState>.Source` 表明实际来源，两者均失败返回 `save.load_failed`。
- `ListSlots()` 返回有效槽位的 `SaveSlotMetadata`；`DeleteAsync(...)` 删除主档与备份，并返回是否实际删除文件。
- 迁移缺失、返回 `null`、抛出异常或内容/校验失败会成为 `save.invalid`。主档失败后仍尝试备份；`OperationCanceledException` 原样抛出，不继续回退。
