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
        MasterVolume = Math.Clamp(MasterVolume, 0, 1),
        MusicVolume = Math.Clamp(MusicVolume, 0, 1),
        SfxVolume = Math.Clamp(SfxVolume, 0, 1),
        UiScale = Math.Clamp(UiScale, 0.75f, 2.0f),
        KeyBindings = NormalizeBindings(KeyBindings),
    };

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
