using LX.Core.Lifetime;
using LX.Core.World;
using LX.Runtime;
using Godot;

namespace LX.World;

/// <summary>
/// Persistent Area2D trigger for streamed worlds. One-shot completion is kept
/// in LX.WorldEvents, not in the chunk node that may be unloaded at any time.
/// </summary>
[GlobalClass]
public partial class WorldEventTrigger2D : Area2D, ILXContextReceiver
{
    private LXContext? _context;
    private LifetimeScope? _lifetime;
    private WorldEventId _eventId;

    [Export]
    public string EventId { get; set; } = string.Empty;

    [Export]
    public bool OneShot { get; set; } = true;

    [Export]
    public StringName RequiredBodyGroup { get; set; } = new("player");

    public bool IsLXInitialized => _context is not null;

    public void Initialize(LXContext context, LifetimeScope lifetime)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(lifetime);
        if (_context is not null)
        {
            throw new InvalidOperationException($"{GetType().Name} was initialized more than once.");
        }

        _context = context;
        _lifetime = lifetime;
    }

    public override void _Ready()
    {
        if (_context is null || _lifetime is null)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} must receive LX context before entering the scene tree.");
        }

        _eventId = new WorldEventId(EventId);
        BodyEntered += HandleBodyEntered;
        _lifetime.Defer(() => BodyEntered -= HandleBodyEntered);
        if (OneShot && _context.WorldEvents.IsCompleted(_eventId))
        {
            Monitoring = false;
        }
    }

    public void ResetCompletion()
    {
        EnsureReady();
        _context!.WorldEvents.Reset(_eventId);
        Monitoring = true;
    }

    public bool TryTrigger(Node2D actor)
    {
        EnsureReady();
        ArgumentNullException.ThrowIfNull(actor);
        if (RequiredBodyGroup.ToString().Length > 0 && !actor.IsInGroup(RequiredBodyGroup))
        {
            return false;
        }

        if (OneShot && !_context!.WorldEvents.TryComplete(_eventId))
        {
            return false;
        }

        if (OneShot)
        {
            SetDeferred(PropertyName.Monitoring, false);
        }
        _context!.Events.Publish(new WorldEventTriggered(_eventId, actor, this));
        return true;
    }

    private void HandleBodyEntered(Node2D body) => TryTrigger(body);

    private void EnsureReady()
    {
        if (_context is null || _lifetime is null || string.IsNullOrEmpty(_eventId.Value))
        {
            throw new InvalidOperationException($"{GetType().Name} is not ready.");
        }
    }
}

public readonly record struct WorldEventTriggered(
    WorldEventId EventId,
    Node2D Actor,
    WorldEventTrigger2D Trigger);
