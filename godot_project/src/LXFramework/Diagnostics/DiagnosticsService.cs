using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using LX.Res;
using LX.Audio;
using LX.Core.Diagnostics;
using LX.Core.Actions;
using LX.Core.Events;
using LX.Core.Lifetime;
using LX.Core.Time;
using LX.Features;
using LX.Input;
using LX.Localization;
using LX.Scenes;
using LX.Settings;
using LX.UI;
using Godot;

namespace LX.Diagnostics;

public sealed class DiagnosticsService
{
    /// <summary>运行时快照格式名称。</summary>
    public const string SnapshotSchema = "lx.runtime-snapshot";

    /// <summary>运行时快照格式版本。</summary>
    public const int SnapshotSchemaVersion = 2;

    internal static IReadOnlyList<string> AvailableSections { get; } =
    [
        "all", "runtime", "events", "scheduler", "actions", "metrics", "resources",
        "ui", "features", "audio", "input", "localization", "settings", "logs",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly MetricRegistry _metrics;
    private readonly LifetimeScope _lifetime;
    private readonly EventHub _events;
    private readonly GameScheduler _scheduler;
    private readonly GameScheduler _physicsScheduler;
    private readonly ActionRunner _actions;
    private readonly AssetRegistry _assets;
    private readonly UIService _ui;
    private readonly FeatureService _features;
    private readonly SceneService _scenes;
    private readonly AudioService _audio;
    private readonly InputRouter _input;
    private readonly LocalizationService _localization;
    private readonly SettingsService _settings;
    private readonly DiagnosticLog _log;
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;

    public DiagnosticsService(
        MetricRegistry metrics,
        LifetimeScope lifetime,
        EventHub events,
        GameScheduler scheduler,
        GameScheduler physicsScheduler,
        ActionRunner actions,
        AssetRegistry assets,
        UIService ui,
        FeatureService features,
        SceneService scenes,
        AudioService audio,
        InputRouter input,
        LocalizationService localization,
        SettingsService settings,
        int logCapacity = 256)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _physicsScheduler = physicsScheduler ?? throw new ArgumentNullException(nameof(physicsScheduler));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _features = features ?? throw new ArgumentNullException(nameof(features));
        _scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _log = new DiagnosticLog(logCapacity);
    }

    /// <summary>统一写入结构化诊断日志，并同步到 Godot 控制台。</summary>
    public DiagnosticEntry Log(
        DiagnosticSeverity severity,
        string category,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, string>? fields = null)
    {
        var entry = _log.Write(severity, category, message, exception, fields);
        var consoleMessage = $"[{entry.Category}] {entry.Message}";
        switch (severity)
        {
            case DiagnosticSeverity.Warning:
                GD.PushWarning(consoleMessage);
                break;
            case DiagnosticSeverity.Error:
            case DiagnosticSeverity.Critical:
                GD.PushError(exception is null ? consoleMessage : $"{consoleMessage}\n{exception}");
                break;
            default:
                GD.Print(consoleMessage);
                break;
        }
        return entry;
    }

    /// <summary>返回统一的运行时状态、资源所有权与最近日志快照。</summary>
    public RuntimeSnapshot Snapshot() => new(
        SnapshotSchema,
        SnapshotSchemaVersion,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow - _startedAtUtc,
        new LifetimeRecord(_lifetime.Name, _lifetime.IsDisposed, _lifetime.OwnedCount),
        new SceneRecord(_scenes.ActivePath, _scenes.ActiveNode?.Name.ToString()),
        _events.Snapshot(),
        _scheduler.Snapshot(),
        _physicsScheduler.Snapshot(),
        _actions.Snapshot(),
        _metrics.Snapshot(),
        _assets.Snapshot(),
        _ui.Snapshot(),
        _features.Snapshot(),
        _audio.Snapshot(),
        _input.Snapshot(),
        _localization.Snapshot(),
        _settings.Current,
        _log.Snapshot());

    /// <summary>把当前快照序列化为稳定、可供工具读取的 JSON。</summary>
    public string ToJson() => JsonSerializer.Serialize(Snapshot(), JsonOptions);

    internal object SnapshotSection(string section)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            throw new ArgumentException("Runtime snapshot section cannot be empty.", nameof(section));
        }

        return section.ToLowerInvariant() switch
        {
            "all" => Snapshot(),
            "runtime" => new RuntimeOverviewRecord(
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow - _startedAtUtc,
                new LifetimeRecord(_lifetime.Name, _lifetime.IsDisposed, _lifetime.OwnedCount),
                new SceneRecord(_scenes.ActivePath, _scenes.ActiveNode?.Name.ToString())),
            "events" => _events.Snapshot(),
            "scheduler" => new SchedulerOverviewRecord(
                _scheduler.Snapshot(),
                _physicsScheduler.Snapshot()),
            "actions" => _actions.Snapshot(),
            "metrics" => _metrics.Snapshot(),
            "resources" => _assets.Snapshot(),
            "ui" => _ui.Snapshot(),
            "features" => _features.Snapshot(),
            "audio" => _audio.Snapshot(),
            "input" => _input.Snapshot(),
            "localization" => _localization.Snapshot(),
            "settings" => _settings.Current,
            "logs" => _log.Snapshot(),
            _ => throw new ArgumentException(
                $"Unknown runtime snapshot section '{section}'. Available: " +
                string.Join(", ", AvailableSections),
                nameof(section)),
        };
    }

    /// <summary>把当前快照写入 user://，并返回绝对文件路径。</summary>
    public string WriteSnapshot(string userPath = "user://lx-runtime.json")
    {
        if (!userPath.StartsWith("user://", StringComparison.Ordinal))
        {
            throw new ArgumentException("Diagnostic snapshots must be written under user://.", nameof(userPath));
        }

        var absolutePath = ProjectSettings.GlobalizePath(userPath);
        System.IO.File.WriteAllText(absolutePath, ToJson());
        return absolutePath;
    }
}

public sealed record RuntimeSnapshot(
    string Schema,
    int SchemaVersion,
    DateTimeOffset CapturedAtUtc,
    TimeSpan Uptime,
    LifetimeRecord Lifetime,
    SceneRecord Scene,
    EventHubSnapshot Events,
    GameSchedulerSnapshot Scheduler,
    GameSchedulerSnapshot PhysicsScheduler,
    ActionRunnerSnapshot Actions,
    MetricSnapshot Metrics,
    IReadOnlyList<AssetRecord> Assets,
    IReadOnlyList<UIRecord> UI,
    IReadOnlyList<FeatureRecord> Features,
    AudioStateRecord Audio,
    InputSnapshot Input,
    LocalizationSnapshot Localization,
    UserSettings Settings,
    IReadOnlyList<DiagnosticEntry> Logs);

/// <summary>根生命周期的所有权状态。</summary>
public sealed record LifetimeRecord(string Name, bool IsDisposed, int OwnedCount);

/// <summary>当前活动世界场景的诊断信息。</summary>
public sealed record SceneRecord(string? Path, string? RootNodeName);

internal sealed record RuntimeOverviewRecord(
    DateTimeOffset CapturedAtUtc,
    TimeSpan Uptime,
    LifetimeRecord Lifetime,
    SceneRecord Scene);

internal sealed record SchedulerOverviewRecord(
    GameSchedulerSnapshot Frame,
    GameSchedulerSnapshot Physics);
