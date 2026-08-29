using System.Diagnostics;
using System.Security.Cryptography;

namespace LXFramework.Tools;

internal static class ExportRunner
{
    private static readonly ExportTarget[] Targets =
    [
        new("windows", "Windows Desktop", "windows", ".exe"),
    ];

    public static async Task<int> RunAsync(string root, IReadOnlyList<string> arguments)
    {
        var targetId = arguments.Count == 0 ? "windows" : arguments[0].ToLowerInvariant();
        var target = Targets.FirstOrDefault(candidate => candidate.Id == targetId);
        if (target is null)
        {
            Console.Error.WriteLine(
                $"export: target '{targetId}' is not configured. " +
                $"Available targets: {string.Join(", ", Targets.Select(candidate => candidate.Id))}.");
            return 2;
        }

        var executable = GodotLocator.Find(root, preferConsole: true);
        if (executable is null)
        {
            Console.Error.WriteLine("export: Godot .NET was not found. Run 'lx doctor'.");
            return 2;
        }

        var game = ToolFiles.ReadJson<GameManifest>(
            Path.Combine(root, "content", "game", "game-manifest.json"));
        var productName = GetSafeProductName(game.Name);
        var version = FileVersionInfo.GetVersionInfo(executable).ProductVersion ?? "unknown";
        var templateVersion = GodotLocator.RequiredTemplateVersion;
        var templateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Godot",
            "export_templates");
        var hasTemplates = Directory.Exists(templateRoot) &&
                           Directory.EnumerateDirectories(templateRoot)
                               .Any(directory => Path.GetFileName(directory)
                                   .StartsWith(templateVersion, StringComparison.OrdinalIgnoreCase));
        if (!hasTemplates)
        {
            var missing = new ExportReport(
                "lx.export-report",
                2,
                target.Id,
                target.Preset,
                false,
                false,
                false,
                version,
                null,
                [],
                [],
                $"Godot {templateVersion} export templates are not installed under the user profile.");
            WriteReport(root, target.Id, missing);
            Console.Error.WriteLine(
                $"export: Godot {templateVersion} export templates are missing. " +
                "Install the official templates from Godot Editor > Manage Export Templates.");
            return 2;
        }

        var buildResult = await RunProcessAsync(
            "dotnet",
            root,
            ["build", "LXFramework.sln", "-c", "Release", "--nologo", "--verbosity", "quiet"]);
        if (buildResult.ExitCode != 0)
        {
            var failedBuild = new ExportReport(
                "lx.export-report",
                2,
                target.Id,
                target.Preset,
                false,
                true,
                false,
                version,
                null,
                [],
                [],
                buildResult.Output.Trim());
            WriteReport(root, target.Id, failedBuild);
            Console.Error.WriteLine(buildResult.Output);
            return 1;
        }

        var workspaceRoot = Path.GetFullPath(Path.Combine(root, ".."));
        var outputDirectory = PrepareOutputDirectory(workspaceRoot, target.OutputDirectory);
        var output = Path.Combine(outputDirectory, productName + target.ExecutableExtension);
        var exportResult = await RunProcessAsync(
            executable,
            root,
            [
                "--path", root,
                "--headless",
                "--audio-driver", "Dummy",
                "--export-release", target.Preset, output,
            ]);
        if (exportResult.ExitCode != 0 || !File.Exists(output))
        {
            var failed = new ExportReport(
                "lx.export-report",
                2,
                target.Id,
                target.Preset,
                false,
                true,
                false,
                version,
                output,
                CollectFiles(workspaceRoot, outputDirectory),
                [],
                exportResult.Output.Trim());
            WriteReport(root, target.Id, failed);
            Console.Error.WriteLine(exportResult.Output);
            return 1;
        }

        var smokeExecutable = File.Exists(Path.ChangeExtension(output, ".console.exe"))
            ? Path.ChangeExtension(output, ".console.exe")
            : output;
        var smokes = new List<ExportSmokeResult>();
        var frameworkSmoke = await RunSmokeAsync(
            root,
            target,
            smokeExecutable,
            outputDirectory,
            "framework",
            "--lx-export-smoke",
            "LX_FRAMEWORK_SMOKE_PASS",
            30);
        smokes.Add(frameworkSmoke);
        if (frameworkSmoke.Success)
        {
            foreach (var smoke in game.ExportSmokes)
            {
                smokes.Add(await RunSmokeAsync(
                    root,
                    target,
                    smokeExecutable,
                    outputDirectory,
                    smoke.Id,
                    smoke.Argument,
                    smoke.SuccessMarker,
                    smoke.TimeoutSeconds));
            }
        }

