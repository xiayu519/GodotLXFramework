using Godot;

namespace LX.UI.Components;

/// <summary>可定位到任意画布坐标的通用文字提示。</summary>
[GlobalClass]
public partial class TooltipView : PanelContainer
{
    private Label? _label;

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", UIExamplePalette.Card(UIExamplePalette.Warning));
        _label = new Label { Text = "Tooltip" };
        _label.AddThemeColorOverride("font_color", UIExamplePalette.Text);
        AddChild(_label);
    }

    /// <summary>在画布坐标处显示提示文本。</summary>
    public void ShowAt(string text, Vector2 canvasPosition)
    {
        if (_label is null)
        {
            throw new InvalidOperationException("TooltipView must be inside the scene tree before use.");
        }
        _label.Text = text;
        Position = canvasPosition;
        Show();
    }

    /// <summary>隐藏当前提示。</summary>
    public void HideTooltip() => Hide();
}
