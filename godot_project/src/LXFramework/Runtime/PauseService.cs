using LX.Core.Diagnostics;
using LX.Core.Events;
using LX.Core.Time;
using Godot;

namespace LX.Runtime;

public sealed class PauseService
{
    private readonly Node _host;
    private readonly GameClock _frameClock;
    private readonly GameClock _physicsClock;
    private readonly EventHub _events;
    private readonly MetricRegistry _metrics;
    private readonly int _mainThreadId;

    public PauseService(
        Node host,
        GameClock frameClock,
        GameClock physicsClock,
        EventHub events,
        MetricRegistry metrics)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _frameClock = frameClock ?? throw new ArgumentNullException(nameof(frameClock));
        _physicsClock = physicsClock ?? throw new ArgumentNullException(nameof(physicsClock));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _mainThreadId = System.Environment.CurrentManagedThreadId;
        UpdateMetrics();
    }

    public bool IsPaused { get; private set; }

    public void SetPaused(bool paused)
    {
        EnsureMainThread();
        var tree = _host.GetTree();
        if (_host.IsInsideTree() && !_host.CanProcess())
        {
            throw new InvalidOperationException(
                "Pause state cannot change while the LXHost is suspended or disabled.");
        }
        if (IsPaused == paused && tree.Paused == paused)
        {
            return;
        }

        var previous = IsPaused;
        IsPaused = paused;
        _frameClock.IsPaused = paused;
        _physicsClock.IsPaused = paused;
        try
        {
            tree.Paused = paused;
            if (tree.Paused != paused)
            {
                throw new InvalidOperationException(
                    $"Godot rejected the requested SceneTree pause state '{paused}'.");
            }
        }
        catch
        {
            IsPaused = previous;
            _frameClock.IsPaused = previous;
            _physicsClock.IsPaused = previous;
            UpdateMetrics();
            throw;
        }

        UpdateMetrics();
        if (previous != paused)
        {
            _events.Publish(new PauseChanged(paused));
        }
    }

    public void Toggle() => SetPaused(!IsPaused);

    private void UpdateMetrics() => _metrics.SetGauge("runtime.paused", IsPaused ? 1 : 0);

    private void EnsureMainThread()
    {
        if (System.Environment.CurrentManagedThreadId != _mainThreadId)
        {
            throw new InvalidOperationException("Pause changes must run on Godot's main thread.");
        }
    }
}

public readonly record struct PauseChanged(bool IsPaused);
