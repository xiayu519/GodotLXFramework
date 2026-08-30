using LX.Res;
using LX.Audio;
using LX.Content;
using LX.Core.Diagnostics;
using LX.Core.Actions;
using LX.Core.Events;
using LX.Core.Lifetime;
using LX.Core.Random;
using LX.Core.Time;
using LX.Core.World;
using LX.Diagnostics;
using LX.Features;
using LX.Generated;
using LX.Input;
using LX.Localization;
using LX.Scenes;
using LX.Settings;
using LX.UI;
using LX.Validation;
using Godot;

namespace LX.Runtime;

[GlobalClass]
public partial class LXHost : Node
{
    private readonly object _shutdownGate = new();
    private LifetimeScope? _lifetime;
    private RuntimeBridgeService? _runtimeBridge;
    private Task? _bootTask;
    private TaskCompletionSource<object?>? _shutdownCompletion;
    private bool _quitRequested;

    public LXContext LX { get; private set; } = null!;

    public Task BootTask => _bootTask ?? Task.CompletedTask;

    public bool IsBooted { get; private set; }

    public string? BootError { get; private set; }

    [Export(PropertyHint.File, "*.tscn")]
    public string InitialWorldScene { get; set; } = string.Empty;

    [Export]
    public string InitialWorldId { get; set; } = string.Empty;

    [Export]
    public bool ShowFrameworkStatus { get; set; } = true;

    public override void _Ready()
    {
        _lifetime = new LifetimeScope("LXFramework");
        var metrics = new MetricRegistry();
        var events = _lifetime.Own(new EventHub(
            exception =>
            {
                metrics.Increment("events.handler_failures");
                GD.PushError($"LXFramework event handler failed: {exception}");
            },
            isolateHandlerExceptions: true));
        var clock = new GameClock();
        var scheduler = _lifetime.Own(new GameScheduler(clock));
        var physicsClock = new GameClock();
        var physicsScheduler = _lifetime.Own(new GameScheduler(physicsClock));
        var actions = _lifetime.Own(new ActionRunner(_lifetime));
        var random = new DeterministicRng(0x4C584652414D4557UL);
        var input = _lifetime.Own(new InputRouter(events));
        var localization = new LocalizationService(events);
        var content = new ContentService();
        var worldEvents = new WorldEventJournal();
        var assets = _lifetime.Own(new AssetRegistry(this, metrics));
        var features = _lifetime.Own(new FeatureService(assets, _lifetime, metrics, () => LX));
        features.RegisterRange(FeatureCatalog.All);
        var scenes = _lifetime.Own(new SceneService(this, assets, _lifetime, events, metrics, () => LX));
        scenes.RegisterRange(WorldCatalog.All);
        var audio = _lifetime.Own(new AudioService(this, assets, metrics));
        var ui = _lifetime.Own(new UIService(this, assets, _lifetime, metrics, () => LX));
        ui.RegisterRange(UICatalog.All);
        var settings = new SettingsService(this, events, metrics, localization, input);
        var diagnostics = new DiagnosticsService(
            metrics,
            _lifetime,
            events,
            scheduler,
            physicsScheduler,
            actions,
            assets,
            ui,
            features,
            scenes,
            audio,
            input,
            localization,
            settings);
        _runtimeBridge = _lifetime.Own(new RuntimeBridgeService(diagnostics));
        var pause = new PauseService(this, clock, physicsClock, events, metrics);

        LX = new LXContext(
            _lifetime,
            events,
            clock,
            scheduler,
            physicsClock,
            physicsScheduler,
            actions,
            pause,
            random,
            metrics,
            input,
            localization,
            content,
            worldEvents,
            settings,
            assets,
            features,
            scenes,
            audio,
            ui,
            diagnostics);

        events.Subscribe<GameActionTriggered>(HandleGlobalAction, _lifetime);

        _bootTask = BootFrameworkAsync(_lifetime.Token);
        diagnostics.Log(
            DiagnosticSeverity.Information,
            "runtime",
            $"LXFramework runtime ready. Initial scene: " +
            $"{(string.IsNullOrWhiteSpace(InitialWorldScene) ? "none" : InitialWorldScene)}");
    }

