# 动态资源绑定

动态材质、字体、Shader、Mesh 或自定义 Resource 使用 `AssetBinding<T>`。它负责后发先至和释放时清空目标；setter 只能捕获同一生命周期内的目标：

```csharp
var material = AssetBinding<Material>.Create(
    LX.Res,
    Lifetime,
    value => _sprite.Material = value);
await material.SetAsync(ResCatalog.PlayerMaterial, Lifetime.Token);
```

UI 图片、九宫格和按钮状态图使用 `UIScreen.BindTexture` / `UITextureBinding`，默认绑定页面 `Activation`。Godot `AtlasTexture` 作为 `.tres` 注册，仍复用 `LX.Res`，不创建图集缓存或图片管理器。

动态属性必须先清空目标，再释放旧租约；不要把 `binding.Resource` 或 `lease.Resource` 保存到更长生命周期。
