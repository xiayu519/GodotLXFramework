using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LXFramework.Tools;

internal static partial class GodotSmoke
{
    public static async Task<int> RunAsync(string root, IReadOnlyList<string>? arguments = null)
    {
        arguments ??= [];
        if (arguments.Count > 0)
        {
            if (string.Equals(arguments[0], "product", StringComparison.OrdinalIgnoreCase))
            {
                return await ProductSmokeRunner.RunAsync(root, arguments.Skip(1).ToArray());
            }
            Console.Error.WriteLine(
                "smoke usage: lx smoke | lx smoke product [id|all|affected <changed-path> ...]");
            return 2;
        }
        var executable = GodotLocator.Find(root, preferConsole: true);
        if (executable is null)
        {
            Console.Error.WriteLine("smoke: Godot .NET was not found. Run 'lx doctor' for setup details.");
            return 2;
        }

        var expectedFrameworkMarkers = new List<string>
        {
            "LX_CONTEXT_INJECTION_PASS",
            "LX_CONTEXT_INJECTION_ORDER_PASS",
            "LX_RUNTIME_GUARDS_PASS",
            "LX_RESOURCE_LEASE_LIFECYCLE_PASS",
            "LX_RESOURCE_SHARED_CACHE_SAFETY_PASS",
            "LX_RESOURCE_INFLIGHT_OBSERVER_ISOLATION_PASS",
            "LX_RESOURCE_BATCH_POLICY_PASS",
            "LX_RESOURCE_PRELOAD_PLAN_PASS",
            "LX_DYNAMIC_TEXTURE_ATLAS_LIFECYCLE_PASS",
            "LX_SCENE_PRELOAD_PROGRESS_PASS",
            "LX_INPUT_CONTEXT_PASS",
            "LX_INPUT_DEFAULT_BINDING_RESTORE_PASS",
            "LX_LOCALIZATION_QA_PASS",
            "LX_AUDIO_GROUP_POLICY_PASS",
            "LX_AUDIO_FADE_PASS",
            "LX_FEATURE_LIFECYCLE_PASS",
            "LX_PACKED_SCENE_INSTANCE_LIFECYCLE_PASS",
            "LX_PACKED_SCENE_POOL_PASS",
            "LX_WORLD_CHUNK_STREAMING_PROGRESS_PASS",
            "LX_WORLD_EVENT_TRIGGER_PASS",
            "LX_UI_COVER_POLICY_PASS",
            "LX_UI_RESULT_TRANSITION_PASS",
            "LX_UI_FADE_TRANSITION_PASS",
            "LX_UI_COMPONENT_LIFECYCLE_PASS",
            "LX_UI_LIFECYCLE_PASS",
            "LX_ACTIONS_LIFETIME_PASS",
            "LX_VIDEO_SEQUENCE_CONTRACT_PASS",
            "LX_RUNTIME_DIAGNOSTICS_PASS",
            "LX_RUNTIME_BRIDGE_PASS",
            "LX_FRAMEWORK_SMOKE_PASS",
            "LX_ASYNC_SHUTDOWN_PASS",
        };
        var game = ToolFiles.ReadJson<GameManifest>(
            Path.Combine(root, "content", "game", "game-manifest.json"));
        if (!string.IsNullOrWhiteSpace(game.Name) && ProductEmitsLubanMarker(root, game))
        {
            expectedFrameworkMarkers.Add("LX_LUBAN_BINARY_TABLE_PASS");
        }

        var checks = new[]
        {
            await RunCheckAsync(executable, root, "editor-import", ["--headless", "--editor", "--quit"]),
            await RunCheckAsync(
                executable,
                root,
                "framework-bootstrap",
                ["--headless", "--quit-after", "120", "--", "--lx-framework-smoke"],
                expectedFrameworkMarkers),
        };
        var report = new SmokeReport(
            DateTimeOffset.UtcNow,
            executable,
            checks.All(check => check.Success),
            checks);
        var output = Path.Combine(root, ".lx", "smoke.json");
        ToolFiles.WriteJson(output, report);

        foreach (var check in checks)
        {
            Console.WriteLine($"{check.Name,-20} {(check.Success ? "passed" : "failed")} (exit {check.ExitCode})");
            foreach (var error in check.Errors)
            {
                Console.Error.WriteLine($"smoke: {check.Name}: {error}");
            }
        }

        Console.WriteLine($"report               {ToolFiles.Relative(root, output)}");
        return report.Success ? 0 : 1;
    }

    private static bool ProductEmitsLubanMarker(string root, GameManifest game)
    {
        var sourceRoot = ProductLayout.GetSourceDirectory(root, game);
        return Directory.Exists(sourceRoot) &&
               Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                   .Where(path => !path.Split(Path.DirectorySeparatorChar)
                       .Any(segment => segment is "bin" or "obj" or "Generated"))
                   .Any(path => File.ReadAllText(path).Contains(
                       "LX_LUBAN_BINARY_TABLE_PASS",
                       StringComparison.Ordinal));
    }

    internal static async Task<SmokeCheck> RunCheckAsync(
        string executable,
        string root,
        string name,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string>? expectedMarkers = null)
    {
        var normalizedArguments = arguments.ToList();
        if (!normalizedArguments.Contains("--headless", StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Godot smoke '{name}' must run with --headless; visible automated tests are forbidden.");
        }

        if (!normalizedArguments.Contains("--audio-driver", StringComparer.Ordinal))
        {
            var separator = normalizedArguments.IndexOf("--");
            var insertAt = separator < 0 ? normalizedArguments.Count : separator;
            normalizedArguments.Insert(insertAt, "--audio-driver");
            normalizedArguments.Insert(insertAt + 1, "Dummy");
        }

        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("--path");
        start.ArgumentList.Add(root);
        foreach (var argument in normalizedArguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ??
            throw new InvalidOperationException("Failed to start Godot for smoke validation.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var combined = string.Join('\n', await stdoutTask, await stderrTask);
        var errors = combined
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => AnsiEscapeRegex().Replace(line, string.Empty))
            .Where(IsEngineError)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var scenarios = (expectedMarkers ?? [])
            .Select(marker => new SmokeScenario(marker, combined.Contains(marker, StringComparison.Ordinal)))
            .ToArray();
        foreach (var scenario in scenarios)
        {
            if (!scenario.Success)
            {
                errors.Add($"Expected runtime marker '{scenario.Marker}' was not emitted.");
            }
        }

        return new SmokeCheck(
            name,
            process.ExitCode,
            process.ExitCode == 0 && errors.Count == 0,
            errors,
            scenarios);
    }

    private static bool IsEngineError(string line) =>
        line.StartsWith("ERROR:", StringComparison.Ordinal) ||
        line.StartsWith("SCRIPT ERROR:", StringComparison.Ordinal) ||
        line.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("\\x1B(?:[@-Z\\\\-_]|\\[[0-?]*[ -/]*[@-~])", RegexOptions.CultureInvariant)]
    private static partial Regex AnsiEscapeRegex();
}

internal sealed record SmokeCheck(
    string Name,
    int ExitCode,
    bool Success,
    IReadOnlyList<string> Errors,
    IReadOnlyList<SmokeScenario> Scenarios);

internal sealed record SmokeScenario(string Marker, bool Success);

internal sealed record SmokeReport(
    DateTimeOffset ValidatedAtUtc,
    string GodotExecutable,
    bool Success,
    IReadOnlyList<SmokeCheck> Checks);
