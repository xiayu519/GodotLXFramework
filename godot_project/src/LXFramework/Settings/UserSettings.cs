namespace LX.Settings;

public sealed record UserSettings(
    string Locale = "zh_CN",
    float MasterVolume = 1.0f,
    float MusicVolume = 0.8f,
    float SfxVolume = 0.8f,
    bool Fullscreen = false,
    float UiScale = 1.0f,
    Dictionary<string, string>? KeyBindings = null)
{
    public UserSettings Normalize() => this with
    {
        Locale = string.IsNullOrWhiteSpace(Locale) ? "zh_CN" : Locale.Trim(),
        MasterVolume = ClampFinite(MasterVolume, 0, 1, 1.0f),
        MusicVolume = ClampFinite(MusicVolume, 0, 1, 0.8f),
        SfxVolume = ClampFinite(SfxVolume, 0, 1, 0.8f),
        UiScale = ClampFinite(UiScale, 0.75f, 2.0f, 1.0f),
        KeyBindings = NormalizeBindings(KeyBindings),
    };

    private static float ClampFinite(float value, float minimum, float maximum, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private static Dictionary<string, string> NormalizeBindings(
        IReadOnlyDictionary<string, string>? bindings)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in bindings ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                result[pair.Key.Trim()] = pair.Value.Trim();
            }
        }
        return result;
    }
}
