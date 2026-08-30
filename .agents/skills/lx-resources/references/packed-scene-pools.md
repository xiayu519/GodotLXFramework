# PackedScene 实例与节点池

一次性动态场景使用 `PackedSceneInstance<TNode>`；句柄负责场景租约、递归 LX 注入、实例子生命周期和 Node 回收：

```csharp
await using var enemy = await PackedSceneInstance<Enemy>.CreateAsync(
    LX,
    ResCatalog.Enemy,
    this,
    Lifetime,
    Lifetime.Token);
```

大量子弹、敌机或特效使用 `PackedSceneNodePool<TNode>`。需要每轮订阅、异步任务或动态资源的节点实现 `IPooledNodeLifecycle`，把状态绑定到 `OnRent` 收到的激活生命周期。带 `configure` 的 Rent/RentLease 可在节点进入树前完成配置。

完整页面、Feature 或世界仍走对应服务。Node 回收先取消实例生命周期，再 `QueueFree`；需要确定证据时等待下一次 `ProcessFrame` 或使用 `DisposeAsync()` 后检查原生对象失效。
