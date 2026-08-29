using Godot;

namespace LX.UI;

/// <summary>由 UIService 托管的全屏黑幕过场 prefab 根节点。</summary>
public partial class UIFadeTransitionScreen : UIScreen
{
    internal float Opacity => Blackout.Color.A;

    internal void SetOpacity(float opacity)
    {
        var color = Blackout.Color;
        color.A = Math.Clamp(opacity, 0f, 1f);
        Blackout.Color = color;
    }

    internal async ValueTask AnimateOpacityAsync(
        float targetOpacity,
        TimeSpan duration,
        Tween.TransitionType transition,
        Tween.EaseType ease,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startOpacity = Opacity;
        targetOpacity = Math.Clamp(targetOpacity, 0f, 1f);
        if (duration == TimeSpan.Zero || Math.Abs(targetOpacity - startOpacity) <= float.Epsilon)
        {
            SetOpacity(targetOpacity);
            return;
        }

        var durationSeconds = duration.TotalSeconds;
        var startedAt = Godot.Time.GetTicksUsec();
        while (true)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            cancellationToken.ThrowIfCancellationRequested();
            var elapsedSeconds = (Godot.Time.GetTicksUsec() - startedAt) / 1_000_000d;
            if (elapsedSeconds >= durationSeconds)
            {
                SetOpacity(targetOpacity);
                return;
            }

            var value = Tween.InterpolateValue(
                startOpacity,
                targetOpacity - startOpacity,
                elapsedSeconds,
                durationSeconds,
                transition,
                ease);
            SetOpacity((float)value);
        }
    }

    internal async ValueTask HoldAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (duration == TimeSpan.Zero)
        {
            return;
        }

        var durationSeconds = duration.TotalSeconds;
        var startedAt = Godot.Time.GetTicksUsec();
        while ((Godot.Time.GetTicksUsec() - startedAt) / 1_000_000d < durationSeconds)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
