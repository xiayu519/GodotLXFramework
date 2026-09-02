# 资源释放验证

验证必须观察所有权事实：

1. 连续执行加载/替换/清空或创建/销毁循环。
2. 绑定释放后目标属性为 `null`，`LX.Res.Snapshot()` 活动租约回到基线。
3. `PackedSceneInstance.DisposeAsync()` 后 `GodotObject.IsInstanceValid(node)` 为 false。
4. `Cached` 条目与活动租约分开判断；证明缓存可回收时先 `PurgeIdleCache()`。
5. 共享加载资源在 LX 清缓存后仍可用；不用强制 GC 数字代替所有权断言。

重开闭环在 UI 与 Feature handle 退出作用域后采样。框架 smoke 保留 `LX_RESOURCE_SHARED_CACHE_SAFETY_PASS`、`LX_DYNAMIC_TEXTURE_ATLAS_LIFECYCLE_PASS` 和 `LX_PACKED_SCENE_INSTANCE_LIFECYCLE_PASS`。完成后一次运行路径级 `check`；达到根 `AGENTS.md` 的仓库级门禁时才运行 `validate`。
