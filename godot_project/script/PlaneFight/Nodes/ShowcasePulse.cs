using Godot;
using LX.Core.Lifetime;
using LX.Pooling;
using LX.Runtime;

namespace PlaneFight.Nodes;

public partial class ShowcasePulse : Node2D, ILXContextReceiver, IPooledNodeLifecycle
{
    private LXContext? _context;
    private LifetimeScope? _lifetime;
    private readonly Polygon2D _ring;
    private LifetimeScope? _activation;

    public ShowcasePulse()
    {
        _ring = new Polygon2D
        {
            Polygon = BuildRing(24),
            Color = new Color(0.2f, 0.85f, 1f, 0.75f),
        };
        AddChild(_ring);
    }

    protected LXContext LX => _context ??
        throw new InvalidOperationException($"{GetType().Name} has not received a LXFramework context.");

    protected LifetimeScope Lifetime => _lifetime ??
        throw new InvalidOperationException($"{GetType().Name} has not received a LXFramework lifetime.");

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
        OnLXInitialized();
    }

    protected virtual void OnLXInitialized()
    {
        LX.Metrics.Increment("plane.showcase.pulse_initialized");
    }

    public void OnRent(LifetimeScope activation)
    {
        _activation = activation;
        Visible = true;
        Scale = Vector2.One;
        Modulate = Colors.White;
        LX.Metrics.Increment("plane.showcase.pulse_rented");
    }

    public void OnReturn()
    {
        _activation = null;
        Visible = false;
        Position = Vector2.Zero;
        Scale = Vector2.One;
        Modulate = Colors.White;
        LX.Metrics.Increment("plane.showcase.pulse_returned");
    }

    internal void Configure(Vector2 position, Color color)
    {
        Position = position;
        _ring.Color = color;
    }

    private static Vector2[] BuildRing(int segments)
    {
        var points = new Vector2[segments];
        for (var index = 0; index < segments; index++)
        {
            var angle = Mathf.Tau * index / segments;
            var radius = index % 2 == 0 ? 24 : 15;
            points[index] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        return points;
    }
}
