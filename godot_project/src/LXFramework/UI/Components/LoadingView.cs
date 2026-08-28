using Godot;

namespace LX.UI.Components;

/// <summary>统一展示加载消息和可选进度的覆盖组件。</summary>
[GlobalClass]
public partial class LoadingView : PanelContainer
{
    private Label? _message;
    private ProgressBar? _progress;

    /// <inheritdoc />
    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", UIExamplePalette.Card(UIExamplePalette.Primary));
        var layout = new VBoxContainer();
        AddChild(layout);
        _message = new Label { Text = "Loading…" };
        _message.AddThemeColorOverride("font_color", UIExamplePalette.Text);
        _progress = new ProgressBar { MinValue = 0, MaxValue = 100, ShowPercentage = true };
        _progress.AddThemeColorOverride("font_color", UIExamplePalette.Primary);
        layout.AddChild(_message);
        layout.AddChild(_progress);
    }

    /// <summary>显示加载状态；progress 为空时隐藏进度条，否则取值范围为 0 到 1。</summary>
    public void ShowLoading(string message, float? progress = null)
    {
        EnsureReady();
        _message!.Text = string.IsNullOrWhiteSpace(message) ? "Loading…" : message.Trim();
        _progress!.Visible = progress is not null;
        if (progress is not null)
        {
            _progress.Value = Math.Clamp(progress.Value, 0, 1) * 100;
        }
        Show();
    }

    /// <summary>隐藏加载状态。</summary>
    public void HideLoading() => Hide();

    private void EnsureReady()
    {
        if (_message is null || _progress is null)
        {
            throw new InvalidOperationException("LoadingView must be inside the scene tree before use.");
        }
    }
}
