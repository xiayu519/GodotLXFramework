using LX.UI;
using Godot;

namespace LX.Validation;

public partial class FrameworkStatusScreen : UIScreen
{
    protected override void OnLXInitialized()
    {
        LX.Metrics.Increment("validation.ui_context_injected");
    }

    protected internal override ValueTask OnShowAsync(object? payload, CancellationToken cancellationToken)
    {
        StatusLabel.Text = "LXFramework 运行正常";
        NameInput.PlaceholderText = "输入文字以测试原生输入";
        NameInput.GrabFocus();

        ApplyNameButton.Pressed += ApplyText;
        NameInput.TextSubmitted += SubmitText;
        Activation.Defer(() => ApplyNameButton.Pressed -= ApplyText);
        Activation.Defer(() => NameInput.TextSubmitted -= SubmitText);
        return ValueTask.CompletedTask;
    }

    protected internal override ValueTask OnHideAsync(CancellationToken cancellationToken)
    {
        ResultLabel.Text = string.Empty;
        return ValueTask.CompletedTask;
    }

    private void SubmitText(string value) => ApplyText(value);

    private void ApplyText() => ApplyText(NameInput.Text);

    private void ApplyText(string rawValue)
    {
        var value = rawValue.Trim();
        ResultLabel.Text = value.Length == 0 ? "请输入文字。" : $"输入：{value}";
    }
}
