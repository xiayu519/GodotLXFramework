using LX.Res;
using LX.Core.Diagnostics;
using LX.Core.Lifetime;
using LX.Generated;
using LX.Runtime;
using Godot;

namespace LX.UI;

public sealed class UIService : IAsyncDisposable
{
    private sealed class UIInstance
    {
        public required Guid InstanceId { get; init; }
        public required UIDescriptor Descriptor { get; init; }
        public required UIScreen Screen { get; init; }
        public required AssetLease<PackedScene> SceneLease { get; init; }
        public required LifetimeScope Lifetime { get; init; }
        public LifetimeScope? Activation { get; set; }
        public long OpenSequence { get; set; }
        public UIVisualState State { get; set; }
        public TaskCompletionSource<UICompletion>? Completion { get; set; }
    }

    private readonly AssetRegistry _assets;
    private readonly LifetimeScope _serviceLifetime;
    private readonly MetricRegistry _metrics;
    private readonly Func<LXContext> _context;
    private readonly int _mainThreadId;
    private readonly CanvasLayer _canvas;
    private readonly Dictionary<UILayer, Control> _roots = [];
    private readonly Dictionary<UIId, UIDescriptor> _catalog = [];
    private readonly Dictionary<Guid, UIInstance> _active = [];
    private readonly Dictionary<UIId, UIInstance> _cache = [];
    private readonly HashSet<UIId> _openingSingletons = [];
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _fadeGate = new(1, 1);
    private UIHandle? _fadeHandle;
    private long _openSequence;
    private bool _disposed;

    public UIService(
        Node host,
        AssetRegistry assets,
        LifetimeScope rootLifetime,
        MetricRegistry metrics,
        Func<LXContext> context)
    {
        ArgumentNullException.ThrowIfNull(host);
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        ArgumentNullException.ThrowIfNull(rootLifetime);
        _serviceLifetime = rootLifetime.CreateChild("UIService");
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _mainThreadId = System.Environment.CurrentManagedThreadId;

        _canvas = new CanvasLayer
        {
            Name = "LXUI",
            Layer = 100,
            FollowViewportEnabled = false,
            ProcessMode = Node.ProcessModeEnum.Always,
        };
        host.AddChild(_canvas);
        _roots.Add(UILayer.Screen, CreateLayerRoot("Screens", 0));
        _roots.Add(UILayer.Popup, CreateLayerRoot("Popups", 100));
        _roots.Add(UILayer.Overlay, CreateLayerRoot("Overlays", 200));
        UpdateMetrics();
    }

    public void Register(UIDescriptor descriptor)
    {
        EnsureMainThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (string.IsNullOrWhiteSpace(descriptor.Id.Value))
        {
            throw new ArgumentException("UI IDs cannot be empty.", nameof(descriptor));
        }
        if (!GodotResourcePath.IsCanonical(descriptor.ScenePath, ".tscn"))
        {
            throw new ArgumentException("UI scenes must use a res:// .tscn path.", nameof(descriptor));
        }
        if (!Enum.IsDefined(descriptor.Layer) ||
            !Enum.IsDefined(descriptor.CachePolicy) ||
            !Enum.IsDefined(descriptor.CoverPolicy) ||
            !Enum.IsDefined(descriptor.InputPolicy) ||
            !Enum.IsDefined(descriptor.FocusPolicy))
        {
            throw new ArgumentException("UI layer, cache policy, and cover policy must be defined values.", nameof(descriptor));
        }
        if (!_catalog.TryAdd(descriptor.Id, descriptor))
        {
            throw new InvalidOperationException($"UI ID '{descriptor.Id}' is already registered.");
        }
    }

    public void RegisterRange(IEnumerable<UIDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        foreach (var descriptor in descriptors)
        {
            Register(descriptor);
        }
    }

