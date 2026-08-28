namespace LX.Res;

/// <summary>资源租约归还后，AssetRegistry 对底层 Godot Resource 的保留策略。</summary>
public enum AssetCachePolicy
{
    /// <summary>最后一个租约释放后即可移除缓存，适合一次性或体积较大的资源。</summary>
    Transient = 0,

    /// <summary>最后一个租约释放后进入有界空闲缓存，可能按最近最少使用策略回收。</summary>
    Cached = 1,

    /// <summary>注册表存活期间始终保留，适合入口资源和高频基础资源。</summary>
    Resident = 2,
}
