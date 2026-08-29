using LX.Core.Lifetime;

namespace LX.Core.Flow;

public interface IGameFlowState<TContext>
{
    ValueTask EnterAsync(
        TContext context,
        LifetimeScope lifetime,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;

    void Tick(TContext context, double deltaSeconds);

    ValueTask ExitAsync(TContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

/// <summary>
/// A lifecycle-aware state flow for product-level boot, menu, play, pause and
/// game-over states. Every active state owns a child LifetimeScope. The caller
/// must own or dispose the GameFlow itself when ExitAsync is required at teardown.
/// </summary>
public sealed class GameFlow<TState, TContext> : IAsyncDisposable where TState : notnull
{
    private readonly Dictionary<TState, IGameFlowState<TContext>> _states = [];
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly AsyncLocal<int> _transitionDepth = new();
    private readonly LifetimeScope _lifetime;
    private readonly TContext _context;
    private IGameFlowState<TContext>? _currentState;
    private LifetimeScope? _stateLifetime;
    private bool _disposed;

    public GameFlow(TContext context, LifetimeScope parentLifetime, string name = "GameFlow")
    {
        _context = context;
        ArgumentNullException.ThrowIfNull(parentLifetime);
        _lifetime = parentLifetime.CreateChild(name);
    }

    public TState? Current { get; private set; }

    public bool HasCurrent => _currentState is not null;

    public event Action<GameFlowTransition<TState>>? Transitioned;

    /// <summary>报告被隔离的 Transitioned 观察者异常；该事件自身的异常也会被隔离。</summary>
    public event Action<Exception>? TransitionObserverFailed;

    public void Register(TState key, IGameFlowState<TContext> state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(state);
        if (!_states.TryAdd(key, state))
        {
            throw new InvalidOperationException($"Game flow state '{key}' is already registered.");
        }
    }

    public async ValueTask TransitionAsync(TState next, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_transitionDepth.Value != 0)
        {
            throw new InvalidOperationException(
                "GameFlow transition cannot be re-entered from EnterAsync, ExitAsync, or Transitioned callbacks.");
        }
        if (!_states.TryGetValue(next, out var nextState))
        {
            throw new KeyNotFoundException($"Game flow state '{next}' is not registered.");
        }

        await _transitionGate.WaitAsync(cancellationToken);
        _transitionDepth.Value++;
        try
        {
            if (_currentState is not null && EqualityComparer<TState>.Default.Equals(Current!, next))
            {
                return;
            }

            var previous = Current;
            if (_currentState is not null)
            {
                await _currentState.ExitAsync(_context, cancellationToken);
                var previousLifetime = _stateLifetime;
                _currentState = null;
                Current = default;
                _stateLifetime = null;
                if (previousLifetime is not null)
                {
                    await previousLifetime.DisposeAsync();
                }
            }

            var nextLifetime = _lifetime.CreateChild($"State:{next}");
            try
            {
                await nextState.EnterAsync(_context, nextLifetime, cancellationToken);
            }
            catch (Exception enterError)
            {
                _currentState = null;
                Current = default;
                try
                {
                    await nextLifetime.DisposeAsync();
                }
                catch (Exception cleanupError)
                {
                    throw new AggregateException(
                        "Game flow state entry and attempted-state cleanup both failed.",
                        enterError,
                        cleanupError);
                }
                throw;
            }

            _stateLifetime = nextLifetime;
            _currentState = nextState;
            Current = next;
            NotifyTransitioned(new GameFlowTransition<TState>(previous, next));
        }
        finally
        {
            _transitionDepth.Value--;
            _transitionGate.Release();
        }
    }

    public void Tick(double deltaSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        _currentState?.Tick(_context, deltaSeconds);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _transitionGate.WaitAsync();
        List<Exception>? errors = null;
        try
        {
            if (_currentState is not null)
            {
                try
                {
                    await _currentState.ExitAsync(_context, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
                finally
                {
                    _currentState = null;
                    Current = default;
                }
            }
            if (_stateLifetime is not null)
            {
                try
                {
                    await _stateLifetime.DisposeAsync();
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
                finally
                {
                    _stateLifetime = null;
                }
            }
            try
            {
                await _lifetime.DisposeAsync();
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
            _states.Clear();
            Transitioned = null;
            TransitionObserverFailed = null;
        }
        finally
        {
            _transitionGate.Release();
            _transitionGate.Dispose();
        }

        if (errors is not null)
        {
            throw new AggregateException("Game flow cleanup reported one or more errors.", errors);
        }
    }

    private void NotifyTransitioned(GameFlowTransition<TState> transition)
    {
        if (Transitioned is not { } observers)
        {
            return;
        }

        foreach (Action<GameFlowTransition<TState>> observer in observers.GetInvocationList())
        {
            try
            {
                observer(transition);
            }
            catch (Exception exception)
            {
                NotifyTransitionObserverFailure(exception);
            }
        }
    }

    private void NotifyTransitionObserverFailure(Exception exception)
    {
        if (TransitionObserverFailed is not { } observers)
        {
            return;
        }

        foreach (Action<Exception> observer in observers.GetInvocationList())
        {
            try
            {
                observer(exception);
            }
            catch
            {
                // Diagnostic observers cannot change a committed flow transition.
            }
        }
    }
}

public readonly record struct GameFlowTransition<TState>(TState? Previous, TState Current);
