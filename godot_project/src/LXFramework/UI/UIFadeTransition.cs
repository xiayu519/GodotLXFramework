using Godot;

namespace LX.UI;

/// <summary>全屏黑幕过场的执行方式。</summary>
public enum UIFadeMode
{
    /// <summary>从透明变为黑色，并在完成后保持黑幕。</summary>
    FadeOut,
    /// <summary>从黑色变为透明，并在完成后移除黑幕。</summary>
    FadeIn,
    /// <summary>依次执行透明到黑色、短暂停留、黑色到透明的完整过场。</summary>
    FadeOutIn,
}

/// <summary>全屏黑幕过场的时间与缓动参数。</summary>
public sealed record UIFadeOptions
{
    /// <summary>透明变为黑色的默认时间。</summary>
    public TimeSpan FadeOutDuration { get; init; } = TimeSpan.FromSeconds(0.35);

    /// <summary>完整过场达到黑色后保持的默认时间。</summary>
    public TimeSpan HoldDuration { get; init; } = TimeSpan.FromSeconds(0.05);

    /// <summary>黑色变为透明的默认时间。</summary>
    public TimeSpan FadeInDuration { get; init; } = TimeSpan.FromSeconds(0.35);

    /// <summary>使用 Godot Tween 定义动画插值曲线；默认为柔和的 Sine。</summary>
    public Tween.TransitionType Transition { get; init; } = Tween.TransitionType.Sine;

    /// <summary>控制插值曲线作用于进入端、退出端或两端；默认为 InOut。</summary>
    public Tween.EaseType Ease { get; init; } = Tween.EaseType.InOut;

    internal void Validate()
    {
        ValidateDuration(FadeOutDuration, nameof(FadeOutDuration));
        ValidateDuration(HoldDuration, nameof(HoldDuration));
        ValidateDuration(FadeInDuration, nameof(FadeInDuration));
        if (!Enum.IsDefined(Transition))
        {
            throw new ArgumentOutOfRangeException(nameof(Transition), Transition, "Fade transition must be defined.");
        }
        if (!Enum.IsDefined(Ease))
        {
            throw new ArgumentOutOfRangeException(nameof(Ease), Ease, "Fade ease must be defined.");
        }
    }

    private static void ValidateDuration(TimeSpan duration, string parameterName)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, duration, "Fade durations cannot be negative.");
        }
    }
}
