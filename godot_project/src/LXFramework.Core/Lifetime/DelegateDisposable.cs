namespace LX.Core.Lifetime;

public sealed class DelegateDisposable : IDisposable
{
    private Action? _dispose;

    public DelegateDisposable(Action dispose)
    {
        _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
    }

    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}
