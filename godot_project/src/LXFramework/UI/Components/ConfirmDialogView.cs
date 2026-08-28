using Godot;

namespace LX.UI.Components;

/// <summary>返回布尔结果的通用确认组件，可嵌入 UIScreen 或普通场景。</summary>
[GlobalClass]
public partial class ConfirmDialogView : PanelContainer
{
    private Label? _message;
    private Button? _confirm;
    private Button? _cancel;
    private TaskCompletionSource<bool>? _pending;

    /// <inheritdoc />
    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", UIExamplePalette.Card(UIExamplePalette.Success));
        var layout = new VBoxContainer { Name = "Layout" };
        AddChild(layout);
        _message = new Label { Name = "Message", Text = "Continue?" };
        _message.AddThemeColorOverride("font_color", UIExamplePalette.Text);
        layout.AddChild(_message);
        var buttons = new HBoxContainer { Name = "Buttons" };
        layout.AddChild(buttons);
        _confirm = new Button { Name = "Confirm", Text = "Confirm" };
        _cancel = new Button { Name = "Cancel", Text = "Cancel" };
        _confirm.AddThemeColorOverride("font_color", UIExamplePalette.Success);
        _cancel.AddThemeColorOverride("font_color", UIExamplePalette.MutedText);
        buttons.AddChild(_confirm);
        buttons.AddChild(_cancel);
        _confirm.Pressed += () => Complete(true);
        _cancel.Pressed += () => Complete(false);
    }

    /// <summary>只更新展示内容，不等待用户输入；用于编辑器预览和视觉基准。</summary>
    public void Preview(string message)
    {
        EnsureReady();
        _message!.Text = message;
        Show();
    }

    /// <summary>显示提示并等待确认或取消；同一实例不允许并行等待两次。</summary>
    public async ValueTask<bool> ShowPromptAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        EnsureReady();
        if (_pending is not null)
        {
            throw new InvalidOperationException("ConfirmDialogView already has a pending prompt.");
        }

        Preview(message);
        _confirm!.GrabFocus();
        _pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => _pending.TrySetCanceled(cancellationToken));
        try
        {
            return await _pending.Task;
        }
        finally
        {
            _pending = null;
            Hide();
        }
    }

    private void Complete(bool result) => _pending?.TrySetResult(result);

    private void EnsureReady()
    {
        if (_message is null || _confirm is null || _cancel is null)
        {
            throw new InvalidOperationException("ConfirmDialogView must be inside the scene tree before use.");
        }
    }
}
