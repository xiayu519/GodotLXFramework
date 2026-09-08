# 运行时契约

## 上下文注入

`LXContextInjector.InitializeTree` 会在子树进入活动场景树之前，以 `GetChild(index)` 的插入顺序建立后序快照并初始化所有尚未初始化的 `ILXContextReceiver`：子先于父。Godot 4.7.2 的 Ready 同样保证子先于父，但引擎内部遍历 `HashMap`，不承诺兄弟节点顺序；业务不得让兄弟间初始化依赖 Ready 的先后。

- `OnLXInitialized` 可以使用 `LX` 和场景自身持有的纯节点引用。
- 生成的 UI 绑定稍后在 `OnBindingsReady` 与 `OnShowAsync` 可用。
- 世界、动态功能、世界区块和 UI 场景遵守同一规则。
- 已初始化节点改挂父级后仍保留原 `LX` 与 `Lifetime`，禁止换所有者复用。

## 生命周期所有权

- Host 拥有共享服务。
- 世界拥有场景生命周期。
- 每个功能实例拥有子生命周期和场景资源租约。
- 每个 UI 实例拥有实例生命周期；每次打开/关闭拥有更短的激活生命周期。
- UIService 先创建私有服务生命周期，再注册为根所有者；实例与激活生命周期都属于该服务。调用方传入的 `parentLifetime` 拥有可等待的关闭链接，不直接拥有 activation；取消会请求关闭，`DisposeAsync` 会等待 UI 关闭完成，保证 `OnHideAsync` 发生在 activation Dispose 之前。
- 实现 `IPooledNodeLifecycle` 的 PackedScene 池化节点，每次租用拥有独立激活生命周期；归还时先调用 `OnReturn`，再取消本轮激活生命周期。
- 缓存 UI 保留实例生命周期，但隐藏时释放激活生命周期。
- 动态资源属性绑定与目标属于同一生命周期，释放时先清空属性再归还租约。
- 一次性 `PackedScene` 实例拥有独立子生命周期；异步关闭可等待 `QueueFree` 跨过帧边界。
- 清理前先取消，随后按注册逆序释放所有对象。

动态回调、订阅、取消注册、资源租约和长任务都绑定到最窄的有效生命周期。

## UI 生命周期

1. 实例化场景。
2. 递归注入 `LX`。
3. 分配独立激活标识与激活生命周期。
4. 加入 UI 树，首次 `_Ready` 中执行生成绑定。
5. 执行 `OnShowAsync` 和 Entering，每次等待后检查取消与激活身份，再交付焦点。
6. 关闭先取消并等待尚在执行的显示/进入回调，再执行 Exiting、`OnHideAsync`，释放激活生命周期，再缓存或释放实例。

自定义异步钩子必须观察传入令牌，不在钩子内部等待自身关闭。忽略取消的任意业务代码无法被强行中止；框架等待其回调结束后才复用或释放节点。缓存节点的每次激活使用新标识，旧句柄和旧 owner 延迟回调不能影响新激活；关闭进行中不接受同一缓存单例重开，等待关闭完成后可立即重开。

显示与 GUI 输入层序是 `Screen → Chrome → Popup → Overlay`，与枚举数值无关，旧数值保持兼容。`Chrome` 用于产品常驻导航/HUD，产品拥有布局和路由；模态 UI 在自身之后设置全屏指针屏障，并禁用更低 UI 的键盘/手柄焦点。语义 Cancel 调用 `LX.UI.RequestBackAsync`：顶层弹窗优先于页面，排除 Chrome/Overlay；页面可从 `OnBackRequestedAsync` 返回 `false` 拒绝关闭。

`LX.UI.PlayFadeAsync` 使用内置缓存黑幕串行执行过场。`FadeOut` 从透明变黑并保持黑幕；`FadeIn` 从黑变透明并关闭黑幕；`FadeOutIn` 自动完成透明、黑色停留、透明的完整流程。调用方可以覆盖淡出、停留、淡入时间以及 Godot Tween 的 Transition/Ease，取消只终止当前等待，不允许留下未纳入 UIService 生命周期的过场实例。

标准模态弹窗继承 `UIPopupScreen`，场景提供 `%Scrim` 与 `%MotionPivot`。进入过渡等待一帧完成容器布局，再以真实经过时间把内容从小尺寸淡入并将 pivot 固定到内容中心；退出过渡执行更短的缩小淡出。进入过渡观察可由关闭取消的开窗令牌，退出过渡观察仍未释放的激活令牌，不缩放全屏输入根或遮罩，时长为零时直接落到确定的最终状态。

## 视觉捕获收尾

正常捕获显式等待 `visualLifetime.DisposeAsync()`，场景节点、捕获 Viewport 与 PackedScene 租约在异步清理期间保持有效；之后等待延迟节点释放，再返回报告。清理异常使捕获失败；捕获与清理同时异常时保留两者。`DisposeEmergency` 仅用于引擎紧急退出，不是正常验证通过的替代路径。

视觉运行时默认 120 秒取消，CLI 进程监督 130 秒硬截止；关闭帧数退出，不把 `--fixed-fps` 或 `--quit-after` 的迭代数当真实秒数。通过必须具有本次实际图片、匹配 hash 的成功报告、退出码与成功标记，渲染模式还需要隐藏窗口证据。永不就绪/不合作清理最终失败，外部强杀不承诺完整清理。详见 README 的隐藏正反向探针命令。

