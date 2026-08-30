using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LXFramework.Tools;

internal static partial class ProductSmokeRunner
{
    public static async Task<int> RunAsync(
        string root,
        IReadOnlyList<string> arguments)
    {
        if (arguments.Count > 1)
        {
            Console.Error.WriteLine("smoke product usage: lx smoke product [id|all]");
            return 2;
        }

        var executable = GodotLocator.Find(root, preferConsole: true);
        if (executable is null)
        {
            Console.Error.WriteLine("smoke product: Godot .NET was not found. Run 'lx doctor'.");
            return 2;
        }
        var game = ToolFiles.ReadJson<GameManifest>(
            Path.Combine(root, "content", "game", "game-manifest.json"));
        GameGenerator.Validate(root, game);
        var productSmokes = game.GetProductSmokes();
        var selectedId = arguments.Count == 0 ? "all" : arguments[0];
        var selected = string.Equals(selectedId, "all", StringComparison.OrdinalIgnoreCase)
            ? productSmokes.ToArray()
            : productSmokes
                .Where(smoke => string.Equals(smoke.Id, selectedId, StringComparison.Ordinal))
                .ToArray();
        if (selected.Length == 0 && !string.Equals(selectedId, "all", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"smoke product: unknown id '{selectedId}'. Available: " +
                string.Join(", ", productSmokes.Select(smoke => smoke.Id)));
            return 2;
        }

        SmokeCheck? preparation = null;
        if (!File.Exists(Path.Combine(root, ".godot", "uid_cache.bin")))
        {
            preparation = await GodotSmoke.RunCheckAsync(
                executable,
                root,
                "editor-import",
                ["--headless", "--editor", "--quit"]);
        }
        var results = new List<ProductSmokeResult>();
        if (preparation?.Success != false)
        {
            foreach (var smoke in selected)
            {
                results.Add(await RunScenarioAsync(executable, root, smoke));
            }
        }
        var report = new ProductSmokeReport(
            "lx.product-smoke-report",
            2,
            DateTimeOffset.UtcNow,
            game.Name,
            preparation?.Success != false &&
            results.Count == selected.Length &&
            results.All(result => result.Success),
            preparation,
            results);
        var output = Path.Combine(root, ".lx", "product-smoke.json");
        ToolFiles.WriteJson(output, report);

        if (preparation is not null)
        {
            Console.WriteLine(
                $"product:editor-import {(preparation.Success ? "passed" : "failed")} " +
                $"(exit {preparation.ExitCode})");
            foreach (var error in preparation.Errors)
            {
                Console.Error.WriteLine($"smoke product: editor-import: {error}");
            }
        }
        if (selected.Length == 0 && preparation?.Success != false)
        {
            Console.WriteLine("product-smoke        skipped (no product smoke scenarios)");
        }
        foreach (var result in results)
        {
            Console.WriteLine($"product:{result.Id,-12} {(result.Success ? "passed" : "failed")} (exit {result.ExitCode})");
            if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
            {
                Console.Error.WriteLine($"smoke product: {result.Id}: {result.Error}");
            }
        }
        Console.WriteLine($"report               {ToolFiles.Relative(root, output)}");
        return report.Success ? 0 : 1;
    }

    private static async Task<ProductSmokeResult> RunScenarioAsync(
        string executable,
        string root,
        ProductSmokeManifestEntry smoke)
    {
        var logDirectory = Path.Combine(root, ".lx", "product-smoke");
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, smoke.Id + ".log");
        if (File.Exists(logPath))
        {
            File.Delete(logPath);
        }

        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
                 {
                     "--path", root,
                     "--headless",
                     "--audio-driver", "Dummy",
                     "--fixed-fps", "60",
                     "--log-file", logPath,
                     "--",
                     smoke.Argument,
                 })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ??
                            throw new InvalidOperationException("Failed to start Godot for product smoke.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var timedOut = false;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(smoke.TimeoutSeconds)))
        {
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                timedOut = true;
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }

        var log = File.Exists(logPath) ? await File.ReadAllTextAsync(logPath) : string.Empty;
        var evidence = string.Join(Environment.NewLine, await stdoutTask, await stderrTask, log);
        var engineErrors = evidence
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => AnsiEscapeRegex().Replace(line, string.Empty))
            .Where(line => line.StartsWith("ERROR:", StringComparison.Ordinal) ||
                           line.StartsWith("SCRIPT ERROR:", StringComparison.Ordinal) ||
                           line.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var markerFound = evidence.Contains(smoke.SuccessMarker, StringComparison.Ordinal);
        var success = !timedOut && process.ExitCode == 0 && markerFound && engineErrors.Length == 0;
        var errors = new List<string>();
        if (timedOut)
        {
            errors.Add($"Exceeded timeout of {smoke.TimeoutSeconds} seconds.");
        }
        if (!markerFound)
        {
            errors.Add($"Expected marker '{smoke.SuccessMarker}' was not emitted.");
        }
        errors.AddRange(engineErrors);
        if (!timedOut && process.ExitCode != 0)
        {
            errors.Add($"Godot exited with code {process.ExitCode}.");
        }
        return new ProductSmokeResult(
            smoke.Id,
            success,
            timedOut ? -1 : process.ExitCode,
            smoke.Argument,
            smoke.SuccessMarker,
            ToolFiles.Relative(root, logPath),
            errors.Count == 0 ? null : string.Join(" ", errors));
    }

    [GeneratedRegex("\\x1B(?:[@-Z\\\\-_]|\\[[0-?]*[ -/]*[@-~])", RegexOptions.CultureInvariant)]
    private static partial Regex AnsiEscapeRegex();
}

internal sealed record ProductSmokeReport(
    string Schema,
    int SchemaVersion,
    DateTimeOffset ValidatedAtUtc,
    string Product,
    bool Success,
    SmokeCheck? Preparation,
    IReadOnlyList<ProductSmokeResult> Scenarios);

internal sealed record ProductSmokeResult(
    string Id,
    bool Success,
    int ExitCode,
    string Argument,
    string SuccessMarker,
    string LogPath,
    string? Error);
