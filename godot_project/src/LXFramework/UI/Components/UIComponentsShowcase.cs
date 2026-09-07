using Godot;

namespace LX.UI.Components;

/// <summary>用白底、彩色文字展示 LXFramework 通用 UI 组件的独立示例场景。</summary>
[GlobalClass]
public partial class UIComponentsShowcase : UIScreen
{
    /// <inheritdoc />
    protected override void OnBindingsReady()
    {
        var toast = (ToastView)ToastPreview;
        toast.ShowMessage("Toast · Data table generated successfully");

        var confirm = (ConfirmDialogView)ConfirmPreview;
        confirm.Preview("Confirm · Apply the new input bindings?");

        var loading = (LoadingView)LoadingPreview;
        loading.ShowLoading("Loading · Prewarming scene resources", 0.68f);

        var tooltip = (TooltipView)TooltipPreview;
        tooltip.ShowAt("Tooltip · Hold Shift for details", Vector2.Zero);

        var list = (VirtualListView)VirtualListPreview;
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
    }
}
