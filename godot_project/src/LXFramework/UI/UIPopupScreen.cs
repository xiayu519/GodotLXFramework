using Godot;

namespace LX.UI;

/// <summary>提供全屏遮罩、居中内容与标准缩放淡入淡出的弹窗页面基类。</summary>
public abstract partial class UIPopupScreen : UIScreen
{
    private bool _restStateCaptured;
    private Vector2 _restScale;
    private Color _restTargetModulate;
    private Color _restScrimModulate;

    /// <summary>承载弹窗缩放和透明度过渡的节点路径。</summary>
    [Export]
    public NodePath MotionTargetPath { get; set; } = new("%MotionPivot");

    /// <summary>全屏遮罩节点路径。</summary>
    [Export]
    public NodePath ScrimPath { get; set; } = new("%Scrim");

    /// <summary>进入动画的秒数；设为零时立即显示最终状态。</summary>
    [Export(PropertyHint.Range, "0,2,0.01,or_greater")]
    public double EnterDurationSeconds { get; set; } = 0.22;

    /// <summary>退出动画的秒数；设为零时立即进入隐藏状态。</summary>
    [Export(PropertyHint.Range, "0,2,0.01,or_greater")]
    public double ExitDurationSeconds { get; set; } = 0.14;

    /// <summary>进入动画开始时相对静止尺寸的缩放比例。</summary>
    [Export(PropertyHint.Range, "0.5,1,0.01")]
    public float EnterScale { get; set; } = 0.92f;

    /// <summary>退出动画结束时相对静止尺寸的缩放比例。</summary>
    [Export(PropertyHint.Range, "0.5,1,0.01")]
    public float ExitScale { get; set; } = 0.96f;

    /// <inheritdoc />
    protected internal override async ValueTask OnTransitionAsync(
        UITransitionPhase phase,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateOptions();

        var target = GetNodeOrNull<Control>(MotionTargetPath) ??
            throw new InvalidOperationException(
                $"{GetType().Name} requires a Control at '{MotionTargetPath}'.");
        var scrim = GetNodeOrNull<CanvasItem>(ScrimPath) ??
            throw new InvalidOperationException(
                $"{GetType().Name} requires a CanvasItem at '{ScrimPath}'.");

        if (phase == UITransitionPhase.Entering)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            cancellationToken.ThrowIfCancellationRequested();
            CaptureRestState(target, scrim);
            target.PivotOffset = target.Size * 0.5f;

            var startTargetModulate = WithAlpha(_restTargetModulate, 0f);
            var startScrimModulate = WithAlpha(_restScrimModulate, 0f);
            target.Scale = _restScale * EnterScale;
            target.Modulate = startTargetModulate;
            scrim.Modulate = startScrimModulate;
            await AnimateAsync(
                target,
                scrim,
                target.Scale,
                _restScale,
                startTargetModulate,
                _restTargetModulate,
                startScrimModulate,
                _restScrimModulate,
                EnterDurationSeconds,
                Tween.TransitionType.Quint,
                Tween.EaseType.Out,
                cancellationToken);
            return;
        }

        CaptureRestState(target, scrim);
        await AnimateAsync(
            target,
            scrim,
            target.Scale,
            _restScale * ExitScale,
            target.Modulate,
            WithAlpha(_restTargetModulate, 0f),
            scrim.Modulate,
            WithAlpha(_restScrimModulate, 0f),
            ExitDurationSeconds,
            Tween.TransitionType.Quint,
            Tween.EaseType.In,
            cancellationToken);
    }

    private async ValueTask AnimateAsync(
        Control target,
        CanvasItem scrim,
        Vector2 startScale,
        Vector2 endScale,
        Color startTargetModulate,
        Color endTargetModulate,
        Color startScrimModulate,
        Color endScrimModulate,
        double durationSeconds,
        Tween.TransitionType transition,
        Tween.EaseType ease,
        CancellationToken cancellationToken)
    {
        if (durationSeconds == 0)
        {
            ApplyState(
                target,
                scrim,
                endScale,
                endTargetModulate,
                endScrimModulate);
            return;
        }

        var startedAt = Time.GetTicksUsec();
        while (true)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            cancellationToken.ThrowIfCancellationRequested();
            var elapsedSeconds = (Time.GetTicksUsec() - startedAt) / 1_000_000d;
            if (elapsedSeconds >= durationSeconds)
            {
                ApplyState(
                    target,
                    scrim,
                    endScale,
                    endTargetModulate,
                    endScrimModulate);
                return;
            }

            var weight = (float)Tween.InterpolateValue(
                0f,
                1f,
                elapsedSeconds,
                durationSeconds,
                transition,
                ease);
            ApplyState(
                target,
                scrim,
                startScale.Lerp(endScale, weight),
                startTargetModulate.Lerp(endTargetModulate, weight),
                startScrimModulate.Lerp(endScrimModulate, weight));
        }
    }

    private void CaptureRestState(Control target, CanvasItem scrim)
    {
        if (_restStateCaptured)
        {
            return;
        }

        _restScale = target.Scale;
        _restTargetModulate = target.Modulate;
        _restScrimModulate = scrim.Modulate;
        _restStateCaptured = true;
    }

    private void ValidateOptions()
    {
        if (!double.IsFinite(EnterDurationSeconds) || EnterDurationSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(EnterDurationSeconds),
                EnterDurationSeconds,
                "Popup enter duration must be finite and non-negative.");
        }
        if (!double.IsFinite(ExitDurationSeconds) || ExitDurationSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ExitDurationSeconds),
                ExitDurationSeconds,
                "Popup exit duration must be finite and non-negative.");
        }
        if (!float.IsFinite(EnterScale) || EnterScale <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(EnterScale),
                EnterScale,
                "Popup enter scale must be finite and positive.");
        }
        if (!float.IsFinite(ExitScale) || ExitScale <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ExitScale),
                ExitScale,
                "Popup exit scale must be finite and positive.");
        }
    }

    private static void ApplyState(
        Control target,
        CanvasItem scrim,
        Vector2 scale,
        Color targetModulate,
        Color scrimModulate)
    {
        target.Scale = scale;
        target.Modulate = targetModulate;
        scrim.Modulate = scrimModulate;
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.A = alpha;
        return color;
    }
}
