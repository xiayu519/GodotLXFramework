using LX.Core.Lifetime;
using Godot;

namespace LX.Runtime;

/// <summary>
/// Initializes every LXFramework context receiver in an instantiated scene tree.
/// Children are initialized before parents, mirroring Godot's _Ready ordering.
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

        var nodes = new List<Node>();
        CollectPreOrder(root, nodes);

        var initialized = 0;
        for (var index = nodes.Count - 1; index >= 0; index--)
        {
            if (nodes[index] is not ILXContextReceiver receiver ||
                receiver.IsLXInitialized)
            {
                continue;
            }

            receiver.Initialize(context, lifetime);
            initialized++;
        }

        return initialized;
    }

    private static void CollectPreOrder(Node node, ICollection<Node> result)
    {
        result.Add(node);
        foreach (var child in node.GetChildren())
        {
            CollectPreOrder(child, result);
        }
    }
}
