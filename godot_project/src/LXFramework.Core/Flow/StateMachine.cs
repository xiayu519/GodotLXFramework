namespace LX.Core.Flow;

public interface IState<TContext>
{
    ValueTask EnterAsync(TContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    void Tick(TContext context, double deltaSeconds);

    ValueTask ExitAsync(TContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

public sealed class StateMachine<TState, TContext> where TState : notnull
{
    private readonly Dictionary<TState, IState<TContext>> _states = [];
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly AsyncLocal<int> _transitionDepth = new();
    private readonly TContext _context;
    private IState<TContext>? _currentState;

    public StateMachine(TContext context)
    {
        _context = context;
    }

    public TState? Current { get; private set; }

    public bool HasCurrent => _currentState is not null;

    public void Register(TState key, IState<TContext> state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!_states.TryAdd(key, state))
        {
            throw new InvalidOperationException($"State '{key}' is already registered.");
        }
    }

    public async ValueTask TransitionAsync(TState next, CancellationToken cancellationToken = default)
    {
        if (_transitionDepth.Value != 0)
        {
            throw new InvalidOperationException(
                "StateMachine transition cannot be re-entered from EnterAsync or ExitAsync callbacks.");
        }
        if (!_states.TryGetValue(next, out var nextState))
        {
            throw new KeyNotFoundException($"State '{next}' is not registered.");
        }

        await _transitionGate.WaitAsync(cancellationToken);
        _transitionDepth.Value++;
        try
        {
            if (EqualityComparer<TState>.Default.Equals(Current!, next) && _currentState is not null)
            {
                return;
            }

            if (_currentState is not null)
            {
                await _currentState.ExitAsync(_context, cancellationToken);
                _currentState = null;
                Current = default;
            }

            await nextState.EnterAsync(_context, cancellationToken);
            _currentState = nextState;
            Current = next;
        }
        finally
        {
            _transitionDepth.Value--;
            _transitionGate.Release();
        }
    }

    public void Tick(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        _currentState?.Tick(_context, deltaSeconds);
    }
}
