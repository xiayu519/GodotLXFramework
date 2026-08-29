using System.Diagnostics;

namespace LXFramework.Tools;

internal static class Doctor
{
    public const string RequiredDotnetSdk = "8.0.416";

    public static int Run(string root, IReadOnlyList<string> arguments)
    {
        if (arguments.Count > 0)
        {
            return MaintenancePlanner.Run(root, "doctor", arguments);
        }

        return Inspect(root);
    }

    private static int Inspect(string root)
    {
        var godot = GodotLocator.Find(root, preferConsole: true);
        var godotVersion = godot is null ? null : GodotLocator.ReadVersion(godot);
        var detectedDotnetSdk = ReadProcess("dotnet", "--version");
        var workspaceRoot = Directory.GetParent(root)?.FullName ?? root;
        var exportTemplates = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Godot",
            "export_templates",
            GodotLocator.RequiredTemplateVersion);
        var checks = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["projectRoot"] = root,
            ["dotnetSdk"] = detectedDotnetSdk == RequiredDotnetSdk ? detectedDotnetSdk : null,
            ["dotnetSdkDetected"] = detectedDotnetSdk ?? "none",
            ["git"] = ReadProcess("git", "--version"),
            ["godotDotnet"] = godot,
            ["godotVersion"] = godotVersion,
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

    internal static string? ReadProcess(string fileName, string argument)
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