## 事件与调度

`LX.Events` 在订阅变化时生成处理器快照，稳态 `Publish` 不分配托管内存。一次发布只观察开始时的快照；回调中订阅或退订从下一次发布起生效。Host 配置为逐处理器隔离异常、记录 `events.handler_failures` 并继续调用后续处理器；直接构造 `EventHub` 时只有显式启用隔离才获得该语义。

`GameScheduler.Tick` 只执行本次 Tick 开始前已经排入的到期任务；回调中安排的零延迟任务推迟到下一次 Tick，避免同 Tick 自调度无法退出。取消会立即从活动字典移除任务并清空回调引用，惰性队列与活动项差距过大时自动压缩。

## 动作编排

`LX.Actions` 执行由 `LXActions` 创建的纯 C# 动作树。每个根同时观察 ActionRunner、调用方 `LifetimeScope` 与显式取消令牌；Sequence、Parallel、Race、Invoke、Async、Delay、Timeout、Retry 和 Finally 只负责编排现有服务，不替代 GameFlow、StateMachine、Scheduler 或 Tween。

`ActionRunner.RunAsync` 在返回完成任务前同步执行到第一个未完成等待。只有 ActionRunner、owner 或显式令牌请求的取消会把任务标记为 cancelled；动作自行抛出的无关 `OperationCanceledException` 按失败处理。

活动根和最近 32 个终结根保留有界 snapshot。Parallel 任一子项失败会取消兄弟；Race 采用最先终结者的结果并观察全部 loser；Finally 在调用方 owner 取消时仍使用服务关闭令牌尝试清理，但框架根紧急关闭不承诺等待异步清理。

动作节点会为诊断分配执行记录，适合过场、教程、任务步骤和其他中低频流程；每帧、高频战斗和大量实体更新仍直接使用确定性系统、Scheduler 或产品数据循环。

`VideoSequencePlayer` 是局部 Godot 适配组件：每段影片通过 `LX.Res` 租约加载，每项作为稳定命名的 `LX.Actions` 节点执行，只有收到 `VideoStreamPlayer.Finished` 才完成；跳过取消整段序列并观察收尾。产品继续拥有影片目录、UI 样式、输入动作和原格式迁移，不建立全局媒体管理器。

## AI 运行时诊断

Godot Editor/Debug 运行时在 `.lx/runtime/` 发布带心跳的本地只读桥；Release 不启用。请求必须匹配当前 `sessionId` 和 `generation`，响应按领域返回有界 snapshot。桥由 `LXHost._Process` 在主线程泵送，因此 UI、资源、场景等快照不跨线程访问 Godot 对象；它不提供任意代码执行，也不是第二套服务通信机制。

## 流程状态

`GameFlow.TransitionAsync` 与 `StateMachine.TransitionAsync` 串行执行状态切换。`EnterAsync`、`ExitAsync` 或 `Transitioned` 回调中禁止再次调用并等待 `TransitionAsync`；重入会立即抛出 `InvalidOperationException`，避免等待当前已持有的 transition gate。

`GameFlow` 构造器收到的 `parentLifetime` 只拥有各状态的资源 Scope，不隐式长期持有 `GameFlow` 对象。需要让根关闭保证调用当前状态的 `ExitAsync` 时，使用 `parentLifetime.Own(new GameFlow<...>(..., parentLifetime))`；短作用域也可使用 `await using` 并显式 `DisposeAsync`。只释放 `parentLifetime` 而未拥有 Flow，仍会清理状态资源，但不承诺执行行为型 `ExitAsync`。

## 2D 相机

`Camera2DController.Attach(camera, lifetime)` 为传入的具体 `Camera2D` 建立局部控制器，提供跟随、平滑、死区、相机中心边界和衰减震动。每个相机使用独立控制器；禁止同一个相机重复 Attach，不引入全局“当前相机”。控制器拥有绑定期间的 `Camera2D.GlobalPosition` 与 `Offset`，随指定生命周期回收。3D 使用未来独立的 `Camera3DController`，不混合 2D/3D 参数。

## 时间与暂停

- `Clock`/`Scheduler` 由 `_Process` 推进。
- `PhysicsClock`/`PhysicsScheduler` 由 `_PhysicsProcess` 推进。
- `PauseService` 同步修改两套时钟与 Godot 场景树。
- 世界根节点可暂停，框架 UI 始终运行，保证暂停菜单可响应。
- `PauseService` 是 `SceneTree.Paused` 的唯一写入者；它会修复外部写入造成的状态偏差，并在 Host 被引擎 suspend/禁用时拒绝产生时钟与场景树分裂的暂停状态。

## 启动与关闭

`LXHost.BootTask`、`IsBooted`、`BootError` 和 `FrameworkBootCompleted` 暴露启动状态。产品代码等待 `LX` 注入，不搜索 Host。

需要有序异步关闭时调用 `LXHost.ShutdownAsync`。`_ExitTree` 只通过 `LifetimeScope.DisposeEmergency` 执行取消与非阻塞尽力清理，不能承担需要等待帧或严格依赖逆序完成的常规清理。

Godot 侧的输入、本地化、资源、对象池、场景、功能、UI、设置、暂停、世界流式、诊断和音频 API 都是主线程操作。音频关闭会取消并等待活动的异步播放/加载，再释放原生子树。
