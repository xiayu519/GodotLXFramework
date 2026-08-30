using Godot;
using GameData.plane_fight;
using LX.Core.Diagnostics;
using LX.Generated;
using LX.Res;
using LX.Runtime;
using PlaneFight.Features.LevelOneBattle;
using PlaneFight.Showcase;
using PlaneFight.UI;

namespace PlaneFight;

public partial class GameRoot : LXNode
{
    // Godot's C# binding can retain a few non-node wrapper objects until process teardown.
    // Nodes, resources, framework handles, leases and memory remain strict and stable below.
    private const int InteropObjectDriftAllowance = 16;

    private GameData.Tables _tables = null!;
    private LevelConfig _level = null!;
    private int _nuclearBombEvents;

    protected override void OnLXInitialized()
    {
        var userArguments = OS.GetCmdlineUserArgs();
        if (userArguments.Contains("--lx-framework-smoke", StringComparer.Ordinal) ||
            userArguments.Contains("--lx-export-smoke", StringComparer.Ordinal))
        {
            return;
        }
        _tables = LX.Content.LoadLubanTables(loader => new GameData.Tables(loader));
        _level = _tables.TbLevel.Get("level_1");
        LX.Events.Subscribe<NuclearBombDetonated>(OnNuclearBombDetonated, Lifetime);
        LX.Events.Subscribe<BattleFinished>(OnBattleFinished, Lifetime);
        LX.Diagnostics.Log(
            DiagnosticSeverity.Information,
            "plane.boot",
            $"PlaneFight initialized with locale '{LX.Settings.Current.Locale}'.");
        if (userArguments.Contains("--plane-fight-api-smoke", StringComparer.Ordinal))
        {
            _ = RunApiShowcaseSmokeAsync();
            return;
        }
        if (userArguments.Contains("--plane-fight-smoke", StringComparer.Ordinal))
        {
            _ = RunBattleSmokeAsync();
            return;
        }
        if (userArguments.Contains("--plane-fight-flow-smoke", StringComparer.Ordinal))
        {
            _ = RunGameLoopAsync(runFlowSmoke: true);
            return;
        }
        _ = RunGameLoopAsync(runFlowSmoke: false);
    }

    private async Task RunGameLoopAsync(bool runFlowSmoke)
    {
        try
        {
            if (runFlowSmoke)
            {
                GD.Print("PLANE_FIGHT_FLOW_SMOKE_STAGE start_exit");
                var exitProbe = await ShowStartScreenAsync(StartChoice.Exit);
                if (exitProbe != StartChoice.Exit)
                {
                    throw new InvalidOperationException("Start screen exit action returned an unexpected result.");
                }
                GD.Print("PLANE_FIGHT_FLOW_SMOKE_STAGE first_start");
            }

            var showStartScreen = true;
            var battleNumber = 0;
            var quitRequested = false;
            var flowSmokePassed = false;
            var baselineAssetLeases = CaptureAssetLeases();
            RuntimeClosureSample? stableRuntime = null;
            while (!Lifetime.Token.IsCancellationRequested)
            {
                var smokeOutcome = battleNumber % 2 == 0
                    ? BattleOutcomeKind.Victory
                    : BattleOutcomeKind.Defeat;
                var smokeResult = battleNumber < 3
                    ? ResultChoice.Restart
                    : ResultChoice.Exit;
                var iteration = await RunBattleIterationAsync(
                    showStartScreen,
                    runFlowSmoke,
                    battleNumber,
                    smokeOutcome,
                    smokeResult);
                if (!iteration.Started)
                {
                    quitRequested = true;
                    break;
                }

                if (runFlowSmoke)
                {
                    var currentRuntime = await AssertReleasedRuntimeAsync(
                        $"cycle_{battleNumber + 1}",
                        baselineAssetLeases,
                        stableRuntime?.AssetLeases);
                    if (stableRuntime is null || battleNumber < 2)
                    {
                        stableRuntime = currentRuntime;
                    }
                    else
                    {
                        AssertStableRuntime(stableRuntime, currentRuntime);
                    }
                }

                if (iteration.ResultChoice != ResultChoice.Restart)
                {
                    if (runFlowSmoke)
                    {
                        if (battleNumber != 3 ||
                            iteration.Outcome?.Kind != BattleOutcomeKind.Defeat)
                        {
                            throw new InvalidOperationException(
                                "Product flow smoke did not complete the expected repeated restart and defeat exit flow.");
                        }
                        flowSmokePassed = true;
                    }
                    quitRequested = true;
                    break;
                }

                showStartScreen = false;
                battleNumber++;
                if (runFlowSmoke)
                {
                    GD.Print("PLANE_FIGHT_FLOW_SMOKE_STAGE direct_restart");
                }
                LX.Audio.StopMusic();
            }

            if (flowSmokePassed)
            {
                GD.Print("PLANE_FIGHT_FLOW_SMOKE_PASS");
            }
            if (quitRequested)
            {
                await QuitGameAsync();
            }
        }
        catch (OperationCanceledException) when (Lifetime.Token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            GD.PushError($"PlaneFight game loop failed: {exception}");
            if (runFlowSmoke)
            {
                GetTree().Quit(1);
            }
        }
    }

