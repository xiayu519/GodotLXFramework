namespace LXFramework.Tools;

internal static class Scaffolder
{
    public static int Run(string root, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return Usage();
        }

        return args[0].ToLowerInvariant() switch
        {
            "game" => GameScaffolder.Run(root, args),
            "world" => WorldScaffolder.Run(root, args),
            "input" => InputScaffolder.Run(root, args),
            "res" => ResScaffolder.Run(root, args),
            "screen" => ScreenScaffolder.Run(root, args),
            "content" => ContentScaffolder.Run(root, args),
            "feature" => FeatureScaffolder.Run(root, args),
            "node" => NodeScaffolder.Run(root, args),
            _ => Usage(),
        };
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: lx create game|world|input|res|screen|content|feature|node ...");
        return 2;
    }
}
