using Godot;

namespace LX.UI.Components;

/// <summary>轻量消息提示组件；同一实例重复显示会复用节点。</summary>
[GlobalClass]
public partial class ToastView : PanelContainer
{
    private Label? _label;
    private long _showSequence;

    /// <summary>未显式指定时消息保持显示的秒数。</summary>
    [Export(PropertyHint.Range, "0.1,10,0.1")]
    public double DefaultDurationSeconds { get; set; } = 2;

    /// <inheritdoc />
    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", UIExamplePalette.Card(UIExamplePalette.Primary));
        _label = GetNodeOrNull<Label>("Message") ?? new Label { Name = "Message" };
        if (_label.GetParent() is null)
        {
            AddChild(_label);
        }
        _label.AddThemeColorOverride("font_color", UIExamplePalette.Text);
    }

    /// <summary>立即显示消息，不自动隐藏；适合静态示例或由外部生命周期控制。</summary>
    public void ShowMessage(string message)
    {
        EnsureReady();
        _label!.Text = string.IsNullOrWhiteSpace(message) ? "Notification" : message.Trim();
        Show();
    }

    /// <summary>显示消息并在指定时间后自动隐藏。</summary>
    public async ValueTask ShowMessageAsync(
        string message,
        double? durationSeconds = null,
        CancellationToken cancellationToken = default)
    {
        ShowMessage(message);
        var sequence = ++_showSequence;
        var duration = durationSeconds ?? DefaultDurationSeconds;
        if (!double.IsFinite(duration) || duration <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        }
        cancellationToken.ThrowIfCancellationRequested();
        using var timer = GetTree().CreateTimer(
            duration,
            processAlways: true,
            processInPhysics: false,
            ignoreTimeScale: true);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Complete() => completion.TrySetResult();
        timer.Timeout += Complete;
        using var cancellation = cancellationToken.Register(() =>
            completion.TrySetCanceled(cancellationToken));
        try
        {
            await completion.Task;
        }
        finally
        {
            timer.Timeout -= Complete;
            if (sequence == _showSequence)
            {
                Hide();
            }
        }
    }

    private void EnsureReady()
    {
        if (_label is null)
        {
            throw new InvalidOperationException("ToastView must be inside the scene tree before use.");
        }
    }
}
