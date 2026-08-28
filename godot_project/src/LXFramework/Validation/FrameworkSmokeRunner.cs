using LX.Audio;
using LX.Content;
using LX.Core.Audio;
using LX.Core.Diagnostics;
using LX.Core.Lifetime;
using LX.Core.World;
using LX.Diagnostics;
using LX.Features;
using LX.Generated;
using LX.Input;
using LX.Localization;
using LX.Pooling;
using LX.Res;
using LX.Runtime;
using LX.Scenes;
using LX.UI;
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
        var rootProbe = new ContextProbeNode { Name = "RootProbe" };
        var childProbe = new ContextProbeNode { Name = "ChildProbe" };
        rootProbe.AddChild(childProbe);
        var initialized = LXContextInjector.InitializeTree(rootProbe, LX, probeLifetime);
        if (initialized != 2 ||
            !rootProbe.InitializationHookCalled ||
            !childProbe.InitializationHookCalled ||
            !rootProbe.ChildrenWereInitializedFirst)
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

        try
        {
            LX.Input.Register(
                new StringName("ui_cancel"),
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

        var usedJsonOptions = new System.Text.Json.JsonSerializerOptions();
        _ = System.Text.Json.JsonSerializer.Serialize(new { value = 1 }, usedJsonOptions);
        _ = new ContentService(usedJsonOptions);
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

        using (var inputContext = LX.Input.PushContext(new InputContextDescriptor(
                   "validation.menu",
                   new HashSet<InputActionId> { InputCatalog.Confirm, InputCatalog.Cancel },
                   InputContextMode.Exclusive)))
        {
            var snapshot = LX.Input.Snapshot();
            var prompt = LX.Input.Prompt(InputCatalog.Confirm);
            if (snapshot.Contexts.Count != 1 ||
                snapshot.Contexts[0].Id != "validation.menu" ||
                string.IsNullOrWhiteSpace(prompt.Text))
            {
                throw new InvalidOperationException("Input context or human-readable prompt was not observable.");
            }
        }
        if (LX.Input.Snapshot().Contexts.Count != 0)
        {
            throw new InvalidOperationException("Disposed input context remained on the routing stack.");
        }
        GD.Print("LX_INPUT_CONTEXT_PASS");

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
        if (LX.Features.Snapshot().Count != 0)
        {
            throw new InvalidOperationException("Feature despawn did not release its active record.");
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
        var pooled = pool.RentLease(host);
        if (!pooled.Node.IsLXInitialized || pool.RentedCount != 1)
        {
            throw new InvalidOperationException("Packed-scene pool did not inject context or track its rented node.");
        }
        pooled.Dispose();
        if (pool.RentedCount != 0 || pool.RetainedCount != 1)
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
        await poolLifetime.DisposeAsync();
        GD.Print("LX_PACKED_SCENE_POOL_PASS");

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
        if (!result.HasValue || result.Value != "accepted" || resultScreen.ExitTransitions != 1)
        {
            throw new InvalidOperationException("UI strong-type result or exit transition did not complete.");
        }
        GD.Print("LX_UI_RESULT_TRANSITION_PASS");

        var metrics = LX.Metrics.Snapshot();
        if (!metrics.Counters.TryGetValue("validation.ui_context_injected", out var injections) ||
            injections < 1)
        {
            throw new InvalidOperationException("UI context injection was not observed during smoke validation.");
        }
        GD.Print("LX_UI_LIFECYCLE_PASS");

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
    }

    private sealed class ValidationWorldChunkSource(
        IReadOnlyCollection<ChunkCoordinate> coordinates,
        Func<ChunkCoordinate, Node> factory) : IWorldChunkSource
    {
        public int ChunkWidth => 32;

        public int ChunkHeight => 24;

        public IReadOnlyCollection<ChunkCoordinate> Coordinates { get; } = coordinates;

        public Node? LastCreated { get; private set; }

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

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
