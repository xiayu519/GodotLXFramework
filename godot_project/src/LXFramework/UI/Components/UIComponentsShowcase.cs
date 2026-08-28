using Godot;

namespace LX.UI.Components;

/// <summary>用白底、彩色文字展示 LXFramework 通用 UI 组件的独立示例场景。</summary>
[GlobalClass]
public partial class UIComponentsShowcase : UIScreen
{
    /// <inheritdoc />
    protected override void OnBindingsReady()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = Color.FromHtml("#EFF6FF") };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 36);
        margin.AddThemeConstantOverride("margin_top", 28);
        margin.AddThemeConstantOverride("margin_right", 36);
        margin.AddThemeConstantOverride("margin_bottom", 28);
        AddChild(margin);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 22);
        margin.AddChild(columns);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0) };
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0) };
        columns.AddChild(left);
        columns.AddChild(right);

        left.AddChild(Heading("LXFramework UI Components", UIExamplePalette.Primary, 30));
        left.AddChild(Subheading("Reusable, lifecycle-friendly building blocks"));

        var toast = new ToastView();
        left.AddChild(toast);
        toast.ShowMessage("Toast · Data table generated successfully");

        var confirm = new ConfirmDialogView();
        left.AddChild(confirm);
        confirm.Preview("Confirm · Apply the new input bindings?");

        var loading = new LoadingView();
        left.AddChild(loading);
        loading.ShowLoading("Loading · Prewarming scene resources", 0.68f);

        right.AddChild(Heading("Tooltip & Virtual List", UIExamplePalette.Success, 24));
        var tooltip = new TooltipView();
        right.AddChild(tooltip);
        tooltip.ShowAt("Tooltip · Hold Shift for details", Vector2.Zero);

        var list = new VirtualListView { CustomMinimumSize = new Vector2(0, 390) };
        right.AddChild(list);
        list.Configure(
            1000,
            () =>
            {
                var label = new Label();
                label.AddThemeColorOverride("font_color", UIExamplePalette.Text);
                return label;
            },
            (control, index) => ((Label)control).Text =
                $"  Item {index + 1:0000}   ·   recycled row   ·   {(index % 2 == 0 ? "READY" : "CACHED")}");

        right.AddChild(Subheading("Only visible rows own Control nodes."));
    }

    private static Label Heading(string text, Color color, int fontSize)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    private static Label Subheading(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", UIExamplePalette.MutedText);
        label.AddThemeFontSizeOverride("font_size", 16);
        return label;
    }
}
