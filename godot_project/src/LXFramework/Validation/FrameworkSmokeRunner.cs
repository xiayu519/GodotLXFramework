using LX.Audio;
using LX.Camera;
using LX.Content;
using LX.Core.Audio;
using LX.Core.Actions;
using LX.Core.Diagnostics;
using LX.Core.Lifetime;
using LX.Core.World;
using LX.Diagnostics;
using LX.Features;
using LX.Generated;
using LX.Input;
using LX.Localization;
using LX.Media;
using LX.Pooling;
using LX.Res;
using LX.Runtime;
using LX.Scenes;
using LX.UI;
using LX.UI.Components;
using LX.World;
using Godot;

namespace LX.Validation;

/// <summary>
/// Owns headless runtime acceptance scenarios so production host composition stays compact.
/// </summary>
internal sealed class FrameworkSmokeRunner(Node host, LXContext context)
{
    public async Task RunAsync(UIHandle? frameworkStatus, CancellationToken cancellationToken)
    {
        var LX = context;
        var probeLifetime = LX.Lifetime.CreateChild("Validation:ContextInjection");
        var initializationOrder = new List<string>();
        var rootProbe = new ContextProbeNode { Name = "RootProbe", InitializationOrder = initializationOrder };
        var childProbe = new ContextProbeNode { Name = "ChildProbe", InitializationOrder = initializationOrder };
        var siblingProbe = new ContextProbeNode { Name = "SiblingProbe", InitializationOrder = initializationOrder };
        rootProbe.AddChild(childProbe);
        rootProbe.AddChild(siblingProbe);
        var initialized = LXContextInjector.InitializeTree(rootProbe, LX, probeLifetime);
        if (initialized != 3 ||
            !rootProbe.InitializationHookCalled ||
            !childProbe.InitializationHookCalled ||
            !siblingProbe.InitializationHookCalled ||
            !rootProbe.ChildrenWereInitializedFirst ||
            !initializationOrder.SequenceEqual(["ChildProbe", "SiblingProbe", "RootProbe"]))
        {
            throw new InvalidOperationException("Recursive LXFramework context injection failed.");
        }
        rootProbe.Free();
        await probeLifetime.DisposeAsync();
        GD.Print("LX_CONTEXT_INJECTION_PASS");
        GD.Print("LX_CONTEXT_INJECTION_ORDER_PASS");

        try
        {
            LX.Res.MaxIdleCacheEntries = -1;
            throw new InvalidOperationException("Asset cache accepted a negative idle-entry limit.");
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        using var collisionAction = new StringName("ui_cancel");
        try
        {
            LX.Input.Register(
                collisionAction,
                new InputActionId("framework_smoke_collision"));
            throw new InvalidOperationException("Input router accepted a conflicting Godot action mapping.");
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("already mapped", StringComparison.Ordinal))
        {
        }

        var pauseThreadGuarded = await Task.Run(() =>
        {
            try
            {
                LX.Pause.SetPaused(false);
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        });
        if (!pauseThreadGuarded)
        {
            throw new InvalidOperationException("Pause service did not reject a background-thread call.");
        }

        host.ProcessMode = Node.ProcessModeEnum.Disabled;
        try
        {
            try
            {
                LX.Pause.SetPaused(true);
                throw new InvalidOperationException("Pause service changed state while LXHost could not process.");
            }
            catch (InvalidOperationException exception) when (
                exception.Message.Contains("suspended or disabled", StringComparison.Ordinal))
            {
            }
        }
        finally
        {
            host.ProcessMode = Node.ProcessModeEnum.Always;
        }
        if (LX.Pause.IsPaused || LX.Clock.IsPaused || LX.PhysicsClock.IsPaused || host.GetTree().Paused)
        {
            throw new InvalidOperationException("Rejected pause left framework clocks or SceneTree out of sync.");
        }

        LX.Pause.SetPaused(true);
        host.GetTree().Paused = false;
        LX.Pause.SetPaused(true);
        if (!host.GetTree().Paused || !LX.Clock.IsPaused || !LX.PhysicsClock.IsPaused)
        {
            throw new InvalidOperationException("Pause service did not repair an external SceneTree pause write.");
        }
        LX.Pause.SetPaused(false);

        var usedJsonOptions = new System.Text.Json.JsonSerializerOptions();
        _ = System.Text.Json.JsonSerializer.Serialize(new { value = 1 }, usedJsonOptions);
        _ = new ContentService(usedJsonOptions);
        try
        {
            _ = new AssetRef<Resource>("res://scene/../scene/main.tscn");
            throw new InvalidOperationException("AssetRef accepted a non-canonical resource alias.");
        }
        catch (ArgumentException)
        {
        }
        try
        {
            _ = new ContentRef<object>("res://content/../project.godot.json");
            throw new InvalidOperationException("ContentRef accepted a path outside the content boundary.");
        }
        catch (ArgumentException)
        {
        }
        GD.Print("LX_RUNTIME_GUARDS_PASS");

        const string generatedAssetKey = "generated://validation/resource_lifetime";
        Gradient generatedGradient = null!;
        var assetLifetime = LX.Lifetime.CreateChild("Validation:ResourceLease");
        _ = assetLifetime.Own(LX.Res.AcquireGenerated(
            generatedAssetKey,
            () => generatedGradient = new Gradient(),
            AssetCachePolicy.Transient));
        var activeAsset = LX.Res.Snapshot().SingleOrDefault(record => record.Path == generatedAssetKey);
        if (activeAsset is null || activeAsset.LeaseCount != 1)
        {
            throw new InvalidOperationException("Generated resource lease was not tracked while active.");
        }
        await assetLifetime.DisposeAsync();
        if (LX.Res.Snapshot().Any(record => record.Path == generatedAssetKey))
        {
            throw new InvalidOperationException("Transient resource survived its owning lifetime.");
        }
        if (GodotObject.IsInstanceValid(generatedGradient))
        {
            throw new InvalidOperationException("Registry-owned generated resource was not disposed.");
        }
        GD.Print("LX_RESOURCE_LEASE_LIFECYCLE_PASS");

        var loadOrder = new List<string>();
        var icon = new AssetRef<Texture2D>("res://icon.svg", AssetCachePolicy.Transient);
        var sharedIcon = ResourceLoader.Load<Texture2D>(icon.Path) ??
            throw new InvalidOperationException("Validation icon could not be loaded through Godot's shared cache.");
        using (var sharedLease = LX.Res.Acquire(
                   new AssetRef<Texture2D>(icon.Path, AssetCachePolicy.Cached)))
        {
            if (!ReferenceEquals(sharedIcon, sharedLease.Resource))
            {
                throw new InvalidOperationException("LX.Res did not reuse Godot's shared cached resource.");
            }
        }
        LX.Res.PurgeIdleCache();
        if (!GodotObject.IsInstanceValid(sharedIcon) || sharedIcon.GetWidth() <= 0)
        {
            throw new InvalidOperationException("Purging LX.Res disposed a ResourceLoader-owned shared resource.");
        }
        GD.Print("LX_RESOURCE_SHARED_CACHE_SAFETY_PASS");

        var observerFailure = LX.Res.AcquireAsync<Texture2D>(
            icon.Path,
            icon.CachePolicy,
            progress: _ => throw new InvalidOperationException("expected observer failure"),
            cancellationToken: cancellationToken).AsTask();
        var observerSurvivor = LX.Res.AcquireAsync(icon, cancellationToken).AsTask();
        try
        {
            _ = await observerFailure;
            throw new InvalidOperationException("A failing asset progress observer unexpectedly succeeded.");
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("expected observer failure", StringComparison.Ordinal))
        {
        }
        using (var survivorLease = await observerSurvivor)
        {
            if (survivorLease.Resource.GetWidth() <= 0)
            {
                throw new InvalidOperationException("A caller observer failure poisoned the shared asset load.");
            }
        }

        using (var cancelledObserver = new CancellationTokenSource())
        {
            var cancelledAcquire = LX.Res.AcquireAsync<Texture2D>(
                icon.Path,
                icon.CachePolicy,
                progress: _ => { },
                cancellationToken: cancelledObserver.Token).AsTask();
            var cancellationSurvivor = LX.Res.AcquireAsync(icon, cancellationToken).AsTask();
            cancelledObserver.Cancel();
            try
            {
                _ = await cancelledAcquire;
                throw new InvalidOperationException("A cancelled asset observer unexpectedly acquired a lease.");
            }
            catch (OperationCanceledException) when (cancelledObserver.IsCancellationRequested)
            {
            }
            using var survivorLease = await cancellationSurvivor;
            if (survivorLease.Resource.GetWidth() <= 0)
            {
                throw new InvalidOperationException("A cancelled caller poisoned the shared asset load.");
            }
        }

        using (var retryLease = await LX.Res.AcquireAsync(icon, cancellationToken))
        {
            if (retryLease.Resource.GetWidth() <= 0)
            {
                throw new InvalidOperationException("Asset retry failed after isolated observer termination.");
            }
        }
        if (LX.Res.Snapshot().Any(record => record.Path == icon.Path))
        {
            throw new InvalidOperationException("Shared asset observer validation leaked a transient lease.");
        }
        GD.Print("LX_RESOURCE_INFLIGHT_OBSERVER_ISOLATION_PASS");

        using (var batch = await LX.Res.AcquireBatchAsync(
                   [
                       new AssetLoadRequest<Texture2D>("low", icon),
                       new AssetLoadRequest<Texture2D>("high", icon, Priority: 10),
                       new AssetLoadRequest<Texture2D>("dependent", icon, Priority: 100, Dependencies: ["low"]),
                   ],
                   maxConcurrency: 1,
                   progress: update =>
                   {
                       if (update.CurrentId is not null)
                       {
                           loadOrder.Add(update.CurrentId);
                       }
                   },
                   cancellationToken: cancellationToken))
        {
            if (batch.Count != 3 || !loadOrder.SequenceEqual(["high", "low", "dependent"]))
            {
                throw new InvalidOperationException("Resource batch priority or dependency order was not deterministic.");
            }
            var iconRecord = LX.Res.Snapshot().Single(record => record.Path == icon.Path);
            if (iconRecord.LeaseCount != 3)
            {
                throw new InvalidOperationException("Resource batch did not retain one lease per request.");
            }
        }
        if (LX.Res.Snapshot().Any(record => record.Path == icon.Path))
        {
            throw new InvalidOperationException("Transient resource batch survived batch disposal.");
        }
        GD.Print("LX_RESOURCE_BATCH_POLICY_PASS");

        var dependencyReady = AssetDependencyAnalyzer.Analyze(
        [
            new AssetLoadRequest<Texture2D>("base", icon),
            new AssetLoadRequest<Texture2D>("dependent", icon, Dependencies: ["base"]),
        ]);
        var dependencyMissing = AssetDependencyAnalyzer.Analyze(
        [
            new AssetLoadRequest<Texture2D>("broken", icon, Dependencies: ["absent"]),
        ]);
        var dependencyCycle = AssetDependencyAnalyzer.Analyze(
        [
            new AssetLoadRequest<Texture2D>("left", icon, Dependencies: ["right"]),
            new AssetLoadRequest<Texture2D>("right", icon, Dependencies: ["left"]),
        ]);
        if (!dependencyReady.LoadOrder.SequenceEqual(["base", "dependent"]) ||
            dependencyMissing.Status != AssetPlanStatus.MissingDependency ||
            dependencyCycle.Status != AssetPlanStatus.Cycle)
        {
            throw new InvalidOperationException("Asset dependency analysis did not report a stable plan and failures.");
        }
        using (var preloadSet = await LX.Res.PreloadAsync(
                   new AssetPreloadSet<Texture2D>(
                       "validation.icons",
                       [new AssetLoadRequest<Texture2D>("icon", icon)]),
                   maxConcurrency: 1,
                   cancellationToken: cancellationToken))
        {
            if (preloadSet.Count != 1)
            {
                throw new InvalidOperationException("Named asset preload set did not retain its resource lease.");
            }
        }
        GD.Print("LX_RESOURCE_PRELOAD_PLAN_PASS");

        const string atlasPath = "res://scene/validation/icon_atlas.tres";
        var dynamicTextureTarget = new TextureRect { Name = "DynamicTextureValidation" };
        host.AddChild(dynamicTextureTarget);
        var dynamicTextureLifetime = LX.Lifetime.CreateChild("Validation:DynamicTexture");
        var dynamicTexture = UITextureBinding.Create(
            LX.Res,
            dynamicTextureLifetime,
            dynamicTextureTarget);
        var atlas = new AssetRef<AtlasTexture>(atlasPath, AssetCachePolicy.Transient);
        for (var cycle = 0; cycle < 8; cycle++)
        {
            if (!await dynamicTexture.SetAsync(atlas, cancellationToken) ||
                dynamicTextureTarget.Texture is not AtlasTexture loadedAtlas ||
                loadedAtlas.Atlas is null ||
                LX.Res.Snapshot().Single(record => record.Path == atlasPath).LeaseCount != 1)
            {
                throw new InvalidOperationException("Dynamic AtlasTexture binding did not own its active resource.");
            }

            dynamicTexture.Clear();
            if (dynamicTextureTarget.Texture is not null ||
                LX.Res.Snapshot().Any(record => record.Path == atlasPath))
            {
                throw new InvalidOperationException("Dynamic AtlasTexture binding retained a released resource.");
            }
        }
        await dynamicTextureLifetime.DisposeAsync();
        dynamicTextureTarget.Free();
        GD.Print("LX_DYNAMIC_TEXTURE_ATLAS_LIFECYCLE_PASS");

        var sceneProgress = new List<SceneLoadProgress>();
        using (var scenePreload = await LX.Scenes.PreloadAsync(
                   "res://scene/validation/context_probe.tscn",
                   sceneProgress.Add,
                   cancellationToken))
        {
            if (scenePreload.ScenePath != "res://scene/validation/context_probe.tscn" ||
                sceneProgress.Count < 2 ||
                sceneProgress[0].Ratio != 0 ||
                sceneProgress[^1].Stage != SceneLoadStage.Ready ||
                sceneProgress[^1].Ratio != 1)
            {
                throw new InvalidOperationException("Scene preload did not report a complete progress lifecycle.");
            }
        }
        GD.Print("LX_SCENE_PRELOAD_PROGRESS_PASS");

        var inputContextsBefore = LX.Input.Snapshot().Contexts
            .Select(InputContextSignature)
            .ToArray();
        using (var inputContext = LX.Input.PushContext(new InputContextDescriptor(
                   "validation.menu",
                   new HashSet<InputActionId> { InputCatalog.Confirm, InputCatalog.Cancel },
                   InputContextMode.Exclusive)))
        {
            var snapshot = LX.Input.Snapshot();
            var prompt = LX.Input.Prompt(InputCatalog.Confirm);
            if (snapshot.Contexts.Count(context =>
                    context.Id == "validation.menu") != 1 ||
                snapshot.Contexts.Count != inputContextsBefore.Length + 1 ||
                !snapshot.Contexts
                    .Where(context => context.Id != "validation.menu")
                    .Select(InputContextSignature)
                    .SequenceEqual(inputContextsBefore) ||
                string.IsNullOrWhiteSpace(prompt.Text))
            {
                throw new InvalidOperationException("Input context or human-readable prompt was not observable.");
            }
        }
        if (!LX.Input.Snapshot().Contexts
                .Select(InputContextSignature)
                .SequenceEqual(inputContextsBefore))
        {
            throw new InvalidOperationException(
                "Disposed validation input context did not restore the routing stack.");
        }
        GD.Print("LX_INPUT_CONTEXT_PASS");

        using (var menuAction = new StringName("lx_menu"))
        {
            var defaultKeys = InputMap.ActionGetEvents(menuAction)
                .OfType<InputEventKey>()
                .Select(inputEvent => (inputEvent.Keycode, inputEvent.PhysicalKeycode))
                .ToArray();
            LX.Input.ReplaceKeyBinding(menuAction, Key.F12);
            LX.Input.RestoreDefaultKeyBinding(menuAction);
            var restoredKeys = InputMap.ActionGetEvents(menuAction)
                .OfType<InputEventKey>()
                .Select(inputEvent => (inputEvent.Keycode, inputEvent.PhysicalKeycode))
                .ToArray();
            if (!restoredKeys.SequenceEqual(defaultKeys))
            {
                throw new InvalidOperationException("Removing a custom key binding did not restore its default events.");
            }
        }
        GD.Print("LX_INPUT_DEFAULT_BINDING_RESTORE_PASS");

        const string missingLocalizationKey = "__lx_framework_missing_probe__";
        LX.Localization.ClearMissingKeys();
        LX.Localization.PseudoLocalizationEnabled = true;
        using (var localizationKey = new StringName(missingLocalizationKey))
        {
            var pseudoText = LX.Localization.Text(localizationKey);
            var variants = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [LX.Localization.CurrentLocale.Replace('-', '_')] = "res://localized/current.png",
                ["en"] = "res://localized/fallback.png",
            };
            if (!pseudoText.StartsWith('［') ||
                !LX.Localization.MissingKeys.Contains(missingLocalizationKey, StringComparer.Ordinal) ||
                LX.Localization.ResolveVariant(variants) != "res://localized/current.png")
            {
                throw new InvalidOperationException("Localization QA state or resource variant resolution failed.");
            }
        }
        LX.Localization.PseudoLocalizationEnabled = false;
        LX.Localization.ClearMissingKeys();
        GD.Print("LX_LOCALIZATION_QA_PASS");

        var silence = new PcmWave(8000, 1, 16, new byte[16000]);
        var rejectGroup = new AudioGroupPolicy(
            "validation.reject",
            MaxConcurrent: 1,
            OverflowPolicy: AudioOverflowPolicy.RejectNew);
        using (var firstCancellation = new CancellationTokenSource())
        {
            var first = LX.Audio.PlayPcmSfxAsync(
                "reject_first", silence, rejectGroup, cancellationToken: firstCancellation.Token).AsTask();
            var rejected = await LX.Audio.PlayPcmSfxAsync("reject_second", silence, rejectGroup);
            if (rejected != AudioPlayResult.Rejected ||
                LX.Audio.SnapshotGroups().Single(group => group.Id == rejectGroup.Id).Voices != 1)
            {
                throw new InvalidOperationException("Audio reject-new group policy did not enforce its limit.");
            }
            firstCancellation.Cancel();
            try
            {
                await first;
                throw new InvalidOperationException("Cancelled validation SFX completed without cancellation.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        var preemptGroup = new AudioGroupPolicy(
            "validation.preempt",
            MaxConcurrent: 1,
            OverflowPolicy: AudioOverflowPolicy.StopOldest);
        using (var newestCancellation = new CancellationTokenSource())
        {
            var oldest = LX.Audio.PlayPcmSfxAsync("preempt_old", silence, preemptGroup).AsTask();
            var newest = LX.Audio.PlayPcmSfxAsync(
                "preempt_new", silence, preemptGroup, cancellationToken: newestCancellation.Token).AsTask();
            if (await oldest != AudioPlayResult.Preempted)
            {
                throw new InvalidOperationException("Audio stop-oldest group policy did not preempt the oldest voice.");
            }
            newestCancellation.Cancel();
            try
            {
                await newest;
                throw new InvalidOperationException("Cancelled replacement SFX completed without cancellation.");
            }
            catch (OperationCanceledException)
            {
            }
        }
        if (LX.Audio.SnapshotGroups().Any(group => group.Id.StartsWith("validation.", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Audio validation groups retained voices after completion.");
        }
        GD.Print("LX_AUDIO_GROUP_POLICY_PASS");

        LX.Audio.PlayPcmMusic("validation_fade", silence, volumeDb: -12);
        await LX.Audio.FadeMusicVolumeAsync(-6, TimeSpan.FromMilliseconds(20), cancellationToken);
        var audioState = LX.Audio.Snapshot();
        if (!audioState.MusicPlaying || Math.Abs(audioState.MusicVolumeDb - (-6)) > 0.01f)
        {
            throw new InvalidOperationException("Music fade did not reach the requested volume.");
        }
        LX.Audio.StopMusic();
        GD.Print("LX_AUDIO_FADE_PASS");

        var featureId = new FeatureId("framework_smoke_probe");
        LX.Features.Register(new FeatureDescriptor(
            featureId,
            "res://scene/validation/context_probe.tscn"));
        var feature = await LX.Features.SpawnAsync(
            featureId,
            host,
            LX.Lifetime,
            cancellationToken);
        if (feature.Node is not ContextProbeNode featureRoot ||
            featureRoot.GetNode("ContextProbeChild") is not ContextProbeNode featureChild ||
            !featureRoot.InitializationHookCalled ||
            !featureChild.InitializationHookCalled ||
            LX.Features.Snapshot().Count != 1)
        {
            throw new InvalidOperationException("Feature spawn and recursive initialization failed.");
        }
        await feature.DisposeAsync();
        if (!feature.IsDisposed || LX.Features.Snapshot().Count != 0)
        {
            throw new InvalidOperationException("Feature despawn did not release its handle or active record.");
        }

        var featureOwner = LX.Lifetime.CreateChild("Validation:FeatureOwner");
        var ownerBoundFeature = await LX.Features.SpawnAsync(
            featureId,
            host,
            featureOwner,
            cancellationToken);
        await featureOwner.DisposeAsync();
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!ownerBoundFeature.IsDisposed || LX.Features.Snapshot().Count != 0)
        {
            throw new InvalidOperationException(
                "Feature owner lifetime did not update its handle or active record.");
        }
        GD.Print("LX_FEATURE_LIFECYCLE_PASS");

        const string prefabPath = "res://scene/validation/context_probe.tscn";
        LX.Res.PurgeIdleCache();
        var prefabLifetime = LX.Lifetime.CreateChild("Validation:PackedSceneInstance");
        for (var cycle = 0; cycle < 4; cycle++)
        {
            var prefab = await PackedSceneInstance<ContextProbeNode>.CreateAsync(
                LX,
                new AssetRef<PackedScene>(prefabPath, AssetCachePolicy.Transient),
                host,
                prefabLifetime,
                cancellationToken);
            var prefabNode = prefab.Node;
            if (!prefabNode.IsLXInitialized ||
                LX.Res.Snapshot().Single(record => record.Path == prefabPath).LeaseCount != 1)
            {
                throw new InvalidOperationException("PackedScene instance did not own its injected node and lease.");
            }

            await prefab.DisposeAsync();
            if (GodotObject.IsInstanceValid(prefabNode) ||
                LX.Res.Snapshot().Any(record => record.Path == prefabPath))
            {
                throw new InvalidOperationException("PackedScene instance did not complete its release boundary.");
            }
        }
        await prefabLifetime.DisposeAsync();
        GD.Print("LX_PACKED_SCENE_INSTANCE_LIFECYCLE_PASS");

        var poolLifetime = LX.Lifetime.CreateChild("Validation:PackedScenePool");
        var pool = await PackedSceneNodePool<ContextProbeNode>.CreateAsync(
            LX,
            "res://scene/validation/context_probe.tscn",
            poolLifetime,
            maxRetained: 2,
            cancellationToken: cancellationToken);
        var pooled = pool.RentLease(host, node => node.ConfiguredBeforeTree = !node.IsInsideTree());
        var firstPooledNode = pooled.Node;
        if (!firstPooledNode.IsLXInitialized ||
            !firstPooledNode.ConfiguredBeforeTree ||
            firstPooledNode.PoolRentCount != 1 ||
            firstPooledNode.LastPoolActivationToken.IsCancellationRequested ||
            pool.RentedCount != 1)
        {
            throw new InvalidOperationException("Packed-scene pool did not configure, activate or track its rented node.");
        }
        pooled.Dispose();
        if (firstPooledNode.PoolReturnCount != 1 ||
            !firstPooledNode.LastPoolActivationToken.IsCancellationRequested ||
            pool.RentedCount != 0 ||
            pool.RetainedCount != 1)
        {
            throw new InvalidOperationException("Packed-scene pool did not retain the returned node.");
        }
        var leaseLifetime = poolLifetime.CreateChild("Validation:PooledNodeLease");
        _ = pool.RentLease(host, leaseLifetime);
        if (pool.RentedCount != 1)
        {
            throw new InvalidOperationException("Lifetime-owned pooled node lease was not tracked as rented.");
        }
        await leaseLifetime.DisposeAsync();
        if (pool.RentedCount != 0 || pool.RetainedCount != 1 || pool.Statistics.Reused < 1)
        {
            throw new InvalidOperationException("Lifetime disposal did not return and retain the pooled node.");
        }

        var activeDuringOwnerShutdown = pool.Rent(host);
        await poolLifetime.DisposeAsync();
        if (activeDuringOwnerShutdown.PoolReturnCount < 1 ||
            !activeDuringOwnerShutdown.PoolReturnObservedActiveToken ||
            !activeDuringOwnerShutdown.LastPoolActivationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "Pool owner shutdown did not call OnReturn before cancelling the active rental lifetime.");
        }

        using (var invalidNodePool = new NodePool<Node>(() => new Node(), maxRetained: 1))
        {
            var invalidPooledNode = invalidNodePool.Rent(host);
            invalidPooledNode.QueueFree();
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            try
            {
                invalidNodePool.Return(invalidPooledNode);
                throw new InvalidOperationException("Node pool retained a freed node.");
            }
            catch (InvalidOperationException exception) when (
                exception.Message.Contains("cannot be retained", StringComparison.Ordinal))
            {
            }
            if (invalidNodePool.RentedCount != 0 || invalidNodePool.RetainedCount != 0)
            {
                throw new InvalidOperationException("Node pool accounting retained a freed node.");
            }
        }
        GD.Print("LX_PACKED_SCENE_POOL_PASS");

        var cameraLifetime = LX.Lifetime.CreateChild("Validation:Camera2DController");
        var camera = new Camera2D { Name = "ValidationCamera", Enabled = false };
        var secondCamera = new Camera2D { Name = "ValidationCameraSecond", Enabled = false };
        var cameraTarget = new Node2D { Name = "ValidationCameraTarget" };
        host.AddChild(camera);
        host.AddChild(secondCamera);
        host.AddChild(cameraTarget);
        camera.GlobalPosition = new Vector2(10, 10);
        secondCamera.GlobalPosition = new Vector2(20, 20);
        cameraTarget.GlobalPosition = new Vector2(120, 50);
        var cameraController = Camera2DController.Attach(camera, cameraLifetime);
        _ = Camera2DController.Attach(secondCamera, cameraLifetime);
        cameraController.Follow(cameraTarget, new Camera2DFollowOptions
        {
            DeadZoneSize = new Vector2(10, 10),
            SmoothingSpeed = 0,
        });
        cameraController.SetCenterBounds(new Rect2(0, 0, 100, 100));
        cameraController._Process(1.0 / 60.0);
        if (!camera.GlobalPosition.IsEqualApprox(new Vector2(100, 45)) ||
            !secondCamera.GlobalPosition.IsEqualApprox(new Vector2(20, 20)))
        {
            throw new InvalidOperationException(
                "Camera2D controller follow, dead-zone, bounds or per-camera isolation failed.");
        }
        try
        {
            _ = Camera2DController.Attach(camera, cameraLifetime);
            throw new InvalidOperationException("Camera2D accepted two controllers for the same camera.");
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("already has", StringComparison.Ordinal))
        {
        }
        cameraController.Shake(4, TimeSpan.FromSeconds(1), 8);
        cameraController._Process(0.1);
        if (camera.Offset.IsEqualApprox(Vector2.Zero))
        {
            throw new InvalidOperationException("Camera2D controller shake did not affect Camera2D.Offset.");
        }
        cameraController.StopShake();
        if (!camera.Offset.IsEqualApprox(Vector2.Zero))
        {
            throw new InvalidOperationException("Camera2D controller did not restore its base offset.");
        }
        await cameraLifetime.DisposeAsync();
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        if (GodotObject.IsInstanceValid(cameraController))
        {
            throw new InvalidOperationException("Camera2D controller outlived its owning lifetime.");
        }
        camera.Free();
        secondCamera.Free();
        cameraTarget.Free();
        GD.Print("LX_CAMERA_2D_CONTROLLER_PASS");

        var chunkParent = new Node2D { Name = "ValidationChunks" };
        host.AddChild(chunkParent);
        var chunkOwner = LX.Lifetime.CreateChild("Validation:WorldChunkStreaming");
        var chunkSource = new ValidationWorldChunkSource(
            [new ChunkCoordinate(0, 0), new ChunkCoordinate(1, 0)],
            coordinate => new Node2D { Name = $"ValidationChunk_{coordinate.X}_{coordinate.Y}" });
        var chunkStreamer = new WorldChunkStreamer(
            chunkParent,
            chunkSource,
            chunkOwner,
            LX.Metrics,
            () => LX);
        var chunkProgress = new List<(int Completed, int Total)>();
        await chunkStreamer.SetFocusAsync(
            new ChunkCoordinate(0, 0),
            new WorldChunkStreamingOptions
            {
                Radius = 0,
                Progress = (completed, total) => chunkProgress.Add((completed, total)),
            },
            cancellationToken);
        if (!chunkProgress.SequenceEqual([(0, 1), (1, 1)]) ||
            !chunkStreamer.ActiveChunks.SequenceEqual([new ChunkCoordinate(0, 0)]))
        {
            throw new InvalidOperationException("World chunk loading progress was not deterministic.");
        }

        chunkProgress.Clear();
        await chunkStreamer.SetFocusAsync(
            new ChunkCoordinate(1, 0),
            new WorldChunkStreamingOptions
            {
                Radius = 0,
                Progress = (completed, total) => chunkProgress.Add((completed, total)),
            },
            cancellationToken);
        var positionedChunk = chunkParent.GetNodeOrNull<Node2D>("ValidationChunk_1_0");
        if (!chunkProgress.SequenceEqual([(0, 2), (1, 2), (2, 2)]) ||
            positionedChunk is null ||
            positionedChunk.Position != new Vector2(32, 0))
        {
            throw new InvalidOperationException("World chunk replacement progress or positioning was incorrect.");
        }
        await chunkStreamer.DisposeAsync();

        var invalidChunkSource = new ValidationWorldChunkSource(
            [new ChunkCoordinate(0, 0)],
            _ => new Node { Name = "InvalidChunkRoot" });
        var invalidChunkStreamer = new WorldChunkStreamer(
            chunkParent,
            invalidChunkSource,
            chunkOwner,
            LX.Metrics,
            () => LX);
        try
        {
            await invalidChunkStreamer.SetFocusAsync(
                new ChunkCoordinate(0, 0),
                new WorldChunkStreamingOptions { Radius = 0 },
                cancellationToken);
            throw new InvalidOperationException("World chunk streaming accepted a non-Node2D root.");
        }
        catch (InvalidDataException exception) when (
            exception.Message.Contains("must derive from Node2D", StringComparison.Ordinal))
        {
        }
        if (invalidChunkSource.LastCreated is null ||
            GodotObject.IsInstanceValid(invalidChunkSource.LastCreated) ||
            invalidChunkStreamer.ActiveChunks.Count != 0)
        {
            throw new InvalidOperationException("Invalid world chunk root was not released atomically.");
        }
        await invalidChunkStreamer.DisposeAsync();
        await chunkOwner.DisposeAsync();

        var autoOwnedChunkOwner = LX.Lifetime.CreateChild("Validation:AutoOwnedWorldChunkStreaming");
        var autoOwnedChunkSource = new ValidationWorldChunkSource(
            [new ChunkCoordinate(0, 0)],
            _ => new Node2D { Name = "AutoOwnedValidationChunk" });
        var autoOwnedChunkStreamer = new WorldChunkStreamer(
            chunkParent,
            autoOwnedChunkSource,
            autoOwnedChunkOwner,
            LX.Metrics,
            () => LX);
        await autoOwnedChunkStreamer.SetFocusAsync(
            new ChunkCoordinate(0, 0),
            radius: 0,
            cancellationToken);
        await autoOwnedChunkOwner.DisposeAsync();
        if (!autoOwnedChunkSource.IsDisposed ||
            autoOwnedChunkSource.LastCreated is null ||
            GodotObject.IsInstanceValid(autoOwnedChunkSource.LastCreated) ||
            LX.Metrics.Snapshot().Gauges.GetValueOrDefault("world.chunks_active") != 0)
        {
            throw new InvalidOperationException(
                "World chunk streamer was not closed by its declared parent lifetime.");
        }
        chunkParent.Free();
        GD.Print("LX_WORLD_CHUNK_STREAMING_PROGRESS_PASS");

        var worldEventId = new WorldEventId("validation.world_event");
        LX.WorldEvents.TryComplete(worldEventId);
        var triggerLifetime = LX.Lifetime.CreateChild("Validation:WorldEventTrigger");
        var trigger = new WorldEventTrigger2D
        {
            Name = "WorldEventTrigger",
            EventId = worldEventId.Value,
            RequiredBodyGroup = new StringName(string.Empty),
        };
        LXContextInjector.InitializeTree(trigger, LX, triggerLifetime);
        host.AddChild(trigger);
        if (trigger.Monitoring)
        {
            throw new InvalidOperationException("Completed world event trigger remained active after entering the tree.");
        }
        trigger.ResetCompletion();
        if (!trigger.Monitoring || LX.WorldEvents.IsCompleted(worldEventId))
        {
            throw new InvalidOperationException("World event trigger reset did not restore activation state.");
        }
        var triggerActor = new Node2D { Name = "WorldEventActor" };
        if (!trigger.TryTrigger(triggerActor) || trigger.TryTrigger(triggerActor) ||
            !LX.WorldEvents.IsCompleted(worldEventId))
        {
            throw new InvalidOperationException("World event trigger did not enforce one-shot activation.");
        }
        triggerActor.Free();
        await triggerLifetime.DisposeAsync();
        trigger.Free();
        GD.Print("LX_WORLD_EVENT_TRIGGER_PASS");

        if (frameworkStatus is null || !LX.UI.IsOpen(UICatalog.FrameworkStatus.Id))
        {
            throw new InvalidOperationException("Framework status UI did not open during smoke validation.");
        }
        var uiCanvas = host.GetNodeOrNull<CanvasLayer>("LXUI");
        if (uiCanvas is null || uiCanvas.Layer != 100 || uiCanvas.FollowViewportEnabled)
        {
            throw new InvalidOperationException("LX.UI is not using the fixed foreground CanvasLayer contract.");
        }
        await frameworkStatus.CloseAsync();
        if (LX.UI.IsOpen(UICatalog.FrameworkStatus.Id))
        {
            throw new InvalidOperationException("Framework status UI did not close during smoke validation.");
        }

        _ = await LX.UI.OpenAsync(
            UICatalog.FrameworkStatus.Id,
            parentLifetime: LX.Lifetime,
            cancellationToken: cancellationToken);
        if (!await LX.UI.RequestBackAsync() || LX.UI.IsOpen(UICatalog.FrameworkStatus.Id))
        {
            throw new InvalidOperationException("UI back navigation failed during smoke validation.");
        }

        var baseUiId = new UIId("validation.cover_base");
        var coverUiId = new UIId("validation.cover_top");
        LX.UI.Register(new UIDescriptor(
            baseUiId,
            UICatalog.FrameworkStatus.ScenePath,
            UILayer.Screen,
            UICachePolicy.Transient));
        LX.UI.Register(new UIDescriptor(
            coverUiId,
            UICatalog.FrameworkStatus.ScenePath,
            UILayer.Screen,
            UICachePolicy.Transient,
            UICoverPolicy.HidePrevious));
        var baseUi = await LX.UI.OpenAsync(
            baseUiId, parentLifetime: LX.Lifetime, cancellationToken: cancellationToken);
        var coverUi = await LX.UI.OpenAsync(
            coverUiId, parentLifetime: LX.Lifetime, cancellationToken: cancellationToken);
        var coveredRecords = LX.UI.Snapshot();
        if (coveredRecords.Single(record => record.UIId == baseUiId.Value).State != UIVisualState.Covered ||
            coveredRecords.Single(record => record.UIId == coverUiId.Value).State != UIVisualState.Visible)
        {
            throw new InvalidOperationException("UI hide-previous cover policy did not update visual states.");
        }
        await coverUi.CloseAsync();
        if (LX.UI.Snapshot().Single(record => record.UIId == baseUiId.Value).State != UIVisualState.Visible)
        {
            throw new InvalidOperationException("UI cover removal did not reveal the previous screen.");
        }
        await baseUi.CloseAsync();
        GD.Print("LX_UI_COVER_POLICY_PASS");

        var resultUiId = new UIId("validation.result_probe");
        LX.UI.Register(new UIDescriptor(
            resultUiId,
            "res://scene/validation/ui_result_probe.tscn",
            UILayer.Popup,
            UICachePolicy.CachedSingleton,
            UICoverPolicy.KeepVisible,
            UIInputPolicy.Modal,
            UIFocusPolicy.Preserve));
        var resultHandle = await LX.UI.OpenAsync(
            resultUiId, parentLifetime: LX.Lifetime, cancellationToken: cancellationToken);
        var resultScreen = uiCanvas.FindChild("UIResultProbe", recursive: true, owned: false)
            as UIResultProbeScreen ??
            throw new InvalidOperationException("UI result probe screen was not instanced.");
        if (resultScreen.EnterTransitions != 1 || resultScreen.MouseFilter != Control.MouseFilterEnum.Stop)
        {
            throw new InvalidOperationException("UI transition or modal input policy was not applied.");
        }
        var resultTask = resultHandle.WaitForResultAsync<string>(cancellationToken).AsTask();
        resultScreen.Complete("accepted");
        var result = await resultTask;
        if (!result.HasValue ||
            result.Value != "accepted" ||
            resultScreen.ExitTransitions != 1 ||
            !resultHandle.IsClosed)
        {
            throw new InvalidOperationException(
                "UI strong-type result, exit transition, or handle closure did not complete.");
        }
        GD.Print("LX_UI_RESULT_TRANSITION_PASS");

        var resultOwner = LX.Lifetime.CreateChild("Validation:UIActivationOwner");
        var ownerBoundHandle = await LX.UI.OpenAsync(
            resultUiId,
            parentLifetime: resultOwner,
            cancellationToken: cancellationToken);
        var ownerBoundCompletion = ownerBoundHandle.WaitForResultAsync<string>(cancellationToken).AsTask();
        await resultOwner.DisposeAsync();
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        _ = await ownerBoundCompletion;
        if (resultScreen.HideSawDisposedActivation ||
            LX.UI.IsOpen(resultUiId) ||
            !ownerBoundHandle.IsClosed)
        {
            throw new InvalidOperationException(
                "UI parent cancellation disposed activation before OnHideAsync completed.");
        }
        GD.Print("LX_UI_ACTIVATION_OWNERSHIP_PASS");

        var instantFade = new UIFadeOptions
        {
            FadeOutDuration = TimeSpan.Zero,
            HoldDuration = TimeSpan.Zero,
            FadeInDuration = TimeSpan.Zero,
            Transition = Tween.TransitionType.Quad,
            Ease = Tween.EaseType.Out,
        };
        await LX.UI.PlayFadeAsync(UIFadeMode.FadeOut, instantFade, cancellationToken);
        var fadeScreen = uiCanvas.FindChild("UIFadeTransition", recursive: true, owned: false)
            as UIFadeTransitionScreen ??
            throw new InvalidOperationException("UI fade transition prefab was not instanced.");
        var blackout = fadeScreen.GetNode<ColorRect>("%Blackout");
        if (!LX.UI.IsFadeBlackoutActive ||
            !LX.UI.IsOpen(UICatalog.UIFadeTransition.Id) ||
            blackout.Color != Colors.Black ||
            blackout.AnchorRight != 1f ||
            blackout.AnchorBottom != 1f)
        {
            throw new InvalidOperationException("UI FadeOut did not retain a full-screen black overlay.");
        }

        await LX.UI.PlayFadeAsync(UIFadeMode.FadeIn, instantFade, cancellationToken);
        if (LX.UI.IsFadeBlackoutActive || LX.UI.IsOpen(UICatalog.UIFadeTransition.Id))
        {
            throw new InvalidOperationException("UI FadeIn did not release the retained black overlay.");
        }

        await LX.UI.PlayFadeAsync(UIFadeMode.FadeOutIn, instantFade, cancellationToken);
        if (LX.UI.IsFadeBlackoutActive || LX.UI.IsOpen(UICatalog.UIFadeTransition.Id))
        {
            throw new InvalidOperationException("Complete UI fade did not close after returning to transparent.");
        }

        var animatedFade = instantFade with
        {
            FadeOutDuration = TimeSpan.FromMilliseconds(40),
            FadeInDuration = TimeSpan.FromMilliseconds(40),
            Transition = Tween.TransitionType.Sine,
            Ease = Tween.EaseType.InOut,
        };
        var queuedFadeOut = LX.UI.PlayFadeAsync(
            UIFadeMode.FadeOut,
            animatedFade,
            cancellationToken).AsTask();
        var queuedFadeIn = LX.UI.PlayFadeAsync(
            UIFadeMode.FadeIn,
            animatedFade,
            cancellationToken).AsTask();
        await Task.WhenAll(queuedFadeOut, queuedFadeIn);
        if (LX.UI.IsFadeBlackoutActive || LX.UI.IsOpen(UICatalog.UIFadeTransition.Id))
        {
            throw new InvalidOperationException("Queued UI fade requests did not execute serially.");
        }

        using (var fadeCancellation = new CancellationTokenSource())
        {
            var cancelledFade = LX.UI.PlayFadeAsync(
                UIFadeMode.FadeOut,
                animatedFade with { FadeOutDuration = TimeSpan.FromSeconds(1) },
                fadeCancellation.Token).AsTask();
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            fadeCancellation.Cancel();
            try
            {
                await cancelledFade;
                throw new InvalidOperationException("UI fade ignored cancellation.");
            }
            catch (OperationCanceledException)
            {
            }
        }
        if (LX.UI.IsFadeBlackoutActive || LX.UI.IsOpen(UICatalog.UIFadeTransition.Id))
        {
            throw new InvalidOperationException("Cancelled UI fade did not roll back its new overlay.");
        }

        try
        {
            await LX.UI.PlayFadeAsync(
                UIFadeMode.FadeOut,
                instantFade with { FadeOutDuration = TimeSpan.FromMilliseconds(-1) },
                cancellationToken);
            throw new InvalidOperationException("UI fade accepted a negative duration.");
        }
        catch (ArgumentOutOfRangeException)
        {
        }
        GD.Print("LX_UI_FADE_TRANSITION_PASS");

        var virtualList = new VirtualListView
        {
            Name = "ValidationVirtualList",
            Size = new Vector2(240, 80),
        };
        host.AddChild(virtualList);
        virtualList.Configure(100, () => new Control(), (_, _) => { });
        var virtualContent = virtualList.GetNode<Control>("VirtualContent");
        var compactPoolSize = virtualContent.GetChildCount();
        virtualList.Size = new Vector2(240, 480);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        if (virtualContent.GetChildCount() <= compactPoolSize)
        {
            throw new InvalidOperationException("Virtual list did not grow its item pool after resizing.");
        }
        virtualList.QueueFree();

        var toast = new ToastView { Name = "ValidationToast" };
        host.AddChild(toast);
        using (var toastCancellation = new CancellationTokenSource())
        {
            var toastTask = toast.ShowMessageAsync(
                "cancel",
                durationSeconds: 10,
                toastCancellation.Token).AsTask();
            toastCancellation.Cancel();
            try
            {
                await toastTask.WaitAsync(TimeSpan.FromMilliseconds(200));
                throw new InvalidOperationException("Toast timer ignored cancellation.");
            }
            catch (OperationCanceledException)
            {
            }
        }
        if (toast.Visible)
        {
            throw new InvalidOperationException("Cancelled toast remained visible.");
        }
        toast.QueueFree();

        var confirm = new ConfirmDialogView { Name = "ValidationConfirm" };
        host.AddChild(confirm);
        var confirmTask = confirm.ShowPromptAsync("exit", cancellationToken).AsTask();
        confirm.QueueFree();
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        try
        {
            await confirmTask.WaitAsync(TimeSpan.FromMilliseconds(200));
            throw new InvalidOperationException("Confirm dialog tree exit left its prompt pending.");
        }
        catch (OperationCanceledException)
        {
        }
        GD.Print("LX_UI_COMPONENT_LIFECYCLE_PASS");

        var metrics = LX.Metrics.Snapshot();
        if (!metrics.Counters.TryGetValue("validation.ui_context_injected", out var injections) ||
            injections < 1)
        {
            throw new InvalidOperationException("UI context injection was not observed during smoke validation.");
        }
        GD.Print("LX_UI_LIFECYCLE_PASS");

        var actionSnapshotBefore = LX.Actions.Snapshot();
        var knownActionIds = actionSnapshotBefore.Active
            .Concat(actionSnapshotBefore.Recent)
            .Select(root => root.Id)
            .ToHashSet();
        var actionOwner = LX.Lifetime.CreateChild("Validation:Actions");
        var actionOrder = new List<int>();
        await LX.Actions.RunAsync(
            LXActions.Sequence(
                LXActions.Invoke(() => actionOrder.Add(1), "validation.action.first"),
                LXActions.Delay(TimeSpan.Zero, "validation.action.yield"),
                LXActions.Invoke(() => actionOrder.Add(2), "validation.action.second")),
            actionOwner,
            cancellationToken);
        await actionOwner.DisposeAsync();
        var actionSnapshot = LX.Actions.Snapshot();
        var completedValidationActions = actionSnapshot.Recent.Where(root =>
            !knownActionIds.Contains(root.Id) &&
            root.Name == "sequence" &&
            root.State == ActionNodeState.Completed &&
            root.Children.Select(child => child.Name)
                .SequenceEqual([
                    "validation.action.first",
                    "validation.action.yield",
                    "validation.action.second",
                ]))
            .ToArray();
        if (!actionOrder.SequenceEqual([1, 2]) ||
            completedValidationActions.Length != 1 ||
            actionSnapshot.Active.Any(root => !knownActionIds.Contains(root.Id)))
        {
            throw new InvalidOperationException("LX.Actions execution or diagnostics were incomplete.");
        }
        GD.Print("LX_ACTIONS_LIFETIME_PASS");

        var videoLifetime = LX.Lifetime.CreateChild("Validation:VideoSequence");
        var videoPlayer = new VideoSequencePlayer { Name = "ValidationVideoSequence" };
        LXContextInjector.InitializeTree(videoPlayer, LX, videoLifetime);
        host.AddChild(videoPlayer);
        var emptyVideoResult = await videoPlayer.PlayAsync([], cancellationToken);
        var duplicateVideo = new VideoSequenceItem(
            "duplicate",
            new AssetRef<VideoStream>("res://icon.svg"));
        try
        {
            await videoPlayer.PlayAsync([duplicateVideo, duplicateVideo], cancellationToken);
            throw new InvalidOperationException("Video sequence accepted duplicate stable item IDs.");
        }
        catch (ArgumentException)
        {
        }
        if (emptyVideoResult.State != VideoSequenceState.Completed ||
            videoPlayer.Snapshot().State != VideoSequenceState.Completed)
        {
            throw new InvalidOperationException("Video sequence did not complete an empty sequence deterministically.");
        }
        videoPlayer.QueueFree();
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        await videoLifetime.DisposeAsync();
        GD.Print("LX_VIDEO_SEQUENCE_CONTRACT_PASS");

        LX.Diagnostics.Log(
            DiagnosticSeverity.Information,
            "validation",
            "runtime snapshot probe",
            fields: new Dictionary<string, string> { ["scenario"] = "smoke" });
        var runtimeSnapshot = LX.Diagnostics.Snapshot();
        var snapshotPath = LX.Diagnostics.WriteSnapshot("user://lx-runtime-smoke.json");
        if (runtimeSnapshot.Schema != DiagnosticsService.SnapshotSchema ||
            runtimeSnapshot.SchemaVersion != DiagnosticsService.SnapshotSchemaVersion ||
            runtimeSnapshot.Logs.All(entry => entry.Category != "validation") ||
            !System.IO.File.Exists(snapshotPath))
        {
            throw new InvalidOperationException("Unified runtime diagnostics snapshot was incomplete or not written.");
        }
        GD.Print("LX_RUNTIME_DIAGNOSTICS_PASS");
        ProductSmokeProbe.Performance(LX, "framework_runtime", "after", 1);
        GD.Print("LX_PRODUCT_SMOKE_PERFORMANCE_PROBE_PASS");
    }

    private static (string Id, InputContextMode Mode, string Actions, int Order)
        InputContextSignature(InputContextRecord context) =>
        (context.Id, context.Mode, string.Join('\u001f', context.Actions), context.Order);

    private sealed class ValidationWorldChunkSource(
        IReadOnlyCollection<ChunkCoordinate> coordinates,
        Func<ChunkCoordinate, Node> factory) : IWorldChunkSource
    {
        public int ChunkWidth => 32;

        public int ChunkHeight => 24;

        public IReadOnlyCollection<ChunkCoordinate> Coordinates { get; } = coordinates;

        public Node? LastCreated { get; private set; }

        public bool IsDisposed { get; private set; }

        public ValueTask<Node> InstantiateAsync(
            ChunkCoordinate coordinate,
            LifetimeScope lifetime,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = factory(coordinate);
            LastCreated = node;
            return ValueTask.FromResult(node);
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
