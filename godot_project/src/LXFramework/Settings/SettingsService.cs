using LX.Core.Common;
using LX.Core.Diagnostics;
using LX.Core.Events;
using LX.Core.Persistence;
using LX.Localization;
using LX.Input;
using Godot;

namespace LX.Settings;

public sealed class SettingsService
{
    private const string Slot = "preferences";
    private readonly Node _host;
    private readonly EventHub _events;
    private readonly MetricRegistry _metrics;
    private readonly LocalizationService _localization;
    private readonly InputRouter _input;
    private readonly SaveStore<UserSettings> _store;
    private readonly HashSet<string> _appliedKeyBindings = new(StringComparer.Ordinal);
    private readonly int _mainThreadId;

    public SettingsService(
        Node host,
        EventHub events,
        MetricRegistry metrics,
        LocalizationService localization,
        InputRouter input)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _mainThreadId = System.Environment.CurrentManagedThreadId;
        _store = new SaveStore<UserSettings>(
            ProjectSettings.GlobalizePath("user://settings"),
            currentVersion: 1);
    }

    public UserSettings Current { get; private set; } = new();

    public async ValueTask<Result<UserSettings>> InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureMainThread();
        if (!_store.Exists(Slot))
        {
            Apply(Current);
            return Result<UserSettings>.Success(Current);
        }

        var loaded = await _store.LoadAsync(Slot, cancellationToken);
        EnsureMainThread();
        if (loaded.IsFailure)
        {
            _metrics.Increment("settings.load_failures");
            Apply(Current);
            var error = loaded.Error!.Value;
            return Result<UserSettings>.Failure(
                error.Code,
                error.Message,
                error.Exception);
        }

        Current = loaded.Value!.State.Normalize();
        Apply(Current);
        return Result<UserSettings>.Success(Current);
    }

    public async ValueTask SetAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        EnsureMainThread();
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = settings.Normalize();
        await _store.SaveAsync(Slot, normalized, cancellationToken);
        EnsureMainThread();

        var previous = Current;
        Current = normalized;
        Apply(Current);
        _events.Publish(new UserSettingsChanged(previous, Current));
    }

    public ValueTask UpdateAsync(
        Func<UserSettings, UserSettings> update,
        CancellationToken cancellationToken = default)
    {
        EnsureMainThread();
        ArgumentNullException.ThrowIfNull(update);
        return SetAsync(update(Current), cancellationToken);
    }

    public ValueTask SetKeyBindingAsync(
        string godotAction,
        Key physicalKey,
        CancellationToken cancellationToken = default)
    {
        EnsureMainThread();
        ArgumentException.ThrowIfNullOrWhiteSpace(godotAction);
        if (!_input.HasGodotAction(godotAction))
        {
            throw new KeyNotFoundException($"Godot input action '{godotAction}' is not registered.");
        }
        if (physicalKey == Key.None)
        {
            throw new ArgumentException("Physical key bindings cannot use Key.None.", nameof(physicalKey));
        }
        var bindings = new Dictionary<string, string>(
            Current.KeyBindings ?? [],
            StringComparer.Ordinal)
        {
            [godotAction] = physicalKey.ToString(),
        };
        return SetAsync(Current with { KeyBindings = bindings }, cancellationToken);
    }

    private void Apply(UserSettings settings)
    {
        SetBusVolume("Master", settings.MasterVolume);
        SetBusVolume("Music", settings.MusicVolume);
        SetBusVolume("SFX", settings.SfxVolume);
        _localization.SetLocale(settings.Locale);
        DisplayServer.WindowSetMode(settings.Fullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);
        _host.GetWindow().ContentScaleFactor = settings.UiScale;
        var appliedNow = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in settings.KeyBindings ?? [])
        {
            if (!Enum.TryParse<Key>(binding.Value, ignoreCase: false, out var key) ||
                !_input.HasGodotAction(binding.Key))
            {
                _metrics.Increment("settings.invalid_key_bindings");
                continue;
            }
            _input.ReplaceKeyBinding(binding.Key, key);
            appliedNow.Add(binding.Key);
        }
        foreach (var removed in _appliedKeyBindings.Except(appliedNow).ToArray())
        {
            if (_input.HasGodotAction(removed))
            {
                _input.RestoreDefaultKeyBinding(removed);
            }
        }
        _appliedKeyBindings.Clear();
        _appliedKeyBindings.UnionWith(appliedNow);
        _metrics.SetGauge("settings.ui_scale", settings.UiScale);
        _metrics.SetGauge("settings.fullscreen", settings.Fullscreen ? 1 : 0);
    }

    private static void SetBusVolume(string busName, float linearVolume)
    {
        var index = AudioServer.GetBusIndex(busName);
        if (index < 0)
        {
            return;
        }

        AudioServer.SetBusMute(index, linearVolume <= 0);
        AudioServer.SetBusVolumeDb(index, linearVolume <= 0 ? -80 : Mathf.LinearToDb(linearVolume));
    }

    private void EnsureMainThread()
    {
        if (System.Environment.CurrentManagedThreadId != _mainThreadId)
        {
            throw new InvalidOperationException("Settings must be applied from Godot's main thread.");
        }
    }
}

public readonly record struct UserSettingsChanged(UserSettings Previous, UserSettings Current);
