namespace LXFramework.Tools;

internal static class NodeScaffolder
{
    public static int Run(string root, IReadOnlyList<string> args)
    {
        if (args.Count < 3)
        {
            Console.Error.WriteLine("Usage: lx create node <Class> <GodotBase> [snake_case_id]");
            return 2;
        }

        var className = args[1];
        var godotBase = args[2];
        CodeNames.RequireIdentifier(className, nameof(args));
        CodeNames.RequireIdentifier(godotBase, nameof(args));
        var id = args.Count >= 4 ? args[3] : CodeNames.ToSnakeCase(className);
        CodeNames.RequireSnakeCase(id, nameof(args));

        var manifest = ToolFiles.ReadJson<GameManifest>(
            Path.Combine(root, "content", "game", "game-manifest.json"));
        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            throw new InvalidOperationException("Create the game product before adding native Godot nodes.");
        }

        var namespaceName = $"{manifest.RootNamespace}.Nodes";
        var sourcePath = ProductLayout.GetSourcePath(root, manifest, "Nodes", className + ".cs");
        var scenePath = Path.Combine(root, "scene", "nodes", id + ".tscn");
        if (File.Exists(sourcePath) || File.Exists(scenePath))
        {
            throw new IOException("The requested native node source or scene already exists.");
        }

        ToolFiles.WriteText(sourcePath,
            $$"""
            using Godot;
            using LX.Core.Lifetime;
            using LX.Runtime;

            namespace {{namespaceName}};

            public partial class {{className}} : {{godotBase}}, ILXContextReceiver
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
            """ + "\n");

        var resourceScriptPath = ProductLayout.GetResourcePath(manifest, "Nodes", className + ".cs");
        ToolFiles.WriteText(scenePath,
            $$"""
            [gd_scene load_steps=2 format=3]

            [ext_resource type="Script" path="{{resourceScriptPath}}" id="1_node"]

            [node name="{{className}}" type="{{godotBase}}"]
            script = ExtResource("1_node")
            """ + "\n");

        Console.WriteLine($"created native node '{id}' ({className} : {godotBase})");
        return 0;
    }
}
