# Godot 适配层规则

- 本目录是 LXFramework.Core 到 Godot 的薄适配层，禁止依赖 `game-manifest.json` 声明的产品层。
- 世界、Feature、Chunk 与 UI 通过 `LXContextInjector` 获取上下文；禁止全局上下文访问器。
- Godot 场景树操作保持主线程；动态资源租约归属最窄的 `LifetimeScope`。
- `Generated/` 只能由工具生成，禁止手改。变更后按影响运行 `./lx.ps1 check`；全量门禁时机遵循根 `AGENTS.md`。