    public async ValueTask<UIHandle> OpenAsync(
        UIId uiId,
        object? payload = null,
        LifetimeScope? parentLifetime = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMainThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_catalog.TryGetValue(uiId, out var descriptor))
        {
            throw new KeyNotFoundException($"UI '{uiId}' is not registered.");
        }

        var parent = parentLifetime ?? _serviceLifetime;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            parent.Token,
            _shutdown.Token,
            _serviceLifetime.Token);
        var isSingleton = descriptor.CachePolicy == UICachePolicy.CachedSingleton;
        if (isSingleton &&
            (_active.Values.Any(instance => instance.Descriptor.Id == uiId) || !_openingSingletons.Add(uiId)))
        {
            throw new InvalidOperationException($"Cached singleton UI '{uiId}' is already open or opening.");
        }

        UIInstance? openingInstance = null;
        var registered = false;
        try
        {
            UIInstance instance;
            if (isSingleton && _cache.Remove(uiId, out var cached))
            {
                instance = cached;
            }
            else
            {
                var assetPolicy = isSingleton ? AssetCachePolicy.Cached : AssetCachePolicy.Transient;
                var sceneLease = await _assets.AcquireAsync<PackedScene>(
                    descriptor.ScenePath,
                    assetPolicy,
                    operation.Token);
                Node? node = null;
                LifetimeScope? instanceLifetime = null;
                try
                {
                    EnsureMainThread();
                    operation.Token.ThrowIfCancellationRequested();
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    node = sceneLease.Resource.Instantiate();
                    if (node is not UIScreen screen)
                    {
                        var actualType = node?.GetType().Name ?? "<null>";
                        throw new InvalidOperationException(
                            $"UI scene '{descriptor.ScenePath}' must have a UIScreen-derived root, but produced {actualType}.");
                    }

                    var instanceId = Guid.NewGuid();
                    instanceLifetime = _serviceLifetime.CreateChild($"UIInstance:{uiId.Value}:{instanceId:N}");
                    LXContextInjector.InitializeTree(screen, _context(), instanceLifetime);
                    instance = new UIInstance
                    {
                        InstanceId = instanceId,
                        Descriptor = descriptor,
                        Screen = screen,
                        SceneLease = sceneLease,
                        Lifetime = instanceLifetime,
                        State = UIVisualState.Visible,
                    };
                }
                catch
                {
                    if (instanceLifetime is not null)
                    {
                        await instanceLifetime.DisposeAsync();
                    }
                    if (node is not null && GodotObject.IsInstanceValid(node))
                    {
                        node.QueueFree();
                    }
                    sceneLease.Dispose();
                    throw;
                }
            }
            openingInstance = instance;

            if (descriptor.CoverPolicy == UICoverPolicy.ClosePrevious)
            {
                foreach (var previousId in _active.Values
                             .Where(candidate => candidate.Descriptor.Layer == descriptor.Layer)
                             .OrderByDescending(candidate => candidate.OpenSequence)
                             .Select(candidate => candidate.InstanceId)
                             .ToArray())
                {
                    await CloseAsync(previousId);
                }
            }

            var activation = instance.Lifetime.CreateChild($"Activation:{uiId.Value}:{instance.InstanceId:N}");
            var completion = new TaskCompletionSource<UICompletion>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            instance.Completion = completion;
            instance.Activation = activation;
            instance.Screen.SetActivation(activation);
            Action<UICompletion> closeHandler = result =>
                _ = CloseSafelyAsync(instance.InstanceId, result);
            instance.Screen.CloseRequested += closeHandler;
            activation.Defer(() => instance.Screen.CloseRequested -= closeHandler);
            var closeOnParentCancellation = parent.Token.Register(() =>
                Callable.From((Action)(() =>
                    _ = CloseSafelyAsync(instance.InstanceId, UICompletion.Cancelled))).CallDeferred());
            activation.Own(closeOnParentCancellation);

            var root = _roots[descriptor.Layer];
            if (instance.Screen.GetParent() is null)
            {
                root.AddChild(instance.Screen);
            }

            instance.Screen.ProcessMode = Node.ProcessModeEnum.Inherit;
            if (descriptor.InputPolicy == UIInputPolicy.Modal)
            {
                instance.Screen.MouseFilter = Control.MouseFilterEnum.Stop;
            }
            instance.Screen.Show();
            instance.OpenSequence = ++_openSequence;
            _active.Add(instance.InstanceId, instance);
            registered = true;
            RefreshLayerPresentation(descriptor.Layer);
            UpdateMetrics();
            await instance.Screen.OnShowAsync(payload, operation.Token);
            await instance.Screen.OnTransitionAsync(UITransitionPhase.Entering, operation.Token);
            ApplyFocus(instance);
            return new UIHandle(this, instance.InstanceId, uiId, completion.Task);
        }
        catch
        {
            if (openingInstance is not null)
            {
                if (registered && _active.ContainsKey(openingInstance.InstanceId))
                {
                    await CloseAsync(openingInstance.InstanceId);
                }
                else if (!registered)
                {
                    await DisposeUnopenedInstanceAsync(openingInstance);
                }
            }
            throw;
        }
        finally
        {
            if (isSingleton)
            {
                _openingSingletons.Remove(uiId);
            }
        }
    }

    public IReadOnlyList<UIRecord> Snapshot()
    {
        EnsureMainThread();
        return _active.Values
            .OrderBy(instance => instance.OpenSequence)
            .Select(ToRecord)
            .ToArray();
    }

    public bool IsOpen(UIId uiId)
    {
        EnsureMainThread();
        return _active.Values.Any(instance => instance.Descriptor.Id == uiId);
    }

    /// <summary>当前是否由 FadeOut 留下了不透明全屏黑幕。</summary>
    public bool IsFadeBlackoutActive
    {
        get
        {
            EnsureMainThread();
            return TryGetFadeScreen() is { Opacity: >= 1f };
        }
    }

    /// <summary>
    /// 执行全屏黑幕过场。FadeOut 完成后保持黑幕，FadeIn 完成后移除黑幕，
    /// FadeOutIn 自动执行完整往返；并发调用按照请求顺序串行执行。
    /// </summary>
    public async ValueTask PlayFadeAsync(
        UIFadeMode mode,
        UIFadeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMainThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Fade mode must be defined.");
        }

        options ??= new UIFadeOptions();
        options.Validate();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token,
            _serviceLifetime.Token);
        var enteredGate = false;
        try
        {
            await _fadeGate.WaitAsync(operation.Token);
            enteredGate = true;
            EnsureMainThread();
            ObjectDisposedException.ThrowIf(_disposed, this);

            var retainedAtStart = TryGetFadeScreen() is not null;
            try
            {
                var screen = await GetOrOpenFadeScreenAsync(operation.Token);
                switch (mode)
                {
                    case UIFadeMode.FadeOut:
                        if (!retainedAtStart)
                        {
                            screen.SetOpacity(0f);
                        }
                        await screen.AnimateOpacityAsync(
                            1f,
                            options.FadeOutDuration,
                            options.Transition,
                            options.Ease,
                            operation.Token);
                        break;

                    case UIFadeMode.FadeIn:
                        if (!retainedAtStart)
                        {
                            screen.SetOpacity(1f);
                        }
                        await screen.AnimateOpacityAsync(
                            0f,
                            options.FadeInDuration,
                            options.Transition,
                            options.Ease,
                            operation.Token);
                        await CloseFadeAsync();
                        break;

                    case UIFadeMode.FadeOutIn:
                        screen.SetOpacity(0f);
                        await screen.AnimateOpacityAsync(
                            1f,
                            options.FadeOutDuration,
                            options.Transition,
                            options.Ease,
                            operation.Token);
                        await screen.HoldAsync(options.HoldDuration, operation.Token);
                        await screen.AnimateOpacityAsync(
                            0f,
                            options.FadeInDuration,
                            options.Transition,
                            options.Ease,
                            operation.Token);
                        await CloseFadeAsync();
                        break;
                }
            }
            catch
            {
                await RestoreFadeStateAsync(retainedAtStart);
                throw;
            }
        }
        finally
        {
            if (enteredGate)
            {
                _fadeGate.Release();
            }
        }
    }

    public UIRecord? Top(UILayer layer)
    {
        EnsureMainThread();
        var instance = _active.Values
            .Where(candidate => candidate.Descriptor.Layer == layer)
            .MaxBy(candidate => candidate.OpenSequence);
        return instance is null ? null : ToRecord(instance);
    }

    public async ValueTask<UIHandle> NavigateAsync(
        UIId uiId,
        object? payload = null,
        LifetimeScope? parentLifetime = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMainThread();
        await CloseTopAsync(UILayer.Screen);
        return await OpenAsync(uiId, payload, parentLifetime, cancellationToken);
    }

    /// <summary>打开页面并等待它通过 RequestClose 返回强类型结果。</summary>
    public async ValueTask<UIResult<TResult>> OpenForResultAsync<TResult>(
        UIId uiId,
        object? payload = null,
        LifetimeScope? parentLifetime = null,
        CancellationToken cancellationToken = default)
    {
        await using var handle = await OpenAsync(uiId, payload, parentLifetime, cancellationToken);
        return await handle.WaitForResultAsync<TResult>(cancellationToken);
    }

    public async ValueTask<bool> CloseTopAsync(UILayer layer)
    {
        EnsureMainThread();
        var instance = _active.Values
            .Where(candidate => candidate.Descriptor.Layer == layer)
            .MaxBy(candidate => candidate.OpenSequence);
        if (instance is null)
        {
            return false;
        }

        await CloseAsync(instance.InstanceId);
        return true;
    }

    public async ValueTask<bool> RequestBackAsync()
    {
        EnsureMainThread();
        var instance = _active.Values
            .Where(candidate => candidate.Descriptor.Layer is UILayer.Popup or UILayer.Screen)
            .OrderByDescending(candidate => candidate.Descriptor.Layer == UILayer.Popup)
            .ThenByDescending(candidate => candidate.OpenSequence)
            .FirstOrDefault();
        if (instance?.Activation is null)
        {
            return false;
        }
        if (!await instance.Screen.OnBackRequestedAsync(instance.Activation.Token))
        {
            return false;
        }

        await CloseAsync(instance.InstanceId);
        return true;
    }

    public ValueTask CloseAsync(Guid instanceId) =>
        CloseAsync(instanceId, UICompletion.Cancelled);

    private async ValueTask CloseAsync(Guid instanceId, UICompletion completion)
    {
        EnsureMainThread();
        if (!_active.Remove(instanceId, out var instance))
        {
            return;
        }

        List<Exception>? errors = null;
        if (instance.Activation is not null)
        {
            try
            {
                await instance.Screen.OnTransitionAsync(
                    UITransitionPhase.Exiting,
                    instance.Activation.Token);
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
            try
            {
                await instance.Screen.OnHideAsync(instance.Activation.Token);
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
            try
            {
                await instance.Activation.DisposeAsync();
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
            instance.Activation = null;
            instance.Screen.SetActivation(null);
        }

        var cached = false;
        if (instance.Descriptor.CachePolicy == UICachePolicy.CachedSingleton && !_disposed)
        {
            try
            {
                instance.Screen.Hide();
                instance.Screen.ProcessMode = Node.ProcessModeEnum.Disabled;
                _cache[instance.Descriptor.Id] = instance;
                cached = true;
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }

        if (!cached)
        {
            try
            {
                await instance.Lifetime.DisposeAsync();
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
            try
            {
                instance.Screen.QueueFree();
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
            try
            {
                instance.SceneLease.Dispose();
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }

        try
        {
            RefreshLayerPresentation(instance.Descriptor.Layer);
            UpdateMetrics();
        }
        catch (Exception exception)
        {
            (errors ??= []).Add(exception);
        }
        finally
        {
            // A result waiter must never hang even if user cleanup or a transition throws.
            instance.Completion?.TrySetResult(completion);
            instance.Completion = null;
        }

        if (errors is not null)
        {
            throw new AggregateException($"UI instance '{instance.Descriptor.Id}' reported cleanup errors.", errors);
        }
    }

    public async ValueTask DisposeAsync()
    {
        EnsureMainThread();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        await _fadeGate.WaitAsync();
        try
        {
            _fadeHandle = null;
            List<Exception>? errors = null;
            foreach (var instanceId in _active.Keys.ToArray())
            {
                try
                {
                    await CloseAsync(instanceId);
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
            }
            foreach (var instance in _cache.Values)
            {
                try
                {
                    await instance.Lifetime.DisposeAsync();
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
                try
                {
                    instance.Screen.QueueFree();
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
                try
                {
                    instance.SceneLease.Dispose();
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
            }

            _cache.Clear();
            _openingSingletons.Clear();
            _catalog.Clear();
            _canvas.QueueFree();
            UpdateMetrics();
            try
            {
                await _serviceLifetime.DisposeAsync();
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
            if (errors is not null)
            {
                throw new AggregateException("One or more UI instances could not be disposed.", errors);
            }
        }
        finally
        {
            _fadeGate.Release();
            _fadeGate.Dispose();
            _shutdown.Dispose();
        }
    }

    private async ValueTask DisposeUnopenedInstanceAsync(UIInstance instance)
    {
        List<Exception>? errors = null;
        if (instance.Activation is not null)
        {
            try
            {
                await instance.Activation.DisposeAsync();
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
            instance.Activation = null;
            try
            {
                instance.Screen.SetActivation(null);
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }

        instance.Completion?.TrySetResult(UICompletion.Cancelled);
        instance.Completion = null;
        if (instance.Descriptor.CachePolicy == UICachePolicy.CachedSingleton && !_disposed)
        {
            try
            {
                instance.Screen.Hide();
                instance.Screen.ProcessMode = Node.ProcessModeEnum.Disabled;
                _cache[instance.Descriptor.Id] = instance;
                UpdateMetrics();
                if (errors is not null)
                {
                    throw new AggregateException(
                        $"Unopened UI instance '{instance.Descriptor.Id}' reported cleanup errors.",
                        errors);
                }
                return;
            }
            catch (Exception exception) when (exception is not AggregateException)
            {
                (errors ??= []).Add(exception);
            }
        }

        try
        {
            await instance.Lifetime.DisposeAsync();
        }
        catch (Exception exception)
        {
            (errors ??= []).Add(exception);
        }
        try
        {
            instance.Screen.QueueFree();
        }
        catch (Exception exception)
        {
            (errors ??= []).Add(exception);
        }
        try
        {
            instance.SceneLease.Dispose();
        }
        catch (Exception exception)
        {
            (errors ??= []).Add(exception);
        }
        UpdateMetrics();
        if (errors is not null)
        {
            throw new AggregateException(
                $"Unopened UI instance '{instance.Descriptor.Id}' reported cleanup errors.",
                errors);
        }
    }

    private UIFadeTransitionScreen? TryGetFadeScreen()
    {
        if (_fadeHandle is null ||
            !_active.TryGetValue(_fadeHandle.InstanceId, out var instance))
        {
            _fadeHandle = null;
            return null;
        }

        return instance.Screen as UIFadeTransitionScreen ??
            throw new InvalidOperationException(
                $"Built-in UI '{UICatalog.UIFadeTransition.Id}' must use {nameof(UIFadeTransitionScreen)}.");
    }

    private async ValueTask<UIFadeTransitionScreen> GetOrOpenFadeScreenAsync(
        CancellationToken cancellationToken)
    {
        var existing = TryGetFadeScreen();
        if (existing is not null)
        {
            return existing;
        }

        var handle = await OpenAsync(
            UICatalog.UIFadeTransition.Id,
            parentLifetime: _serviceLifetime,
            cancellationToken: cancellationToken);
        _fadeHandle = handle;
        return TryGetFadeScreen() ??
            throw new InvalidOperationException("Fade transition UI opened without an active screen instance.");
    }

    private async ValueTask CloseFadeAsync()
    {
        var handle = _fadeHandle;
        _fadeHandle = null;
        if (handle is not null)
        {
            await handle.CloseAsync();
        }
    }

    private async ValueTask RestoreFadeStateAsync(bool retainedAtStart)
    {
        if (retainedAtStart)
        {
            TryGetFadeScreen()?.SetOpacity(1f);
            return;
        }

        await CloseFadeAsync();
    }

    private Control CreateLayerRoot(string name, int zIndex)
    {
        var root = new Control
        {
            Name = name,
            LayoutMode = 3,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = zIndex,
        };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _canvas.AddChild(root);
        return root;
    }

    private async Task CloseSafelyAsync(Guid instanceId, UICompletion completion)
    {
        try
        {
            await CloseAsync(instanceId, completion);
        }
        catch (Exception exception)
        {
            GD.PushError($"Failed to close UI instance {instanceId}: {exception}");
        }
    }

    private static void ApplyFocus(UIInstance instance)
    {
        if (instance.Descriptor.FocusPolicy != UIFocusPolicy.GrabFirst)
        {
            return;
        }

        var pending = new Queue<Node>();
        pending.Enqueue(instance.Screen);
        while (pending.Count > 0)
        {
            var node = pending.Dequeue();
            if (node is Control control &&
                control.Visible &&
                control.FocusMode != Control.FocusModeEnum.None)
            {
                control.GrabFocus();
                return;
            }
            var childCount = node.GetChildCount();
            for (var index = 0; index < childCount; index++)
            {
                pending.Enqueue(node.GetChild(index));
            }
        }
    }

    private static UIRecord ToRecord(UIInstance instance) => new(
        instance.InstanceId,
        instance.Descriptor.Id.Value,
        instance.Descriptor.Layer,
        instance.Descriptor.CachePolicy,
        instance.Descriptor.CoverPolicy,
        instance.Descriptor.InputPolicy,
        instance.Descriptor.FocusPolicy,
        instance.State,
        instance.OpenSequence);

    private void RefreshLayerPresentation(UILayer layer)
    {
        var covered = false;
        foreach (var instance in _active.Values
                     .Where(candidate => candidate.Descriptor.Layer == layer)
                     .OrderByDescending(candidate => candidate.OpenSequence))
        {
            instance.State = covered ? UIVisualState.Covered : UIVisualState.Visible;
            if (covered)
            {
                instance.Screen.Hide();
                instance.Screen.ProcessMode = Node.ProcessModeEnum.Disabled;
            }
            else
            {
                instance.Screen.Show();
                instance.Screen.ProcessMode = Node.ProcessModeEnum.Inherit;
            }

            if (instance.Descriptor.CoverPolicy == UICoverPolicy.HidePrevious)
            {
                covered = true;
            }
        }
    }

    private void UpdateMetrics()
    {
        _metrics.SetGauge("ui.active", _active.Count);
        _metrics.SetGauge("ui.cached", _cache.Count);
    }

    private void EnsureMainThread()
    {
        if (System.Environment.CurrentManagedThreadId != _mainThreadId)
        {
            throw new InvalidOperationException("UI operations must run on Godot's main thread.");
        }
    }
}

public sealed record UIRecord(
    Guid InstanceId,
    string UIId,
    UILayer Layer,
    UICachePolicy CachePolicy,
    UICoverPolicy CoverPolicy,
    UIInputPolicy InputPolicy,
    UIFocusPolicy FocusPolicy,
    UIVisualState State,
    long OpenSequence);
