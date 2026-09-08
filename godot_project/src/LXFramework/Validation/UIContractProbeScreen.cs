using Godot;
using LX.UI;

namespace LX.Validation;

internal sealed class UIContractProbeState
{
    internal TaskCompletionSource Shown { get; } = new();
    internal TaskCompletionSource Entered { get; } = new();
    internal TaskCompletionSource Released { get; } = new();
    internal TaskCompletionSource Hiding { get; } = new();
    internal TaskCompletionSource HideReleased { get; } = new();
    internal bool WaitInShow { get; init; }
    internal bool WaitInEnter { get; init; }
    internal bool IgnoreCancellation { get; init; }
    internal bool FailOnRelease { get; init; }
    internal bool WaitInHide { get; init; }
    internal bool FailInHide { get; init; }
    internal Func<ValueTask<bool>>? BackHandler { get; set; }
    internal UIContractProbeScreen Screen { get; set; } = null!;
    internal CancellationToken Token { get; set; }
    internal int EnterCount { get; set; }
    internal int HideCount { get; set; }
    internal int ClickCount { get; set; }
}

internal partial class UIContractProbeScreen : UIScreen
{
    private UIContractProbeState _state = null!;
    internal void CloseFromProbe() => RequestClose();
    protected internal override ValueTask<bool> OnBackRequestedAsync(CancellationToken cancellationToken) =>
        _state.BackHandler?.Invoke() ?? ValueTask.FromResult(true);
    protected override void OnBindingsReady() => GetNode<Button>("Hit").Pressed += () => _state.ClickCount++;
    protected internal override async ValueTask OnShowAsync(object? payload, CancellationToken cancellationToken)
    {
        _state = (UIContractProbeState)payload!;
        var state = _state;
        state.Screen = this;
        state.Token = cancellationToken;
        state.Shown.SetResult();
        if (state.WaitInShow) await WaitAsync(state, cancellationToken);
    }
    protected internal override async ValueTask OnTransitionAsync(UITransitionPhase phase, CancellationToken cancellationToken)
    {
        if (phase != UITransitionPhase.Entering) return;
        var state = _state;
        state.EnterCount++;
        state.Entered.SetResult();
        if (state.WaitInEnter) await WaitAsync(state, cancellationToken);
    }
    private static async Task WaitAsync(UIContractProbeState state, CancellationToken token)
    {
        if (state.IgnoreCancellation) await state.Released.Task;
        else await state.Released.Task.WaitAsync(token);
        if (state.FailOnRelease) throw new InvalidOperationException("probe-show-failure");
    }
    protected internal override async ValueTask OnHideAsync(CancellationToken cancellationToken)
    {
        if (Activation.IsDisposed) throw new InvalidOperationException("Activation disposed before hide.");
        _state.HideCount++;
        _state.Hiding.SetResult();
        if (_state.WaitInHide) await _state.HideReleased.Task.WaitAsync(cancellationToken);
        if (_state.FailInHide) throw new InvalidOperationException("probe-hide-failure");
    }
}
