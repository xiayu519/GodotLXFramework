namespace LXFramework.Tools;

internal static class GameScaffolder
{
    public static int Run(string root, IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            Console.Error.WriteLine("Usage: lx create game <Name>");
            return 2;
        }

        var gameName = args[1].Trim();
        CodeNames.RequireIdentifier(gameName, nameof(args));
        var manifestPath = Path.Combine(root, "content", "game", "game-manifest.json");
        var manifest = ToolFiles.ReadJson<GameManifest>(manifestPath);
        if (!string.IsNullOrWhiteSpace(manifest.Name) || manifest.Worlds.Count > 0)
        {
            throw new InvalidOperationException("A game product has already been created in this LXFramework workspace.");
        }

        manifest.Name = gameName;
        manifest.RootNamespace = gameName;
        manifest.SourceRoot = $"script/{gameName}";

        var sourcePath = ProductLayout.GetSourcePath(root, manifest, "GameRoot.cs");
        var agentPath = ProductLayout.GetSourcePath(root, manifest, "AGENTS.md");
        var scenePath = Path.Combine(root, "scene", "world", "main_world.tscn");
        var mainScenePath = Path.Combine(root, "scene", "main.tscn");
        var projectPath = Path.Combine(root, "project.godot");
        if (File.Exists(sourcePath) || File.Exists(agentPath) || File.Exists(scenePath))
        {
            throw new IOException("Game product source, AGENTS.md, or main world scene already exists.");
        }
        var mainScene = File.ReadAllText(mainScenePath);
        if (!mainScene.Contains("ShowFrameworkStatus = true", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "scene/main.tscn must expose the clean-baseline ShowFrameworkStatus setting.");
        }
        var project = File.ReadAllText(projectPath);
        if (!project.Contains("config/name=\"LXFramework\"", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "project.godot must retain the clean-baseline LXFramework product name before create game.");
        }

        manifest.InitialWorldId = "main_world";
        manifest.Worlds.Add(new WorldManifestEntry
        {
            Id = "main_world",
            ClassName = "GameRoot",
            Namespace = manifest.RootNamespace,
            ScenePath = "res://scene/world/main_world.tscn",
        });

        ToolFiles.WriteText(sourcePath,
            $$"""
            using LX.Runtime;

            namespace {{manifest.RootNamespace}};

            public partial class GameRoot : LXNode
            {
                protected override void OnLXInitialized()
                {
                }
            }
            """ + "\n");
        ToolFiles.WriteText(agentPath,
            $$"""
            # {{gameName}} 产品层规则

            - 本目录是当前游戏的产品代码与专用工作流，只能依赖 LXFramework 公开 API；框架层禁止反向依赖本目录。
            - 通过注入的 `LX` 上下文调用 `LX.UI`、`LX.Res` 等模块；禁止全局上下文、服务定位器和直接动态 `GD.Load`/`ResourceLoader.Load*`。
            - 编写具体内容前先确定驱动架构；重复剧情、任务、场景、对话和战斗入口由统一事件脚本/数据驱动通用模块，禁止逐内容硬编码。架构契约通过后批量验证全部内容。
            - 新结构使用 `./lx.ps1 create world|feature|screen|content|input|res`；`Generated/` 禁止手改。
            - 产品行为变更后运行相关 `./lx.ps1 check <changed-path> [...]`，交付前运行 `./lx.ps1 validate`。
            """ + "\n");
        var resourceScriptPath = ProductLayout.GetResourcePath(manifest, "GameRoot.cs");
        ToolFiles.WriteText(scenePath,
            $$"""
            [gd_scene load_steps=2 format=3]

            [ext_resource type="Script" path="{{resourceScriptPath}}" id="1_game"]

            [node name="GameRoot" type="Node"]
            script = ExtResource("1_game")
            """ + "\n");
        ToolFiles.WriteText(
            mainScenePath,
            mainScene.Replace(
                "ShowFrameworkStatus = true",
                "ShowFrameworkStatus = false",
                StringComparison.Ordinal));
        ToolFiles.WriteText(
            projectPath,
            project.Replace(
                "config/name=\"LXFramework\"",
                $"config/name=\"{gameName}\"",
                StringComparison.Ordinal));
        ToolFiles.WriteJson(manifestPath, manifest);
        ProjectGenerator.Run(root);
        Console.WriteLine($"created game '{gameName}' with initial world 'main_world'");
        return 0;
    }
}
