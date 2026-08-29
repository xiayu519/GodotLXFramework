using LX.Core.Events;
using Godot;

namespace LX.Localization;

public sealed class LocalizationService
{
    private static readonly StringName EmptyContext = new();
    private readonly EventHub _events;
    private readonly HashSet<string> _missingKeys = new(StringComparer.Ordinal);
    private readonly int _mainThreadId;

    public LocalizationService(EventHub events)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _mainThreadId = System.Environment.CurrentManagedThreadId;
    }

    public string CurrentLocale
    {
        get
        {
            EnsureMainThread();
            return TranslationServer.GetLocale();
        }
    }

    public IReadOnlyList<string> LoadedLocales
    {
        get
        {
            EnsureMainThread();
            return TranslationServer.GetLoadedLocales();
        }
    }

    /// <summary>启用后把翻译结果转换为加长的伪本地化文本，用于发现截断与硬编码。</summary>
    public bool PseudoLocalizationEnabled { get; set; }

    /// <summary>自启动或上次清除后发现的缺失翻译键。</summary>
    public IReadOnlyList<string> MissingKeys
    {
        get
        {
            EnsureMainThread();
            return _missingKeys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        }
    }

    public void SetLocale(string locale)
    {
        EnsureMainThread();
        if (string.IsNullOrWhiteSpace(locale))
        {
            throw new ArgumentException("Locale cannot be empty.", nameof(locale));
        }

        var previous = CurrentLocale;
        TranslationServer.SetLocale(locale.Trim());
        var current = CurrentLocale;
        if (!string.Equals(previous, current, StringComparison.Ordinal))
        {
            _events.Publish(new LocaleChanged(previous, current));
        }
    }

    public string Text(StringName key)
    {
        EnsureMainThread();
        return FinalizeTranslation(key.ToString(), TranslationServer.Translate(key, EmptyContext).ToString());
    }

    public string Text(StringName key, StringName context)
    {
        EnsureMainThread();
        return FinalizeTranslation(key.ToString(), TranslationServer.Translate(key, context).ToString());
    }

    public string Plural(
        StringName singular,
        StringName plural,
        int count)
    {
        EnsureMainThread();
        var translated = TranslationServer.TranslatePlural(singular, plural, count, EmptyContext).ToString();
        return FinalizeTranslation(count == 1 ? singular.ToString() : plural.ToString(), translated);
    }

    public string Plural(
        StringName singular,
        StringName plural,
        int count,
        StringName context)
    {
        EnsureMainThread();
        var translated = TranslationServer.TranslatePlural(singular, plural, count, context).ToString();
        return FinalizeTranslation(count == 1 ? singular.ToString() : plural.ToString(), translated);
    }

    /// <summary>清除缺失键集合，通常在重新导入翻译资源后调用。</summary>
    public void ClearMissingKeys()
    {
        EnsureMainThread();
        _missingKeys.Clear();
    }

    /// <summary>
    /// 按完整区域、语言、显式回退和首个可用项的顺序解析本地化资源路径。
    /// </summary>
    public string ResolveVariant(
        IReadOnlyDictionary<string, string> variants,
        string? fallbackLocale = "en")
    {
        EnsureMainThread();
        ArgumentNullException.ThrowIfNull(variants);
        if (variants.Count == 0)
        {
            throw new ArgumentException("At least one localized resource variant is required.", nameof(variants));
        }

        var locale = CurrentLocale.Replace('-', '_');
        var language = locale.Split('_', StringSplitOptions.RemoveEmptyEntries)[0];
        foreach (var candidate in new[] { locale, language, fallbackLocale })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && variants.TryGetValue(candidate, out var path))
            {
                return path;
            }
        }

        return variants.OrderBy(pair => pair.Key, StringComparer.Ordinal).First().Value;
    }

    /// <summary>返回当前语言、本地化 QA 开关和缺失键的快照。</summary>
    public LocalizationSnapshot Snapshot()
    {
        EnsureMainThread();
        return new LocalizationSnapshot(CurrentLocale, PseudoLocalizationEnabled, MissingKeys);
    }

    private string FinalizeTranslation(string key, string translated)
    {
        if (string.Equals(key, translated, StringComparison.Ordinal))
        {
            _missingKeys.Add(key);
        }
        return PseudoLocalizationEnabled ? PseudoLocalize(translated) : translated;
    }

    private static string PseudoLocalize(string text)
    {
        var replacements = new Dictionary<char, char>
        {
            ['a'] = 'á', ['e'] = 'ë', ['i'] = 'ï', ['o'] = 'ö', ['u'] = 'ü',
            ['A'] = 'Á', ['E'] = 'Ë', ['I'] = 'Ï', ['O'] = 'Ö', ['U'] = 'Ü',
        };
        var builder = new System.Text.StringBuilder(text.Length + 8).Append('［');
        var placeholderDepth = 0;
        foreach (var character in text)
        {
            if (character == '{')
            {
                placeholderDepth++;
            }
            builder.Append(placeholderDepth == 0 && replacements.TryGetValue(character, out var replacement)
                ? replacement
                : character);
            if (character == '}' && placeholderDepth > 0)
            {
                placeholderDepth--;
            }
        }
        builder.Append(" ···］");
        return builder.ToString();
    }

    private void EnsureMainThread()
    {
        if (System.Environment.CurrentManagedThreadId != _mainThreadId)
        {
            throw new InvalidOperationException("Localization operations must run on Godot's main thread.");
        }
    }
}

public readonly record struct LocaleChanged(string Previous, string Current);

/// <summary>本地化服务的可序列化运行时状态。</summary>
public sealed record LocalizationSnapshot(
    string CurrentLocale,
    bool PseudoLocalizationEnabled,
    IReadOnlyList<string> MissingKeys);
