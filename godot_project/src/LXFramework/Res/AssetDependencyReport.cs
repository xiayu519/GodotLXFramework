using Godot;

namespace LX.Res;

/// <summary>资源预热计划的静态分析结果。</summary>
public enum AssetPlanStatus
{
    /// <summary>依赖完整且无环，可以按拓扑顺序加载。</summary>
    Ready,

    /// <summary>至少一个资源引用了计划中不存在的依赖。</summary>
    MissingDependency,

    /// <summary>依赖图存在环，无法确定安全加载顺序。</summary>
    Cycle,
}

/// <summary>资源依赖计划的确定性分析报告。</summary>
public sealed record AssetDependencyReport(
    AssetPlanStatus Status,
    IReadOnlyList<string> LoadOrder,
    IReadOnlyList<string> MissingDependencies,
    IReadOnlyList<string> CyclicAssets)
{
    /// <summary>报告是否允许开始加载。</summary>
    public bool IsValid => Status == AssetPlanStatus.Ready;
}

/// <summary>可命名、可复用的资源预热集合。</summary>
public sealed record AssetPreloadSet<T>(
    string Id,
    IReadOnlyList<AssetLoadRequest<T>> Requests)
    where T : Resource
{
    /// <summary>校验集合名称并返回依赖分析报告。</summary>
    public AssetDependencyReport Analyze()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new ArgumentException("Asset preload set IDs cannot be empty.", nameof(Id));
        }
        ArgumentNullException.ThrowIfNull(Requests);
        return AssetDependencyAnalyzer.Analyze(Requests);
    }
}

/// <summary>分析资源请求中的缺失依赖和循环依赖，不触发实际资源加载。</summary>
public static class AssetDependencyAnalyzer
{
    /// <summary>返回稳定拓扑顺序或可供工具展示的依赖错误。</summary>
    public static AssetDependencyReport Analyze<T>(IEnumerable<AssetLoadRequest<T>> requests)
        where T : Resource
    {
        ArgumentNullException.ThrowIfNull(requests);
        var indexed = new Dictionary<string, AssetLoadRequest<T>>(StringComparer.Ordinal);
        foreach (var request in requests)
        {
            if (string.IsNullOrWhiteSpace(request.Id))
            {
                throw new ArgumentException("Asset request IDs cannot be empty.", nameof(requests));
            }
            if (!indexed.TryAdd(request.Id, request))
            {
                throw new ArgumentException($"Asset request ID '{request.Id}' is duplicated.", nameof(requests));
            }
        }

        var missing = indexed.Values
            .SelectMany(request => (request.Dependencies ?? [])
                .Where(dependency => !indexed.ContainsKey(dependency))
                .Select(dependency => $"{request.Id}->{dependency}"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
        {
            return new AssetDependencyReport(
                AssetPlanStatus.MissingDependency,
                [],
                missing,
                []);
        }

        var remaining = indexed.ToDictionary(
            pair => pair.Key,
            pair => new HashSet<string>(pair.Value.Dependencies ?? [], StringComparer.Ordinal),
            StringComparer.Ordinal);
        var order = new List<string>(remaining.Count);
        while (remaining.Count > 0)
        {
            var ready = remaining
                .Where(pair => pair.Value.Count == 0)
                .Select(pair => pair.Key)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            if (ready.Length == 0)
            {
                return new AssetDependencyReport(
                    AssetPlanStatus.Cycle,
                    order,
                    [],
                    remaining.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray());
            }

            foreach (var id in ready)
            {
                remaining.Remove(id);
                order.Add(id);
                foreach (var dependencies in remaining.Values)
                {
                    dependencies.Remove(id);
                }
            }
        }

        return new AssetDependencyReport(AssetPlanStatus.Ready, order, [], []);
    }
}
