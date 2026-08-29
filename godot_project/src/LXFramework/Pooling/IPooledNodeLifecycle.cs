using LX.Core.Lifetime;

namespace LX.Pooling;

/// <summary>为 PackedScene 池化节点提供每次租用独立的激活生命周期。</summary>
public interface IPooledNodeLifecycle
{
    /// <summary>节点完成本次配置、但尚未加入场景树时调用。</summary>
    void OnRent(LifetimeScope activation);

    /// <summary>节点退出本次租用、激活生命周期被取消前调用。</summary>
    void OnReturn();
}