    public override void _Process(double delta)
    {
        if (_lifetime is null || _lifetime.IsDisposed)
        {
            return;
        }

        var frame = LX.Clock.Advance(delta);
        LX.Scheduler.Tick();
        LX.Metrics.SetGauge("runtime.frame", frame.FrameIndex);
        LX.Metrics.SetGauge("runtime.delta_ms", frame.DeltaSeconds * 1000.0);
        LX.Metrics.SetGauge("lifetime.root_owned", LX.Lifetime.OwnedCount);
        _runtimeBridge?.Pump();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_lifetime is null || _lifetime.IsDisposed)
        {
            return;
        }

        var frame = LX.PhysicsClock.Advance(delta);
        LX.PhysicsScheduler.Tick();
        LX.Metrics.SetGauge("runtime.physics_frame", frame.FrameIndex);
        LX.Metrics.SetGauge("runtime.physics_delta_ms", frame.DeltaSeconds * 1000.0);
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (_lifetime is null || _lifetime.IsDisposed)
        {
            return;
        }

        LX.Input.Handle(inputEvent);
    }

    public override void _ExitTree()
    {
        if (_lifetime is null)
        {
            return;
        }

        _lifetime.DisposeEmergency(exception =>
            GD.PushError($"LXFramework emergency shutdown reported a cleanup error: {exception}"));
        _lifetime = null;
        _runtimeBridge = null;
    }

    public ValueTask ShutdownAsync(bool quit = true)
    {
        TaskCompletionSource<object?>? starter = null;
        TaskCompletionSource<object?> completion;
        LifetimeScope? lifetime = null;
        lock (_shutdownGate)
        {
            _quitRequested |= quit;
            if (_shutdownCompletion is null)
            {
                starter = new TaskCompletionSource<object?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _shutdownCompletion = starter;
                lifetime = _lifetime;
                _lifetime = null;
            }
            completion = _shutdownCompletion!;
        }

        if (starter is not null)
        {
            _ = CompleteShutdownAsync(lifetime, starter);
        }
        return new ValueTask(completion.Task);
    }

    private async Task CompleteShutdownAsync(
        LifetimeScope? lifetime,
        TaskCompletionSource<object?> completion)
    {
        Exception? shutdownError = null;
        try
        {
            if (lifetime is not null)
            {
                await lifetime.DisposeAsync();
            }
        }
        catch (Exception exception)
        {
            shutdownError = exception;
        }
        finally
        {
            bool quit;
            lock (_shutdownGate)
            {
                quit = _quitRequested;
            }
            try
            {
                if (quit && IsInsideTree())
                {
                    GetTree().Quit();
                }
            }
            catch (Exception exception)
            {
                shutdownError = shutdownError is null
                    ? exception
                    : new AggregateException(shutdownError, exception);
            }

            if (shutdownError is null)
            {
                completion.TrySetResult(null);
            }
            else
            {
                completion.TrySetException(shutdownError);
            }
        }
    }

    private async Task BootFrameworkAsync(CancellationToken cancellationToken)
    {
        try
        {
            UIHandle? frameworkStatus = null;
            var userArguments = OS.GetCmdlineUserArgs();
            var visualMode = GetArgument(userArguments, "--lx-visual-mode=");
            if (visualMode is not null)
            {
                var actualPath = RequireArgument(userArguments, "--lx-visual-actual=");
                var visualTarget = RequireArgument(userArguments, "--lx-visual-target=");
                var visualScene = RequireArgument(userArguments, "--lx-visual-scene=");
                var visualWidth = int.Parse(RequireArgument(userArguments, "--lx-visual-width="),
                    System.Globalization.CultureInfo.InvariantCulture);
                var visualHeight = int.Parse(RequireArgument(userArguments, "--lx-visual-height="),
                    System.Globalization.CultureInfo.InvariantCulture);
                var baselinePath = GetArgument(userArguments, "--lx-visual-baseline=");
                var diffPath = GetArgument(userArguments, "--lx-visual-diff=");
                var reportPath = RequireArgument(userArguments, "--lx-visual-report=");
                var report = await new VisualCaptureRunner(this, LX).RunAsync(
                    visualMode,
                    visualTarget,
                    visualScene,
                    new Vector2I(visualWidth, visualHeight),
                    actualPath,
                    baselinePath,
                    diffPath,
                    cancellationToken);
                VisualCaptureRunner.WriteReport(reportPath, report);
                GD.Print(report.Success ? "LX_VISUAL_PASS" : "LX_VISUAL_MISMATCH");
                await ShutdownAsync(quit: false);
                GetTree().Quit(report.Success || visualMode == "capture" ? 0 : 1);
                return;
            }

            var isFrameworkSmoke =
                userArguments.Contains("--lx-framework-smoke", StringComparer.Ordinal) ||
                userArguments.Contains("--lx-export-smoke", StringComparer.Ordinal);
            var settings = await LX.Settings.InitializeAsync(cancellationToken);
            if (settings.IsFailure)
            {
                GD.PushWarning($"LXFramework settings fallback was used: {settings.Error}");
            }

            var initialWorldId = string.IsNullOrWhiteSpace(InitialWorldId)
                ? GameCatalog.InitialWorldId
                : InitialWorldId;
            if (!string.IsNullOrWhiteSpace(initialWorldId))
            {
                await LX.Scenes.ChangeAsync(new WorldId(initialWorldId), cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(InitialWorldScene))
            {
                await LX.Scenes.ChangeAsync(InitialWorldScene, cancellationToken);
            }

            if (ShowFrameworkStatus || isFrameworkSmoke)
            {
                frameworkStatus = await LX.UI.OpenAsync(
                    UICatalog.FrameworkStatus.Id,
                    parentLifetime: LX.Lifetime,
                    cancellationToken: cancellationToken);
            }

            IsBooted = true;
            LX.Events.Publish(new FrameworkBootCompleted(true, null));

            if (isFrameworkSmoke)
            {
                await new FrameworkSmokeRunner(this, LX).RunAsync(frameworkStatus, cancellationToken);
                await (_runtimeBridge ?? throw new InvalidOperationException(
                    "Runtime bridge was not composed.")).RunSelfTestAsync(this, cancellationToken);
                GD.Print("LX_RUNTIME_BRIDGE_PASS");
                GD.Print("LX_FRAMEWORK_SMOKE_PASS");
                var firstShutdown = ShutdownAsync(quit: false).AsTask();
                var secondShutdown = ShutdownAsync(quit: false).AsTask();
                if (!ReferenceEquals(firstShutdown, secondShutdown))
                {
                    throw new InvalidOperationException(
                        "Concurrent LXHost shutdown callers did not receive the same completion task.");
                }
                await firstShutdown;
                GD.Print("LX_ASYNC_SHUTDOWN_PASS");
                GetTree().Quit();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            IsBooted = false;
            BootError = "LXFramework bootstrap was cancelled.";
        }
        catch (Exception exception)
        {
            IsBooted = false;
            BootError = exception.ToString();
            LX.Events.Publish(new FrameworkBootCompleted(false, BootError));
            LX.Diagnostics.Log(
                DiagnosticSeverity.Critical,
                "runtime.bootstrap",
                "LXFramework bootstrap failed.",
                exception);
        }
    }

    private void HandleGlobalAction(GameActionTriggered input)
    {
        if (input.Pressed && input.Action == LXInputActions.Cancel)
        {
            _ = RequestBackSafelyAsync();
        }
    }

    private async Task RequestBackSafelyAsync()
    {
        try
        {
            await LX.UI.RequestBackAsync();
        }
        catch (Exception exception)
        {
            LX.Diagnostics.Log(
                DiagnosticSeverity.Error,
                "ui.navigation",
                "LXFramework UI back navigation failed.",
                exception);
        }
    }

    private static string? GetArgument(IReadOnlyList<string> arguments, string prefix) =>
        arguments.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];

    private static string RequireArgument(IReadOnlyList<string> arguments, string prefix) =>
        GetArgument(arguments, prefix) is { Length: > 0 } value
            ? value
            : throw new ArgumentException($"Missing required runtime argument '{prefix}<value>'.");
}

public readonly record struct FrameworkBootCompleted(bool Success, string? Error);
