using Godot;
using LX.Core.Actions;
using LX.Core.Diagnostics;
using LX.Core.Flow;
using LX.Core.Lifetime;
using LX.Generated;
using LX.Pooling;
using LX.Res;
using LX.Runtime;
using LX.UI;
using PlaneFight.Nodes;

namespace PlaneFight.Showcase;

internal static class PlaneFightApiShowcase
{
    private static readonly IReadOnlyDictionary<string, string> ShowcaseNames =
        new Dictionary<string, string>
        {
            ["zh_CN"] = "框架实验室",
            ["en"] = "Framework Lab",
        };

    internal static async Task RunAsync(LXContext lx, Node parent, LifetimeScope lifetime)
    {
        var trace = new ProbeTrace();
        await VerifyFlowsAsync(trace, lifetime);
        await VerifyActionsAsync(lx, trace, lifetime);
        await VerifySchedulerAsync(lx, trace, lifetime);
        VerifyRuntimeServices(lx, trace, lifetime);
        await VerifyAssetsAndScenesAsync(lx, parent, trace, lifetime);
        await lx.UI.PlayFadeAsync(
            UIFadeMode.FadeOutIn,
            new UIFadeOptions
            {
                FadeOutDuration = TimeSpan.FromMilliseconds(10),
                HoldDuration = TimeSpan.FromMilliseconds(10),
                FadeInDuration = TimeSpan.FromMilliseconds(10),
            },
            lifetime.Token);
        if (lx.UI.IsFadeBlackoutActive)
        {
            throw new InvalidOperationException("The showcase UI fade left a blackout active.");
        }

        var snapshot = lx.Diagnostics.Snapshot();
        if (snapshot.Scheduler.PendingCount != 0 || snapshot.Actions.Active.Count != 0)
        {
            throw new InvalidOperationException(
                "The showcase retained scheduler tasks or action roots after completion.");
        }
        lx.Metrics.Increment("plane.showcase.api_smoke_passed");
        lx.Diagnostics.Log(
            DiagnosticSeverity.Information,
            "plane.showcase",
            $"API showcase completed with {trace.Entries.Count} observable steps.");
    }

    private static async Task VerifyFlowsAsync(ProbeTrace trace, LifetimeScope lifetime)
    {
        await using (var flow = new GameFlow<ProbeState, ProbeTrace>(
                         trace,
                         lifetime,
                         "PlaneFightApiFlow"))
        {
            flow.Register(ProbeState.Ready, new ProbeFlowState("flow.ready"));
            flow.Register(ProbeState.Active, new ProbeFlowState("flow.active"));
            await flow.TransitionAsync(ProbeState.Ready, lifetime.Token);
            flow.Tick(0.016);
            await flow.TransitionAsync(ProbeState.Active, lifetime.Token);
            if (flow.Current != ProbeState.Active)
            {
                throw new InvalidOperationException("GameFlow did not commit the active probe state.");
            }
        }

        var machine = new StateMachine<ProbeState, ProbeTrace>(trace);
        machine.Register(ProbeState.Ready, new ProbeMachineState("machine.ready"));
        machine.Register(ProbeState.Active, new ProbeMachineState("machine.active"));
        await machine.TransitionAsync(ProbeState.Ready, lifetime.Token);
        machine.Tick(0.016);
        await machine.TransitionAsync(ProbeState.Active, lifetime.Token);
        if (machine.Current != ProbeState.Active)
        {
            throw new InvalidOperationException("StateMachine did not commit the active probe state.");
        }
    }

