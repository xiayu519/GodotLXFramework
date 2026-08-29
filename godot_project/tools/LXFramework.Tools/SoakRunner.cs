using System.Diagnostics;

namespace LXFramework.Tools;

internal static class SoakRunner
{
    public static async Task<int> RunAsync(string root, IReadOnlyList<string> arguments)
    {
        if (arguments.Count > 1 ||
            (arguments.Count == 1 &&
             (!int.TryParse(arguments[0], out var parsed) || parsed is < 1 or > 50)))
        {
            Console.Error.WriteLine("soak usage: lx soak [cycles:1-50]");
            return 2;
        }

        var cycles = arguments.Count == 0 ? 3 : int.Parse(arguments[0]);
        var iterations = new List<SoakIteration>(cycles);
        for (var cycle = 1; cycle <= cycles; cycle++)
        {
            var started = Stopwatch.GetTimestamp();
            var exitCode = await GodotSmoke.RunAsync(root);
            var reportPath = Path.Combine(root, ".lx", "smoke.json");
            var report = ToolFiles.ReadJson<SmokeReport>(reportPath);
            var iteration = new SoakIteration(
                cycle,
                Stopwatch.GetElapsedTime(started),
                exitCode,
                report.Success,
                report.Checks);
            iterations.Add(iteration);
            Console.WriteLine(
                $"soak cycle {cycle}/{cycles}: {(iteration.Success ? "passed" : "failed")} " +
                $"({iteration.Duration.TotalSeconds:0.0}s)");
            if (!iteration.Success)
            {
                break;
            }
        }

        var soak = new SoakReport(
            "lx.soak-report",
            1,
            DateTimeOffset.UtcNow,
            cycles,
            iterations.Count,
            iterations.Count == cycles && iterations.All(iteration => iteration.Success),
            iterations);
        var output = Path.Combine(root, ".lx", "soak.json");
        ToolFiles.WriteJson(output, soak);
        Console.WriteLine($"soak report -> {ToolFiles.Relative(root, output)}");
        return soak.Success ? 0 : 1;
    }
}

internal sealed record SoakIteration(
    int Cycle,
    TimeSpan Duration,
    int ExitCode,
    bool Success,
    IReadOnlyList<SmokeCheck> Checks);

internal sealed record SoakReport(
    string Schema,
    int SchemaVersion,
    DateTimeOffset CompletedAtUtc,
    int RequestedCycles,
    int CompletedCycles,
    bool Success,
    IReadOnlyList<SoakIteration> Iterations);
