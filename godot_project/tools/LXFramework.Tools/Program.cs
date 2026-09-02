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
                "doctor" => Doctor.Run(root, commandArgs),
                "upgrade" => MaintenancePlanner.Run(root, "upgrade", commandArgs),
                "migrate" => MigrationPlanner.Run(root, commandArgs),
                "inspect" => ProjectInspector.Run(root, commandArgs),
                "capabilities" => CapabilityCatalog.Run(root, commandArgs),
                "generate" => ProjectGenerator.Run(root, commandArgs),
                "validate" => Validator.Run(root, commandArgs),
                "smoke" => await GodotSmoke.RunAsync(root, commandArgs),
                "visual" => await VisualRunner.RunAsync(root, commandArgs),
                "export" => await ExportRunner.RunAsync(root, commandArgs),
                "benchmark" => BenchmarkRunner.Run(root),
                "api" => PublicApiBaseline.Run(root, commandArgs),
                "soak" => await SoakRunner.RunAsync(root, commandArgs),
                "runtime" => RuntimeBridgeClient.Run(root, commandArgs),
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

              doctor [--plan|--apply|--rollback|--recover] 检查环境并事务化修复当前派生状态。
              upgrade --plan|--apply|--rollback|--recover  将产品派生状态升级到当前 LX checkout。
              migrate plan --source <path|ref> 规划旧 LX、其他 Godot 或跨引擎游戏升级/移植/复刻。
              inspect [--full] [--product-coverage] 写入项目索引，并可附产品 LX 服务静态使用图。
              capabilities [id]              写入机器可读能力、前置条件、副作用与验收配方。
              data                            用固定版本 Luban 生成强类型 C# 与 .bytes 二进制表。
              generate [--verbose]           生成目录与绑定；默认只报告汇总。
              check <changed-path> [...]      执行最小去重检查组合（仅 lx.ps1）。
              validate                       校验事实源、生成结果并完成最终门禁。
              smoke                          用 Godot 无窗口导入并启动框架。
              smoke product [id|all|affected <path> ...] 运行指定、全部或受变更路径影响的产品烟测。
              visual capture|compare|approve [target|product] 捕获、比较或显式批准框架/产品视觉基准。
              export <platform>               Release 导出到 build/<platform> 并运行框架与产品烟测；当前支持 windows。
              benchmark                       执行多轮核心性能回归与分配门禁。
              api check|update                 检查或显式更新公开 API 兼容基线。
              soak [cycles]                    重复运行隔离 Godot smoke 并写入稳定性报告。
              runtime status                  查询当前 Editor/Debug 运行会话。
              runtime snapshot [section]      读取当前运行实例的有界结构化快照。
              runtime sample performance [...] 持续采样帧时间、内存与所有权指标，可附性能预算。
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
