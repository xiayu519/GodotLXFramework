using LX.Core.Lifetime;
using Godot;

namespace LX.Runtime;

/// <summary>
/// Initializes every LXFramework context receiver in an instantiated scene tree.
/// Children are initialized before parents; siblings follow GetChildren insertion order.
/// Godot _Ready guarantees the same child-before-parent relation but not sibling order.
/// </summary>
public static class LXContextInjector
{
    public static int InitializeTree(
        Node root,
        LXContext context,
        LifetimeScope lifetime)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(lifetime);
        lifetime.ThrowIfDisposed();

        var postOrder = new List<Node>();
        CollectPostOrder(root, postOrder);
        var initialized = 0;
        foreach (var node in postOrder)
        {
            if (node is ILXContextReceiver receiver && !receiver.IsLXInitialized)
            {
                receiver.Initialize(context, lifetime);
                initialized++;
            }
        }

        return initialized;
    }

    private static void CollectPostOrder(Node node, ICollection<Node> result)
    {
        var childCount = node.GetChildCount();
        for (var index = 0; index < childCount; index++)
        {
            CollectPostOrder(node.GetChild(index), result);
        }

        result.Add(node);
    }
}
