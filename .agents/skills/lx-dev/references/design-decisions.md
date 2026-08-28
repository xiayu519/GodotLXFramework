# Codex 决策表

引入新抽象前先查此表。

| 需求 | 使用 | 禁止引入 |
|---|---|---|
| 纯确定性或数据逻辑 | `LX.Core` 或产品根目录中的纯 C# 类型 | 只为承载逻辑而创建 Godot `Node` |
| 可复用场景能力 | `lx create feature` 与 `FeatureCatalog` | 未注册的动态 `PackedScene` 加载 |
| 局部一次性场景模板 | `ResCatalog` 与 `PackedSceneInstance<TNode>` | 裸 `Instantiate()` 后自行拼接释放逻辑 |
| 完整可玩世界 | `lx create world` 与 `WorldCatalog` | 直接调用 `SceneTree.ChangeScene*` |
| 页面、弹窗、覆盖层 | `lx create screen`，再编辑 UI 清单层级 | 第二个 UI 管理器 |
| 动态 Godot 资源 | 资源清单、`ResCatalog`、`LX.Res`、`AssetLease<T>`/`AssetBinding<T>` | 游戏代码中的 `GD.Load`/`ResourceLoader.Load` |
| 场景已序列化的静态资源 | 导出属性或场景引用 | 不必要的资源租约 |
| 数据表 | `lx create content` 与生成的 `ContentCatalog` | 游戏逻辑中的临时 JSON 路径 |
| 语义输入 | 输入清单与 `InputCatalog`/`LXInputActions` | 游戏代码硬编码注册动作 |
| 跨功能事实通知 | 现有 `EventHub`，订阅归生命周期所有 | 第二条事件总线，或用事件模拟需返回值的调用 |
| 父子协作 | 直接方法/属性调用或 Godot 信号 | 隐藏所有权的全局事件 |
| 启动、菜单、游玩、结束等产品状态 | `GameFlow<TState,TContext>` | 把状态散落到无关节点 |
| 暂停游戏 | `LX.Pause` | 分别修改时钟和 `SceneTree.Paused` |
| 画面帧时序 | `LX.Clock` / `LX.Scheduler` | 用可变渲染帧模拟确定性物理 |
| 固定模拟时序 | `LX.PhysicsClock` / `LX.PhysicsScheduler` | 把渲染帧当确定性 Tick |

## 层级放置

- `LX.Core` 禁止引用 Godot。
- LXFramework 存放可复用的引擎适配，禁止引用游戏清单声明的产品命名空间。
- 产品根目录持有产品规则、内容类型、世界、功能和界面。
- `Generated` 是派生输出，修改对应清单或场景，不手改生成文件。

## 通信优先级

优先使用最窄且可见的关系：直接调用，其次是场景局部反应的 Godot 信号，最后才是供多个观察者消费的跨功能事实事件。需要返回结果的命令不是事件。

## 异步与线程

Godot 场景树、UI、功能、场景、音频、设置和资源操作都从主线程开始，不要用 `Task.Run` 包裹。后台线程可以产生纯数据，但必须回到主线程应用到 Godot；长任务观察所属生命周期的取消令牌。
