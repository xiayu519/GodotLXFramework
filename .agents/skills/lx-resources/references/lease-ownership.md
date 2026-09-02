# 资源租约与所有权

`LX.Res` 是唯一加载、缓存、租约与诊断入口；专用 API 只能包装它。产品先用 `lx create res` 登记并使用生成的 `ResCatalog`，不直接调用 `GD.Load` 或 `ResourceLoader.Load*`。

游戏节点从注入的 `LXContext.Res` 调用 `Acquire/AcquireAsync` 并取得 `AssetLease<T>`。租约由调用方或实际使用目标的所有者在最窄生命周期内持有：短作用域用 `using`，对象作用域交给对应 `LifetimeScope`，裸 `Resource` 不得活得比租约更久。

磁盘资源属于 Godot 引用计数和全局路径缓存；租约释放只删除 LX 强引用，不对共享资源调用 `Dispose()`。`AcquireGenerated` 创建的资源由注册表拥有，最后一个 `Transient` 租约释放或缓存清理时才确定性 `Dispose()`。

运行时路径必须是规范 `res://` 路径：使用 `/`，不含空段、`.`、`..` 或重复分隔符；`ContentRef` 还必须位于 `res://content/`。`Transient` 用于一次性或大资源，`Cached` 使用有界空闲缓存，`Resident` 只用于明确常驻资源。

连续影片使用框架 `VideoSequencePlayer`；它逐项持有 `AssetLease<VideoStream>` 到真实播放完成、跳过或取消，并立即清空 `VideoStreamPlayer.Stream`。产品不复制租约循环，也不增加媒体资源注册表。

只读入口/所有权问题取得 `LXContext`、`AssetRegistry`、`AssetLease` 的直接源码证据后立即回答，不搜索绑定、PackedScene、池或完整资源实现。
