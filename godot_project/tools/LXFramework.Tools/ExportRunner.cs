using System.Diagnostics;
using System.Security.Cryptography;

namespace LXFramework.Tools;

internal static class ExportRunner
{
    public static async Task<int> RunAsync(string root, IReadOnlyList<string> arguments)
    {
        var target = arguments.Count == 0 ? "windows" : arguments[0].ToLowerInvariant();
        if (target != "windows")
        {
            Console.Error.WriteLine("export: only the 'windows' target is currently supported.");
            return 2;
        }
        var executable = GodotLocator.Find(root, preferConsole: true);
        if (executable is null)
        {
            Console.Error.WriteLine("export: Godot .NET was not found. Run 'lx doctor'.");
            return 2;
        }

        var version = FileVersionInfo.GetVersionInfo(executable).ProductVersion ?? "unknown";
        var templateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Godot",
            "export_templates");
        var hasTemplates = Directory.Exists(templateRoot) &&
                           Directory.EnumerateDirectories(templateRoot)
                               .Any(directory => Path.GetFileName(directory)
                                   .StartsWith("4.6.3", StringComparison.OrdinalIgnoreCase));
        if (!hasTemplates)
        {
            var missing = new ExportReport(
                "lx.export-report",
                1,
                "Windows Desktop",
                false,
                false,
                version,
                null,
                [],
                "Godot 4.6.3 export templates are not installed under the user profile.");
            WriteReport(root, missing);
            Console.Error.WriteLine(
                "export: Godot 4.6.3 export templates are missing. " +
                "Install the official templates from Godot Editor > Manage Export Templates.");
            return 2;
        }

        var outputDirectory = Path.Combine(root, ".lx", "artifacts", "windows");
        Directory.CreateDirectory(outputDirectory);
        var output = Path.Combine(outputDirectory, "LXFramework.exe");
        var buildExitCode = await RunProcessAsync(
            "dotnet",
            root,
            ["build", "LXFramework.sln", "-c", "Release", "--nologo", "--verbosity", "quiet"]);
        if (buildExitCode.ExitCode != 0)
        {
            Console.Error.WriteLine(buildExitCode.Output);
            return 1;
        }

        var exportResult = await RunProcessAsync(
            executable,
            root,
            [
                "--path", root,
                "--headless",
                "--audio-driver", "Dummy",
                "--export-release", "Windows Desktop", output,
            ]);
        if (exportResult.ExitCode != 0 || !File.Exists(output))
        {
            var failed = new ExportReport(
                "lx.export-report",
                1,
                "Windows Desktop",
                false,
                true,
                version,
                output,
                [],
                exportResult.Output.Trim());
            WriteReport(root, failed);
            Console.Error.WriteLine(exportResult.Output);
            return 1;
        }

        var smokeExecutable = File.Exists(Path.ChangeExtension(output, ".console.exe"))
            ? Path.ChangeExtension(output, ".console.exe")
            : output;
        var smoke = await RunProcessAsync(
            smokeExecutable,
            outputDirectory,
            ["--headless", "--audio-driver", "Dummy", "--quit-after", "120", "--", "--lx-export-smoke"]);
        var smokePassed = smoke.ExitCode == 0 &&
                          smoke.Output.Contains("LX_FRAMEWORK_SMOKE_PASS", StringComparison.Ordinal);
        var files = Directory.EnumerateFiles(outputDirectory)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new ExportedFile(
                Path.GetRelativePath(root, path).Replace('\\', '/'),
                new FileInfo(path).Length,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))))
            .ToArray();
        var report = new ExportReport(
            "lx.export-report",
            1,
            "Windows Desktop",
            smokePassed,
            true,
            version,
            output,
            files,
            smokePassed ? null : smoke.Output.Trim());
        WriteReport(root, report);
        Console.WriteLine($"export windows       {(smokePassed ? "passed" : "failed")}");
        Console.WriteLine("report               .lx/export.json");
        return smokePassed ? 0 : 1;
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments)
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
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            string.Join(Environment.NewLine, await stdoutTask, await stderrTask));
    }

    private static void WriteReport(string root, ExportReport report) =>
        ToolFiles.WriteJson(Path.Combine(root, ".lx", "export.json"), report);

    private sealed record ProcessResult(int ExitCode, string Output);
}

internal sealed record ExportedFile(string Path, long SizeBytes, string Sha256);

internal sealed record ExportReport(
    string Schema,
    int SchemaVersion,
    string Preset,
    bool Success,
    bool TemplatesInstalled,
    string GodotVersion,
    string? Executable,
    IReadOnlyList<ExportedFile> Files,
    string? Error);
