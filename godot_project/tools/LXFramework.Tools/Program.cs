namespace LXFramework.Tools;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var root = ProjectRoot.Find(Directory.GetCurrentDirectory());
            var command = args.Length == 0 ? "doctor" : args[0].ToLowerInvariant();
            var commandArgs = args.Skip(1).ToArray();

            return command switch
            {
                "doctor" => Doctor.Run(root),
                "inspect" => ProjectInspector.Run(root, commandArgs),
                "generate" => ProjectGenerator.Run(root, commandArgs),
                "validate" => Validator.Run(root),
                "smoke" => await GodotSmoke.RunAsync(root),
                "visual" => await VisualRunner.RunAsync(root, commandArgs),
                "export" => await ExportRunner.RunAsync(root, commandArgs),
                "benchmark" => BenchmarkRunner.Run(root),
                "create" => Scaffolder.Run(root, commandArgs),
                "run" => await GodotRunner.RunAsync(root, commandArgs),
                "help" or "--help" or "-h" => PrintHelp(),
                _ => Unknown(command),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"lx: {exception.Message}");
            return 1;
        }
    }

    private static int PrintHelp()
    {
        Console.WriteLine(
            """
            LXFramework.Tools

              doctor                         检查本地工具链与框架基线。
              inspect [--full]               写入紧凑项目索引；--full 才附完整文件表。
              data                            用固定版本 Luban 生成强类型 C# 与 .bytes 二进制表。
              generate [--verbose]           生成目录与绑定；默认只报告汇总。
              check <changed-path> [...]      执行最小去重检查组合（仅 lx.ps1）。
              validate                       校验事实源、生成结果并完成最终门禁。
              smoke                          用 Godot 无窗口导入并启动框架。
              visual capture|compare|approve 捕获、比较或显式批准 UI 视觉基准。
              export windows                  Release 导出并启动 Windows 产物烟测。
              benchmark                       写入核心性能基线报告。
              create game <Name>             创建产品层与初始世界。
              create world <Name> [id]       创建并注册世界。
              create input <Name> <action>   创建并生成输入动作。
              create res <id> <type> <path>  注册资源并生成类型化引用。
              create screen <Class> [id]     创建 Godot UI 页面。
              create content <Name> [table]  创建类型化 JSON 内容表。
              create feature <Name> [id]     创建 Godot 功能场景。
              create node <Class> <Base> [id] 创建任意 Godot 原生节点与 LX 注入脚手架。
              run [--headless] [godot args]  使用发现的 Godot .NET 编辑器运行。

            所有命令均可在末尾追加 --json，输出 lx.command-report/v1；
            退出码 0 表示成功，1 表示执行失败，2 表示命令或参数用法错误。
            """);
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown LXFramework command '{command}'. Run 'lx help'.");
        return 2;
    }
}
