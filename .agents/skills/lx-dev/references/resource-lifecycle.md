# 资源生命周期

适用于所有 Godot `Resource`：`PackedScene`、`Texture2D`/`AtlasTexture`、`AudioStream`、`Font`、`Material`、`Shader`、`Mesh`、`Theme` 和自定义 `Resource`。`LX.Res` 是唯一加载、缓存、租约与诊断入口；专用 API 只能包装它，不能建立第二套缓存。

## 先选择所有权形态

| 场景 | 使用 | 生命周期 |
|---|---|---|
| 场景中已序列化的静态引用 | 导出属性或场景资源引用 | 随场景树 |
| 短作用域只读资源 | `LX.Res.Acquire/AcquireAsync` + `AssetLease<T>` | 最窄 `LifetimeScope` 或 `using` |
| 会动态替换的资源属性 | `AssetBinding<T>` | 与目标属性相同的 `LifetimeScope` |
| UI 图片、九宫格或按钮状态图 | `UIScreen.BindTexture` / `UITextureBinding` | 默认使用页面 `Activation` |
| 一次性 `PackedScene` 实例 | `PackedSceneInstance<TNode>` | 所属 Feature、世界或页面生命周期 |
| 高频重复 `PackedScene` 实例 | `PackedSceneNodePool<TNode>` | 整场战斗或所属 Feature 生命周期 |
| 页面、Feature、世界和音频 | `LX.UI`、`LX.Features`、`LX.Scenes`、`LX.Audio` | 由对应现有服务管理 |

产品代码先用 `lx create res <id> <ResourceType> <res://path> <policy>` 登记，再使用生成的 `ResCatalog`。不要直接调用 `GD.Load` 或 `ResourceLoader.Load*`。

## 所有权顺序

- 磁盘加载的 `Resource` 属于 Godot 的引用计数与全局路径缓存。`LX.Res` 释放租约时只删除自己的强引用，不对共享加载资源调用 `Dispose()`。
- `AcquireGenerated` 工厂创建的资源由注册表拥有；最后一个 `Transient` 租约释放或缓存被清理时会确定性 `Dispose()`。资源不能越过租约继续保存到其他对象。
- 动态属性必须先把目标设为 `null`，再释放旧租约。使用 `AssetBinding<T>`，不要手写容易颠倒顺序的替换逻辑。
- Node 先取消实例生命周期，再 `QueueFree`。需要确认原生对象已经失效时，使用 `PackedSceneInstance.DisposeAsync()` 或显式等待下一次 `ProcessFrame`；只检查 C# handle 已关闭不能证明 Node 已释放。
- `Transient` 用于一次性或大资源，`Cached` 进入有界 LRU 空闲缓存，`Resident` 只用于明确常驻的基础资源。不要把不断变化的关卡资源标成 `Resident`。

## 动态资源属性

`AssetBinding<T>` 适用于材质、字体、Shader、Mesh、自定义 Resource 等任意动态属性。它保证新请求后发先至时只有最后一次生效，并在释放时清空目标引用：

```csharp
var material = AssetBinding<Material>.Create(
    LX.Res,
    Lifetime,
    value => _sprite.Material = value);

await material.SetAsync(ResCatalog.PlayerMaterial, Lifetime.Token);
```

绑定的 setter 只能捕获同一生命周期内的目标。不要把 `binding.Resource` 或原始 `lease.Resource` 保存到更长生命周期。

## UI 图片与图集

在 `OnShowAsync` 内用激活生命周期绑定，缓存页面隐藏时也会释放动态图片：

```csharp
protected internal override async ValueTask OnShowAsync(
    object? payload,
    CancellationToken cancellationToken)
{
    var portrait = BindTexture(_portrait);
    await portrait.SetAsync(ResCatalog.PlayerPortrait, cancellationToken);
}
```

使用 Godot 导入的 `AtlasTexture` `.tres` 表示图集区域，并以 `AtlasTexture` 或 `Texture2D` 注册到资源清单。`AtlasTexture` 继承 `Texture2D`，因此使用相同绑定；不创建运行时图集缓存或第二套图片管理器。按钮状态图使用 `BindTexture(button, TextureButtonSlot.Normal)` 等明确槽位。

## PackedScene 实例

Godot 的 `PackedScene` 是序列化场景模板，也是最接近 Prefab 的原生概念。一次性实例使用：

```csharp
await using var enemy = await PackedSceneInstance<Enemy>.CreateAsync(
    LX,
    ResCatalog.Enemy,
    this,
    Lifetime,
    Lifetime.Token);
```

句柄负责场景租约、递归 LX 注入、实例子生命周期和 Node 回收。大量子弹、敌机或特效改用现有 `PackedSceneNodePool<TNode>`；完整页面、Feature 或世界仍走对应服务，不绕过目录和状态管理。

## 闭环验证

新增或修改动态资源流程时，测试必须观察事实而不是只看“没有异常”：

1. 连续执行加载/替换/清空或创建/销毁循环。
2. 属性绑定释放后断言目标属性为 `null`；`LX.Res.Snapshot()` 的活动租约回到基线。
3. `PackedSceneInstance.DisposeAsync()` 后用 `GodotObject.IsInstanceValid(node)` 断言 Node 已失效。
4. `Cached` 条目与活动租约分开判断；需要证明缓存可回收时调用 `PurgeIdleCache()` 后再取快照。
5. 共享加载资源在 `LX.Res` 清缓存后仍可用；禁止用强制 GC 数字代替所有权断言。C# GC 可能延迟释放无引用的 Godot `Resource`，短时进程内存不立即下降不等于泄漏。

框架 smoke 必须保留 `LX_RESOURCE_SHARED_CACHE_SAFETY_PASS`、`LX_DYNAMIC_TEXTURE_ATLAS_LIFECYCLE_PASS` 和 `LX_PACKED_SCENE_INSTANCE_LIFECYCLE_PASS`。完成实现后把全部变更路径一次传给 `./lx.ps1 check`，再运行 `./lx.ps1 validate`。
