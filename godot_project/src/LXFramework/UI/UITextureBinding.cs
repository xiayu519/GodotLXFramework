using LX.Core.Lifetime;
using LX.Res;
using Godot;

namespace LX.UI;

/// <summary>
/// A lifetime-owned Texture2D binding for Godot UI controls. Imported
/// AtlasTexture resources use the same API because AtlasTexture derives from Texture2D.
/// </summary>
public sealed class UITextureBinding : IDisposable
{
    private readonly AssetBinding<Texture2D> _binding;

    private UITextureBinding(AssetBinding<Texture2D> binding)
    {
        _binding = binding;
    }

    /// <summary>当前显示的纹理；未设置或已释放时为 null。</summary>
    public Texture2D? Texture => _binding.Resource;

    /// <summary>绑定是否已经随所属生命周期释放。</summary>
    public bool IsDisposed => _binding.IsDisposed;

    /// <summary>为 TextureRect 创建生命周期绑定。</summary>
    public static UITextureBinding Create(
        AssetRegistry assets,
        LifetimeScope lifetime,
        TextureRect target) =>
        CreateCore(
            assets,
            lifetime,
            texture =>
            {
                if (GodotObject.IsInstanceValid(target))
                {
                    target.Texture = texture;
                }
            },
            target);

    /// <summary>为 NinePatchRect 创建生命周期绑定。</summary>
    public static UITextureBinding Create(
        AssetRegistry assets,
        LifetimeScope lifetime,
        NinePatchRect target) =>
        CreateCore(
            assets,
            lifetime,
            texture =>
            {
                if (GodotObject.IsInstanceValid(target))
                {
                    target.Texture = texture;
                }
            },
            target);

    /// <summary>为 TextureButton 的指定状态创建生命周期绑定。</summary>
    public static UITextureBinding Create(
        AssetRegistry assets,
        LifetimeScope lifetime,
        TextureButton target,
        TextureButtonSlot slot) =>
        CreateCore(
            assets,
            lifetime,
            texture => ApplyButtonTexture(target, slot, texture),
            target);

    /// <summary>同步加载并替换 Texture2D 或其派生类型（包括 AtlasTexture）。</summary>
    public void Set<TTexture>(AssetRef<TTexture> asset) where TTexture : Texture2D =>
        _binding.Set(new AssetRef<Texture2D>(asset.Path, asset.CachePolicy));

    /// <summary>
    /// 异步加载并替换 Texture2D 或其派生类型（包括 AtlasTexture）；只有最后一次请求会生效。
    /// </summary>
    public ValueTask<bool> SetAsync<TTexture>(
        AssetRef<TTexture> asset,
        CancellationToken cancellationToken = default)
        where TTexture : Texture2D =>
        _binding.SetAsync(
            new AssetRef<Texture2D>(asset.Path, asset.CachePolicy),
            cancellationToken);

    /// <summary>先清空控件纹理，再归还资源租约。</summary>
    public void Clear() => _binding.Clear();

    public void Dispose() => _binding.Dispose();

    private static UITextureBinding CreateCore(
        AssetRegistry assets,
        LifetimeScope lifetime,
        Action<Texture2D?> apply,
        Control target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!GodotObject.IsInstanceValid(target))
        {
            throw new ObjectDisposedException(target.GetType().Name);
        }

        return new UITextureBinding(AssetBinding<Texture2D>.Create(assets, lifetime, apply));
    }

    private static void ApplyButtonTexture(
        TextureButton target,
        TextureButtonSlot slot,
        Texture2D? texture)
    {
        if (!GodotObject.IsInstanceValid(target))
        {
            return;
        }

        switch (slot)
        {
            case TextureButtonSlot.Normal:
                target.TextureNormal = texture;
                break;
            case TextureButtonSlot.Pressed:
                target.TexturePressed = texture;
                break;
            case TextureButtonSlot.Hover:
                target.TextureHover = texture;
                break;
            case TextureButtonSlot.Disabled:
                target.TextureDisabled = texture;
                break;
            case TextureButtonSlot.Focused:
                target.TextureFocused = texture;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(slot), slot, null);
        }
    }
}

/// <summary>TextureButton 中由动态纹理绑定管理的视觉状态。</summary>
public enum TextureButtonSlot
{
    /// <summary>普通状态纹理。</summary>
    Normal,
    /// <summary>按下状态纹理。</summary>
    Pressed,
    /// <summary>悬停状态纹理。</summary>
    Hover,
    /// <summary>禁用状态纹理。</summary>
    Disabled,
    /// <summary>键盘或手柄焦点状态纹理。</summary>
    Focused,
}
