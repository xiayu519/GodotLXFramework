using LX.Res;
using Godot;

namespace LX.Scenes;

/// <summary>场景加载流程当前所处阶段。</summary>
public enum SceneLoadStage
{
    /// <summary>Godot 正在后台读取 PackedScene 资源。</summary>
    LoadingResource,

    /// <summary>资源已就绪，框架正在实例化节点并注入 LX 上下文。</summary>
    Instantiating,

    /// <summary>场景已经可用于切换或显示。</summary>
    Ready,
}

/// <summary>场景加载进度；Ratio 始终位于 0 到 1。</summary>
public sealed record SceneLoadProgress(
    string ScenePath,
    SceneLoadStage Stage,
    float Ratio);

/// <summary>持有已预载 PackedScene 的租约；释放后资源可按缓存策略回收。</summary>
public sealed class ScenePreload : IDisposable
{
    private AssetLease<PackedScene>? _lease;

    internal ScenePreload(string scenePath, AssetLease<PackedScene> lease)
    {
        ScenePath = scenePath;
        _lease = lease;
    }

    /// <summary>已经预载的 res:// 场景路径。</summary>
    public string ScenePath { get; }

    /// <summary>预载租约是否已经释放。</summary>
    public bool IsDisposed => _lease is null;

    /// <summary>释放预载租约；若资源没有其他租约，缓存策略决定何时回收。</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _lease, null)?.Dispose();
    }
}
