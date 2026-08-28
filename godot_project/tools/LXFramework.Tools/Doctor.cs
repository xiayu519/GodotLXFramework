using System.Diagnostics;

namespace LXFramework.Tools;

internal static class Doctor
{
    public static int Run(string root)
    {
        var godot = GodotLocator.Find(root, preferConsole: true);
        var workspaceRoot = Directory.GetParent(root)?.FullName ?? root;
        var exportTemplates = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Godot",
            "export_templates",
            "4.6.3.stable");
        var checks = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["projectRoot"] = root,
            ["dotnetSdk"] = ReadProcess("dotnet", "--version"),
            ["git"] = ReadProcess("git", "--version"),
            ["godotDotnet"] = godot,
            ["projectFile"] = File.Exists(Path.Combine(root, "project.godot")) ? "ok" : null,
            ["csharpProject"] = File.Exists(Path.Combine(root, "LXFramework.csproj")) ? "ok" : null,
            ["editorPlugin"] = File.Exists(Path.Combine(root, "addons", "lx_tools", "plugin.cfg"))
                ? "ok"
                : null,
            ["lubanToolchain"] = File.Exists(Path.Combine(workspaceRoot, "game_design", "toolchain.json"))
                ? "pinned"
                : null,
            ["visualBaseline"] = File.Exists(Path.Combine(
                root, "tests", "Visual", "Baselines", "ui_components.png"))
                ? "ok"
                : null,
            // Export templates are intentionally optional for ordinary development and validation.
            ["exportTemplates"] = Directory.Exists(exportTemplates)
                ? "installed"
                : "missing (optional; required by 'lx export windows')",
        };
        var missing = checks.Where(pair => pair.Value is null).Select(pair => pair.Key).ToArray();
        var report = new DoctorReport(DateTimeOffset.UtcNow, missing.Length == 0, checks, missing);
        var output = Path.Combine(root, ".lx", "doctor.json");
        ToolFiles.WriteJson(output, report);

        foreach (var check in checks)
        {
            Console.WriteLine($"{check.Key,-16} {check.Value ?? "missing"}");
        }

        Console.WriteLine($"report           {ToolFiles.Relative(root, output)}");
        return missing.Length == 0 ? 0 : 1;
    }

    private static string? ReadProcess(string fileName, string argument)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = argument,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                return null;
            }

            process.WaitForExit(5000);
            return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd().Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed record DoctorReport(
    DateTimeOffset CheckedAtUtc,
    bool Ready,
    IReadOnlyDictionary<string, string?> Checks,
    IReadOnlyList<string> Missing);