    private async Task<BattleIterationResult> RunBattleIterationAsync(
        bool showStartScreen,
        bool runFlowSmoke,
        int battleNumber,
        BattleOutcomeKind smokeOutcome,
        ResultChoice smokeResult)
    {
        using var battleAssets = await PreloadBattleAssetsAsync();
        await using var battleHandle = await LX.Features.SpawnAsync(
            FeatureCatalog.LevelOneBattle.Id,
            this,
            Lifetime,
            Lifetime.Token);
        var battle = battleHandle.Node as LevelOneBattleFeature ??
            throw new InvalidOperationException(
                $"Feature '{FeatureCatalog.LevelOneBattle.Id}' did not create {nameof(LevelOneBattleFeature)}.");
        battle.Configure(_level);

        await using var hudHandle = await LX.UI.OpenAsync(
            UICatalog.BattleHud.Id,
            battle.HudModel,
            Lifetime,
            Lifetime.Token);

        var startChoice = showStartScreen
            ? await ShowStartScreenAsync(runFlowSmoke ? StartChoice.Start : null)
            : StartChoice.Start;
        if (startChoice != StartChoice.Start)
        {
            return new BattleIterationResult(false, null, ResultChoice.Exit);
        }

        battle.StartBattle();
        if (runFlowSmoke)
        {
            GD.Print($"PLANE_FIGHT_FLOW_SMOKE_STAGE battle_{battleNumber + 1}");
            battle.CompleteForSmoke(smokeOutcome);
        }
        var outcome = await battle.Completion.WaitAsync(Lifetime.Token);
        if (battle.ActiveProjectileCount != 0 ||
            battle.ActivePickupCount != 0 ||
            battle.ActiveEnemyCount != 0 ||
            battle.ActiveTransientEffectCount != 0 ||
            battle.RentedPooledNodeCount != 0)
        {
            throw new InvalidOperationException(
                "Completed battle retained gameplay nodes. " +
                $"projectiles={battle.ActiveProjectileCount}, " +
                $"pickups={battle.ActivePickupCount}, " +
                $"enemies={battle.ActiveEnemyCount}, " +
                $"effects={battle.ActiveTransientEffectCount}, " +
                $"pooledRented={battle.RentedPooledNodeCount}.");
        }
        if (runFlowSmoke)
        {
            GD.Print(
                $"PLANE_FIGHT_FLOW_SMOKE_STAGE result_{outcome.Kind} " +
                $"pool_retained_{battle.RetainedPooledNodeCount}");
        }
        var resultChoice = await ShowResultScreenAsync(
            outcome,
            runFlowSmoke ? smokeResult : null);
        return new BattleIterationResult(true, outcome, resultChoice);
    }

