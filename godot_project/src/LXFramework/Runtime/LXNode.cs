using LX.Core.Lifetime;
using Godot;

namespace LX.Runtime;

public abstract partial class LXNode : Node, ILXContextReceiver
{
    private LXContext? _context;
    private LifetimeScope? _lifetime;

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
    }
}