        var smokePassed = smokes.All(smoke => smoke.Success) &&
                          smokes.Count == game.ExportSmokes.Count + 1;
        var files = CollectFiles(workspaceRoot, outputDirectory);
        var report = new ExportReport(
            "lx.export-report",
            2,
            target.Id,
            target.Preset,
            smokePassed,
            true,
            true,
            version,
            output,
            files,
            smokes,
            smokePassed
                ? null
                : string.Join(
                    Environment.NewLine,
                    smokes.Where(smoke => !smoke.Success)
                        .Select(smoke => $"{smoke.Id}: {smoke.Error}")));
        WriteReport(root, target.Id, report);
        Console.WriteLine($"export {target.Id,-13} {(smokePassed ? "passed" : "failed")}");
        Console.WriteLine($"package              {Path.GetRelativePath(workspaceRoot, outputDirectory).Replace('\\', '/')}/");
        Console.WriteLine($"report               godot_project/.lx/export/{target.Id}.json");
        return smokePassed ? 0 : 1;
    }

    private static async Task<ExportSmokeResult> RunSmokeAsync(
        string root,
        ExportTarget target,
        string executable,
        string workingDirectory,
        string id,
        string userArgument,
        string successMarker,
        int timeoutSeconds)
    {
        var logDirectory = Path.Combine(root, ".lx", "export-smoke", target.Id);
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, id + ".log");
        if (File.Exists(logPath))
        {
            File.Delete(logPath);
        }

        var result = await RunProcessAsync(
            executable,
            workingDirectory,
            [
                "--headless",
                "--audio-driver", "Dummy",
                "--fixed-fps", "60",
                "--log-file", logPath,
                "--",
                userArgument,
            ],
            TimeSpan.FromSeconds(timeoutSeconds));
        var log = File.Exists(logPath) ? await File.ReadAllTextAsync(logPath) : string.Empty;
        var evidence = string.Join(Environment.NewLine, result.Output, log);
        var passed = result.ExitCode == 0 &&
                     evidence.Contains(successMarker, StringComparison.Ordinal);
        return new ExportSmokeResult(
            id,
            passed,
            result.ExitCode,
            userArgument,
            successMarker,
            ToolFiles.Relative(root, logPath),
            passed ? null : evidence.Trim());
    }

    private static string PrepareOutputDirectory(string workspaceRoot, string targetDirectory)
    {
        var buildRoot = Path.GetFullPath(Path.Combine(workspaceRoot, "build"));
        var outputDirectory = Path.GetFullPath(Path.Combine(buildRoot, targetDirectory));
        var relative = Path.GetRelativePath(buildRoot, outputDirectory);
        if (relative is "." or ".." ||
            Path.IsPathRooted(relative) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Export target directory '{outputDirectory}' is outside the build root.");
        }
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
        Directory.CreateDirectory(outputDirectory);
        return outputDirectory;
    }

    private static ExportedFile[] CollectFiles(string workspaceRoot, string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return [];
        }
        return Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new ExportedFile(
                Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/'),
                new FileInfo(path).Length,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))))
            .ToArray();
    }

    private static string GetSafeProductName(string manifestName)
    {
        var candidate = string.IsNullOrWhiteSpace(manifestName) ? "LXFramework" : manifestName.Trim();
        var invalid = Path.GetInvalidFileNameChars().Concat(['<', '>', '"', ':', '/', '\\', '|', '?', '*'])
            .ToHashSet();
        var sanitized = new string(candidate
                .Where(character => character >= ' ' && !invalid.Contains(character))
                .ToArray())
            .Trim()
            .TrimEnd('.');
        if (sanitized.Length == 0 || IsReservedWindowsFileName(sanitized))
        {
            return "LXFramework";
        }
        return sanitized;
    }

    private static bool IsReservedWindowsFileName(string value)
    {
        var stem = value.Split('.', 2)[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               Enumerable.Range(1, 9).Any(index =>
                   stem.Equals($"COM{index}", StringComparison.OrdinalIgnoreCase) ||
                   stem.Equals($"LPT{index}", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ??
            throw new InvalidOperationException($"Failed to start '{executable}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var timedOut = false;
        if (timeout.HasValue)
        {
            using var timeoutSource = new CancellationTokenSource(timeout.Value);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
            {
                timedOut = true;
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        else
        {
            await process.WaitForExitAsync();
        }
        var output = string.Join(Environment.NewLine, await stdoutTask, await stderrTask);
        if (timedOut)
        {
            output = string.Join(
                Environment.NewLine,
                output,
                $"Process exceeded the smoke timeout of {timeout!.Value.TotalSeconds:0} seconds.");
        }
        return new ProcessResult(
            timedOut ? -1 : process.ExitCode,
            output);
    }

    private static void WriteReport(string root, string targetId, ExportReport report) =>
        ToolFiles.WriteJson(Path.Combine(root, ".lx", "export", targetId + ".json"), report);

    private sealed record ProcessResult(int ExitCode, string Output);

    private sealed record ExportTarget(
        string Id,
        string Preset,
        string OutputDirectory,
        string ExecutableExtension);
}

internal sealed record ExportedFile(string Path, long SizeBytes, string Sha256);

internal sealed record ExportSmokeResult(
    string Id,
    bool Success,
    int ExitCode,
    string Argument,
    string SuccessMarker,
    string LogPath,
    string? Error);

internal sealed record ExportReport(
    string Schema,
    int SchemaVersion,
    string Target,
    string Preset,
    bool Success,
    bool TemplatesInstalled,
    bool Exported,
    string GodotVersion,
    string? Executable,
    IReadOnlyList<ExportedFile> Files,
    IReadOnlyList<ExportSmokeResult> Smokes,
    string? Error);
