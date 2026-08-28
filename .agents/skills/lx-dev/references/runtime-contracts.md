# 运行时契约

## 上下文注入

`LXContextInjector.InitializeTree` 会在子树进入活动场景树之前，以先子后父顺序初始化所有尚未初始化的 `ILXContextReceiver`。

- `OnLXInitialized` 可以使用 `LX` 和场景自身持有的纯节点引用。
- 生成的 UI 绑定稍后在 `OnBindingsReady` 与 `OnShowAsync` 可用。
- 世界、动态功能、世界区块和 UI 场景遵守同一规则。
- 已初始化节点改挂父级后仍保留原 `LX` 与 `Lifetime`，禁止换所有者复用。

## 生命周期所有权

- Host 拥有共享服务。
- 世界拥有场景生命周期。
- 每个功能实例拥有子生命周期和场景资源租约。
- 每个 UI 实例拥有实例生命周期；每次打开/关闭拥有更短的激活生命周期。
- 缓存 UI 保留实例生命周期，但隐藏时释放激活生命周期。
- 动态资源属性绑定与目标属于同一生命周期，释放时先清空属性再归还租约。
- 一次性 `PackedScene` 实例拥有独立子生命周期；异步关闭可等待 `QueueFree` 跨过帧边界。
- 清理前先取消，随后按注册逆序释放所有对象。

动态回调、订阅、取消注册、资源租约和长任务都绑定到最窄的有效生命周期。

## UI 生命周期

1. 实例化场景。
2. 递归注入 `LX`。
3. 加入 UI 树，在 `_Ready` 中执行生成绑定。
4. 分配激活生命周期。
5. 执行 `OnShowAsync`。
6. 关闭时执行 `OnHideAsync`，释放激活生命周期，再缓存或释放实例。

语义 Cancel 调用 `LX.UI.RequestBackAsync`。顶层弹窗优先于页面；页面可从 `OnBackRequestedAsync` 返回 `false` 拒绝关闭。

## 时间与暂停

- `Clock`/`Scheduler` 由 `_Process` 推进。
- `PhysicsClock`/`PhysicsScheduler` 由 `_PhysicsProcess` 推进。
- `PauseService` 同步修改两套时钟与 Godot 场景树。
- 世界根节点可暂停，框架 UI 始终运行，保证暂停菜单可响应。

## 启动与关闭

`LXHost.BootTask`、`IsBooted`、`BootError` 和 `FrameworkBootCompleted` 暴露启动状态。产品代码等待 `LX` 注入，不搜索 Host。

需要有序异步关闭时调用 `LXHost.ShutdownAsync`。`_ExitTree` 只作为同步应急兜底，不能承担需要等待帧的常规清理。

Godot 侧的输入、本地化、资源、对象池、场景、功能、UI、设置、暂停、世界流式、诊断和音频 API 都是主线程操作。音频关闭会取消并等待活动的异步播放/加载，再释放原生子树。
