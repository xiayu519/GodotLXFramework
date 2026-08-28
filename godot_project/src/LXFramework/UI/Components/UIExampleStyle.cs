using Godot;

namespace LX.UI.Components;

/// <summary>通用 UI 示例使用的高对比度配色；产品可以完全替换这些颜色。</summary>
public static class UIExamplePalette
{
    /// <summary>卡片和对话框的纯白背景。</summary>
    public static readonly Color Surface = Colors.White;

    /// <summary>标题、主按钮和进度条使用的蓝色。</summary>
    public static readonly Color Primary = Color.FromHtml("#2563EB");

    /// <summary>确认与成功状态使用的绿色。</summary>
    public static readonly Color Success = Color.FromHtml("#16A34A");

    /// <summary>警告和提示状态使用的橙色。</summary>
    public static readonly Color Warning = Color.FromHtml("#F59E0B");

    /// <summary>正文使用的深灰色，保证白底可读性。</summary>
    public static readonly Color Text = Color.FromHtml("#1F2937");

    /// <summary>次要说明文字使用的中灰色。</summary>
    public static readonly Color MutedText = Color.FromHtml("#64748B");

    /// <summary>卡片描边使用的浅蓝灰色。</summary>
    public static readonly Color Border = Color.FromHtml("#CBD5E1");

    /// <summary>创建统一圆角、描边和内边距的白底卡片样式。</summary>
    public static StyleBoxFlat Card(Color? accent = null)
    {
        var style = new StyleBoxFlat
        {
            BgColor = Surface,
            BorderColor = accent ?? Border,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            ContentMarginLeft = 14,
            ContentMarginTop = 10,
            ContentMarginRight = 14,
            ContentMarginBottom = 10,
        };
        return style;
    }
}
