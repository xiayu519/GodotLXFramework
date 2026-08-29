namespace LXFramework.Tools;

internal static class Scaffolder
{
    public static int Run(string root, IReadOnlyList<string> args)
    {
        if (args.Count == 0 ||
            args[0] is "--help" or "-h" or "help")
        {
            return Usage(success: args.Count > 0);
        }

        if (args.Count == 2 && args[1] is "--help" or "-h" or "help")
        {
            return Usage(success: true, topic: args[0]);
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
            _ => Usage(success: false),
        };
    }

    private static int Usage(bool success, string? topic = null)
    {
        var writer = success ? Console.Out : Console.Error;
        if (topic is not null)
        {
            var usage = topic.ToLowerInvariant() switch
            {
                "game" => "lx create game <Name>",
                "world" => "lx create world <Name> [snake_case_id]",
                "feature" => "lx create feature <Name> [snake_case_id]",
                "screen" => "lx create screen <ClassName> [snake_case_id]",
                "input" => "lx create input <Name> <godot_action> [DefaultPhysicalKey]",
                "res" => "lx create res <snake_case_id> <ResourceType> <res://path> [Transient|Cached|Resident] [snake_case_group]",
                "content" => "lx create content <Name> [snake_case_table]",
                "node" => "lx create node <Class> <GodotBase> [snake_case_id]",
                _ => null,
            };
            if (usage is null)
            {
                writer.WriteLine($"Unknown create topic '{topic}'.");
                return 2;
            }

            writer.WriteLine($"Usage: {usage}");
            return 0;
        }

        writer.WriteLine(
            """
            Usage:
              lx create game <Name>
              lx create world <Name> [snake_case_id]
              lx create feature <Name> [snake_case_id]
              lx create screen <ClassName> [snake_case_id]
              lx create input <Name> <godot_action> [DefaultPhysicalKey]
              lx create res <snake_case_id> <ResourceType> <res://path> [Transient|Cached|Resident] [snake_case_group]
              lx create content <Name> [snake_case_table]
              lx create node <Class> <GodotBase> [snake_case_id]

            The optional ID is the stable manifest ID and scene filename. The
            command updates the upstream manifest and generated typed catalogs.
            A successful create is sufficient evidence of its own outputs; run
            lx check once for all changed paths, then lx validate.
            """);
        return success ? 0 : 2;
    }
}