    private static async Task VerifyActionsAsync(
        LXContext lx,
        ProbeTrace trace,
        LifetimeScope lifetime)
    {
        var action = LXActions.Finally(
            LXActions.Sequence(
                LXActions.Invoke(() => trace.Add("action.start"), "showcase_start"),
                LXActions.Parallel(
                    LXActions.Invoke(() => trace.Add("action.parallel_a"), "parallel_a"),
                    LXActions.Invoke(() => trace.Add("action.parallel_b"), "parallel_b")),
                LXActions.Delay(TimeSpan.FromMilliseconds(1), "showcase_delay")),
            LXActions.Invoke(() => trace.Add("action.finally"), "showcase_finally"),
            "plane_showcase_action");
        await lx.Actions.RunAsync(action, lifetime, lifetime.Token);
        if (!trace.Entries.Contains("action.finally", StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The showcase action did not run its finalizer.");
        }
    }

    private static async Task VerifySchedulerAsync(
        LXContext lx,
        ProbeTrace trace,
        LifetimeScope lifetime)
    {
        var scheduled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handle = lx.Scheduler.Schedule(
            TimeSpan.Zero,
            () =>
            {
                trace.Add("scheduler.fired");
                scheduled.TrySetResult();
            },
            lifetime);
        await scheduled.Task.WaitAsync(TimeSpan.FromSeconds(3), lifetime.Token);
    }

    private static void VerifyRuntimeServices(
        LXContext lx,
        ProbeTrace trace,
        LifetimeScope lifetime)
    {
        var received = 0;
        using (lx.Events.Subscribe<ShowcaseEvent>(_ => received++, lifetime))
        {
            lx.Events.Publish(new ShowcaseEvent("event.publish"));
        }
        if (received != 1)
        {
            throw new InvalidOperationException("EventHub did not deliver exactly one showcase event.");
        }

        var journal = lx.WorldEvents.Capture();
        var eventId = new LX.Core.World.WorldEventId("plane_showcase_probe");
        try
        {
            lx.WorldEvents.Reset(eventId);
            if (!lx.WorldEvents.TryComplete(eventId) || lx.WorldEvents.TryComplete(eventId))
            {
                throw new InvalidOperationException("WorldEventJournal did not enforce idempotent completion.");
            }
        }
        finally
        {
            lx.WorldEvents.Restore(journal);
        }

        var originalPseudoLocalization = lx.Localization.PseudoLocalizationEnabled;
        try
        {
            lx.Localization.PseudoLocalizationEnabled = false;
            var name = lx.Localization.ResolveVariant(ShowcaseNames);
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Localization did not resolve the showcase variant.");
            }
        }
        finally
        {
            lx.Localization.PseudoLocalizationEnabled = originalPseudoLocalization;
        }

        var prompt = lx.Input.Prompt(InputCatalog.NuclearBomb);
        if (string.IsNullOrWhiteSpace(prompt.Text) ||
            string.IsNullOrWhiteSpace(lx.Settings.Current.Locale))
        {
            throw new InvalidOperationException("Input prompts or settings were unavailable.");
        }

        lx.Pause.SetPaused(true);
        try
        {
            if (!lx.Pause.IsPaused || !lx.Clock.IsPaused || !lx.PhysicsClock.IsPaused)
            {
                throw new InvalidOperationException("PauseService did not synchronize both clocks.");
            }
        }
        finally
        {
            lx.Pause.SetPaused(false);
        }
        trace.Add("runtime.services");
    }