    private async Task<RuntimeClosureSample> AssertReleasedRuntimeAsync(
        string stage,
        IReadOnlyDictionary<string, int> baselineAssetLeases,
        IReadOnlyDictionary<string, int>? stableAssetLeases = null)
    {
        for (var frame = 0; frame < 3; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var snapshot = LX.Diagnostics.Snapshot();
        var leasedAssets = snapshot.Assets
            .Where(asset => asset.LeaseCount != 0)
            .ToDictionary(asset => asset.Path, asset => asset.LeaseCount, StringComparer.Ordinal);
        var assetLeasesClosed = stableAssetLeases is not null
            ? stableAssetLeases.Count == leasedAssets.Count &&
              stableAssetLeases.All(expected =>
                  leasedAssets.TryGetValue(expected.Key, out var actual) &&
                  actual == expected.Value)
            : baselineAssetLeases.All(expected =>
                  leasedAssets.TryGetValue(expected.Key, out var actual) &&
                  actual == expected.Value) &&
              leasedAssets.All(asset =>
                  baselineAssetLeases.ContainsKey(asset.Key) ||
                  asset.Key.StartsWith("res://scene/ui/", StringComparison.Ordinal));
        var leasedAssetDetails = string.Join(
            ", ",
            leasedAssets.Select(asset => $"{asset.Key}:{asset.Value}"));
        var leakedProductNode = FindNode<LevelOneBattleFeature>(GetTree().Root) is not null ||
                                HasActiveScreen<BattleHudScreen>() ||
                                HasActiveScreen<StartScreen>() ||
                                HasActiveScreen<ResultScreen>();
        if (snapshot.UI.Count != 0 ||
            snapshot.Features.Count != 0 ||
            snapshot.Audio.MusicPlaying ||
            snapshot.Audio.ActiveSfx != 0 ||
            !assetLeasesClosed ||
            leakedProductNode)
        {
            throw new InvalidOperationException(
                $"Product flow did not close runtime ownership after '{stage}': " +
                $"ui={snapshot.UI.Count}, features={snapshot.Features.Count}, " +
                $"music={snapshot.Audio.MusicPlaying}, sfx={snapshot.Audio.ActiveSfx}, " +
                $"assetLeases={leasedAssets.Count} [{leasedAssetDetails}], " +
                $"productNode={leakedProductNode}.");
        }

        var objectCount = Performance.GetMonitor(Performance.Monitor.ObjectCount);
        var resourceCount = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
        var nodeCount = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        var staticMemory = Performance.GetMonitor(Performance.Monitor.MemoryStatic);
        GD.Print(
            $"PLANE_FIGHT_RESOURCE_CLOSED {stage} assets={snapshot.Assets.Count} " +
            $"objects={objectCount} resources={resourceCount} nodes={nodeCount} memory={staticMemory}");
        return new RuntimeClosureSample(
            snapshot.Assets.Select(asset => asset.Path).ToHashSet(StringComparer.Ordinal),
            leasedAssets,
            objectCount,
            resourceCount,
            nodeCount,
            staticMemory);
    }

    private bool HasActiveScreen<TScreen>() where TScreen : Control
    {
        var screen = FindNode<TScreen>(GetTree().Root);
        return screen is not null &&
               (screen.Visible || screen.ProcessMode != Node.ProcessModeEnum.Disabled);
    }

    private static void AssertStableRuntime(
        RuntimeClosureSample expected,
        RuntimeClosureSample actual)
    {
        if (!expected.AssetPaths.SetEquals(actual.AssetPaths) ||
            expected.AssetLeases.Count != actual.AssetLeases.Count ||
            expected.AssetLeases.Any(asset =>
                !actual.AssetLeases.TryGetValue(asset.Key, out var count) || count != asset.Value) ||
            expected.ResourceCount != actual.ResourceCount ||
            expected.NodeCount != actual.NodeCount ||
            actual.ObjectCount > expected.ObjectCount + InteropObjectDriftAllowance ||
            actual.StaticMemory > expected.StaticMemory + 1024 * 1024)
        {
            throw new InvalidOperationException(
                "Product flow runtime ownership grew after a restart cycle. " +
                $"assets={expected.AssetPaths.Count}/{actual.AssetPaths.Count}, " +
                $"leases={expected.AssetLeases.Count}/{actual.AssetLeases.Count}, " +
                $"objects={expected.ObjectCount}/{actual.ObjectCount}, " +
                $"resources={expected.ResourceCount}/{actual.ResourceCount}, " +
                $"nodes={expected.NodeCount}/{actual.NodeCount}, " +
                $"memory={expected.StaticMemory}/{actual.StaticMemory}.");
        }
    }

    private Dictionary<string, int> CaptureAssetLeases() =>
        LX.Diagnostics.Snapshot().Assets
            .Where(asset => asset.LeaseCount != 0)
            .ToDictionary(asset => asset.Path, asset => asset.LeaseCount, StringComparer.Ordinal);

    private async Task<StartChoice> ShowStartScreenAsync(StartChoice? automatedChoice = null)
    {
        await using var handle = await LX.UI.OpenAsync(
            UICatalog.Start.Id,
            parentLifetime: Lifetime,
            cancellationToken: Lifetime.Token);
        if (automatedChoice.HasValue)
        {
            FindActiveNode<StartScreen>().ChooseForSmoke(automatedChoice.Value);
        }
        var result = await handle.WaitForResultAsync<StartChoice>(Lifetime.Token);
        return result.HasValue ? result.Value : StartChoice.Exit;
    }

    private async Task<ResultChoice> ShowResultScreenAsync(
        BattleOutcome outcome,
        ResultChoice? automatedChoice = null)
    {
        await using var handle = await LX.UI.OpenAsync(
            UICatalog.Result.Id,
            new ResultScreenPayload(outcome),
            Lifetime,
            Lifetime.Token);
        if (automatedChoice.HasValue)
        {
            var screen = FindActiveNode<ResultScreen>();
            if (screen.DisplayedOutcome != outcome.Kind)
            {
                throw new InvalidOperationException(
                    $"Result screen displayed '{screen.DisplayedOutcome}' for '{outcome.Kind}'.");
            }
            screen.ChooseForSmoke(automatedChoice.Value);
        }
        var result = await handle.WaitForResultAsync<ResultChoice>(Lifetime.Token);
        return result.HasValue ? result.Value : ResultChoice.Exit;
    }

    private TNode FindActiveNode<TNode>() where TNode : Node
    {
        return FindNode<TNode>(GetTree().Root) ??
               throw new InvalidOperationException($"Active node '{typeof(TNode).Name}' was not found.");
    }

    private static TNode? FindNode<TNode>(Node node) where TNode : Node
    {
        if (node is TNode typed)
        {
            return typed;
        }
        foreach (var child in node.GetChildren())
        {
            var found = FindNode<TNode>(child);
            if (found is not null)
            {
                return found;
            }
        }
        return null;
    }

    private async Task QuitGameAsync()
    {
        for (var frame = 0; frame < 3; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        await ShutdownHostAsync();
    }

    private async Task RunApiShowcaseSmokeAsync()
    {
        try
        {
            await PlaneFightApiShowcase.RunAsync(LX, this, Lifetime);
            GD.Print("PLANE_FIGHT_API_SHOWCASE_PASS");
            await ShutdownHostAsync();
        }
        catch (Exception exception)
        {
            GD.PushError($"PlaneFight API showcase failed: {exception}");
            GetTree().Quit(1);
        }
    }

    private async ValueTask<AssetBatchLease<Texture2D>> PreloadBattleAssetsAsync()
    {
        var preloadSet = new AssetPreloadSet<Texture2D>(
            "plane_battle_core",
            [
                new AssetLoadRequest<Texture2D>("background", ResCatalog.PfLevel1Background, 30),
                new AssetLoadRequest<Texture2D>("player", ResCatalog.PfPlayer, 20, ["background"]),
                new AssetLoadRequest<Texture2D>("boss", ResCatalog.PfBoss2, 10, ["player"]),
            ]);
        var plan = preloadSet.Analyze();
        if (plan.Status != AssetPlanStatus.Ready)
        {
            throw new InvalidOperationException(
                $"PlaneFight battle preload plan is '{plan.Status}'.");
        }
        return await LX.Res.PreloadAsync(
            preloadSet,
            maxConcurrency: 2,
            progress => LX.Metrics.SetGauge(
                "plane.assets.preload_ratio",
                progress.Total == 0 ? 1 : (double)progress.Completed / progress.Total),
            Lifetime.Token);
    }

    private void OnNuclearBombDetonated(NuclearBombDetonated message)
    {
        _nuclearBombEvents++;
        LX.Diagnostics.Log(
            DiagnosticSeverity.Information,
            "plane.nuclear",
            $"Nuclear bomb hit {message.TargetsHit} target(s) for base damage {message.BaseDamage:0.##}; " +
            $"inventory={message.ConsumedInventory}.");
    }

    private void OnBattleFinished(BattleFinished message)
    {
        LX.Diagnostics.Log(
            DiagnosticSeverity.Information,
            "plane.battle",
            $"Battle closed in {message.FinalState}: score={message.Score}, " +
            $"gold={message.Gold}, medals={message.Medals}.");
    }

    private async Task RunBattleSmokeAsync()
    {
        var baselineAssetLeases = CaptureAssetLeases();
        try
        {
            using (var battleAssets = await PreloadBattleAssetsAsync())
            {
                await using var battleHandle = await LX.Features.SpawnAsync(
                    FeatureCatalog.LevelOneBattle.Id,
                    this,
                    Lifetime,
                    Lifetime.Token);
                var battle = (LevelOneBattleFeature)battleHandle.Node;
                battle.Configure(_level);
                var nuclearEventsBeforeProbe = _nuclearBombEvents;
                battle.VerifyNuclearBombContractForSmoke();
                if (_nuclearBombEvents != nuclearEventsBeforeProbe + 1)
                {
                    throw new InvalidOperationException(
                        "The nuclear bomb contract did not publish exactly one gameplay event.");
                }
                battle.StartBattle();

                for (var frame = 0; frame < 180; frame++)
                {
                    if (frame == 30)
                    {
                        battle.HudModel.UseMissile?.Invoke();
                        battle.HudModel.UseIceMissile?.Invoke();
                        battle.HudModel.UseShield?.Invoke();
                    }
                    if (frame == 90)
                    {
                        battle.HudModel.UseNuclearBomb?.Invoke();
                    }
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                battle.CompleteForSmoke(BattleOutcomeKind.Victory);
                var smokeOutcome = await battle.Completion.WaitAsync(Lifetime.Token);

                if (smokeOutcome.Kind != BattleOutcomeKind.Victory)
                {
                    throw new InvalidOperationException(
                        "PlaneFight smoke did not complete the deterministic victory flow. " +
                        $"score={battle.HudModel.Score}, " +
                        $"levelScore={battle.HudModel.LevelScore}, " +
                        $"bossVisible={battle.HudModel.BossVisible}, " +
                        $"bossHp={battle.HudModel.BossHp}.");
                }
                if (battle.ActiveProjectileCount != 0 ||
                    battle.ActivePickupCount != 0 ||
                    battle.ActiveEnemyCount != 0 ||
                    battle.ActiveTransientEffectCount != 0 ||
                    battle.RentedPooledNodeCount != 0 ||
                    battle.RetainedPooledNodeCount == 0)
                {
                    throw new InvalidOperationException(
                        "PlaneFight victory retained transient battle objects. " +
                        $"projectiles={battle.ActiveProjectileCount}, " +
                        $"pickups={battle.ActivePickupCount}, " +
                        $"enemies={battle.ActiveEnemyCount}, " +
                        $"effects={battle.ActiveTransientEffectCount}, " +
                        $"pooledRented={battle.RentedPooledNodeCount}, " +
                        $"pooledRetained={battle.RetainedPooledNodeCount}.");
                }
                GD.Print($"PLANE_FIGHT_POOL_CLOSED retained={battle.RetainedPooledNodeCount}");

                await LX.Audio.StopMusicAndDrainAsync(Lifetime.Token);
            }
            for (var frame = 0; frame < 45; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            _ = await AssertReleasedRuntimeAsync("level_one", baselineAssetLeases);

            GD.Print("PLANE_FIGHT_LEVEL_ONE_RUNTIME_PASS");
            await ShutdownHostAsync();
        }
        catch (Exception exception)
        {
            GD.PushError($"PlaneFight smoke failed: {exception}");
            GetTree().Quit(1);
        }
    }

    private async Task ShutdownHostAsync()
    {
        var host = GetTree().Root.GetChildren().OfType<LXHost>().FirstOrDefault();
        if (host is not null)
        {
            var tree = host.GetTree();
            await host.ShutdownAsync(quit: false);
            for (var frame = 0; frame < 3; frame++)
            {
                await host.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
            tree.Quit();
            return;
        }

        await ShutdownFallbackAsync();
    }

    private async Task ShutdownFallbackAsync()
    {
        await LX.Audio.StopMusicAndDrainAsync();
        GetTree().Quit();
    }

    private readonly record struct BattleIterationResult(
        bool Started,
        BattleOutcome? Outcome,
        ResultChoice ResultChoice);

    private sealed record RuntimeClosureSample(
        HashSet<string> AssetPaths,
        Dictionary<string, int> AssetLeases,
        double ObjectCount,
        double ResourceCount,
        double NodeCount,
        double StaticMemory);
}