    private static async Task VerifyAssetsAndScenesAsync(
        LXContext lx,
        Node parent,
        ProbeTrace trace,
        LifetimeScope lifetime)
    {
        var preloadSet = new AssetPreloadSet<Texture2D>(
            "plane_showcase_textures",
            [
                new AssetLoadRequest<Texture2D>("background", ResCatalog.PfLevel1Background, 30),
                new AssetLoadRequest<Texture2D>("player", ResCatalog.PfPlayer, 20, ["background"]),
                new AssetLoadRequest<Texture2D>("boss", ResCatalog.PfBoss2, 10, ["player"]),
            ]);
        var plan = preloadSet.Analyze();
        if (plan.Status != AssetPlanStatus.Ready || plan.LoadOrder.Count != 3)
        {
            throw new InvalidOperationException("The showcase texture dependency plan was not ready.");
        }

        using var assets = await lx.Res.PreloadAsync(
            preloadSet,
            maxConcurrency: 2,
            progress => lx.Metrics.SetGauge(
                "plane.showcase.preload_ratio",
                progress.Total == 0 ? 1 : (double)progress.Completed / progress.Total),
            lifetime.Token);
        if (assets.Count != 3 || !assets.TryGet("player", out var player) || player is null)
        {
            throw new InvalidOperationException("The showcase texture preload did not retain all assets.");
        }

        using var scene = await lx.Scenes.PreloadAsync(
            WorldCatalog.MainWorld.Id,
            progress => lx.Metrics.SetGauge("plane.showcase.scene_ratio", progress.Ratio),
            lifetime.Token);
        if (!string.Equals(scene.ScenePath, WorldCatalog.MainWorld.ScenePath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("SceneService preloaded an unexpected world path.");
        }

        using (var instanceOwner = lifetime.CreateChild("ShowcasePackedSceneInstance"))
        {
            var instance = await PackedSceneInstance<ShowcasePulse>.CreateAsync(
                lx,
                ResCatalog.PfShowcasePulse,
                parent,
                instanceOwner,
                lifetime.Token);
            var node = instance.Node;
            if (!node.IsLXInitialized || node.GetParent() != parent)
            {
                throw new InvalidOperationException("PackedSceneInstance did not inject or attach the showcase node.");
            }
            await instance.DisposeAsync();
            if (GodotObject.IsInstanceValid(node))
            {
                throw new InvalidOperationException("PackedSceneInstance did not release its node on disposal.");
            }
        }

        using (var poolOwner = lifetime.CreateChild("ShowcasePackedScenePool"))
        {
            var pool = await PackedSceneNodePool<ShowcasePulse>.CreateAsync(
                lx,
                ResCatalog.PfShowcasePulse,
                poolOwner,
                maxRetained: 2,
                cancellationToken: lifetime.Token);
            using (var rental = pool.RentLease(
                       parent,
                       pulse => pulse.Configure(new Vector2(48, 48), Colors.Cyan)))
            {
                if (!rental.Node.IsLXInitialized || pool.RentedCount != 1)
                {
                    throw new InvalidOperationException("PackedSceneNodePool did not rent an initialized node.");
                }
            }
            if (pool.RentedCount != 0 || pool.RetainedCount != 1)
            {
                throw new InvalidOperationException("PackedSceneNodePool did not close and retain its rental.");
            }
        }
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);

        var bindingTarget = new Sprite2D();
        parent.AddChild(bindingTarget);
        try
        {
            using var bindingOwner = lifetime.CreateChild("ShowcaseAssetBinding");
            var binding = AssetBinding<Texture2D>.Create(
                lx.Res,
                bindingOwner,
                texture => bindingTarget.Texture = texture);
            if (!await binding.SetAsync(ResCatalog.PfPlayer, lifetime.Token) ||
                !binding.HasValue ||
                bindingTarget.Texture is null)
            {
                throw new InvalidOperationException("AssetBinding did not apply the showcase texture.");
            }
            binding.Clear();
            if (binding.HasValue || bindingTarget.Texture is not null)
            {
                throw new InvalidOperationException("AssetBinding did not clear its target and lease.");
            }
        }
        finally
        {
            bindingTarget.QueueFree();
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        trace.Add("assets.scenes");
    }

    private enum ProbeState
    {
        Ready,
        Active,
    }

    private readonly record struct ShowcaseEvent(string Id);

    private sealed class ProbeTrace
    {
        public List<string> Entries { get; } = [];

        public void Add(string value) => Entries.Add(value);
    }

    private sealed class ProbeFlowState(string id) : IGameFlowState<ProbeTrace>
    {
        public ValueTask EnterAsync(
            ProbeTrace context,
            LifetimeScope lifetime,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Add(id + ".enter");
            lifetime.Defer(() => context.Add(id + ".lifetime_disposed"));
            return ValueTask.CompletedTask;
        }

        public void Tick(ProbeTrace context, double deltaSeconds) => context.Add(id + ".tick");

        public ValueTask ExitAsync(ProbeTrace context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Add(id + ".exit");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProbeMachineState(string id) : IState<ProbeTrace>
    {
        public ValueTask EnterAsync(ProbeTrace context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Add(id + ".enter");
            return ValueTask.CompletedTask;
        }

        public void Tick(ProbeTrace context, double deltaSeconds) => context.Add(id + ".tick");

        public ValueTask ExitAsync(ProbeTrace context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Add(id + ".exit");
            return ValueTask.CompletedTask;
        }
    }
}
