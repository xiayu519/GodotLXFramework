namespace LXFramework.Tools;

internal static class GodotLocator
{
    public static string? Find(string? projectRoot = null, bool preferConsole = false)
    {
        foreach (var variable in new[] { "LX_GODOT", "GODOT4", "GODOT" })
        {
            var candidate = System.Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        if (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(projectRoot))
        {
            var absoluteProjectRoot = Path.GetFullPath(projectRoot);
            var workspaceRoot = Directory.GetParent(absoluteProjectRoot)?.FullName;
            var toolsDirectories = new[]
            {
                Path.Combine(absoluteProjectRoot, ".tools"),
                string.IsNullOrWhiteSpace(workspaceRoot)
                    ? string.Empty
                    : Path.Combine(workspaceRoot, ".tools"),
            };
            foreach (var toolsDirectory in toolsDirectories
                         .Where(path => !string.IsNullOrWhiteSpace(path))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(toolsDirectory))
                {
                    continue;
                }

                var localExecutable = FindLocalWindowsExecutable(toolsDirectory, preferConsole);
                if (localExecutable is not null)
                {
                    return localExecutable;
                }
            }
        }

        var pathDirectories = (System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var names = OperatingSystem.IsWindows()
            ? new[] { "godot.exe", "godot4.exe", "Godot_v4.6.3-stable_mono_win64.exe", "Godot_v4.6-stable_mono_win64.exe" }
            : new[] { "godot", "godot4" };
        foreach (var directory in pathDirectories)
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    private static string? FindLocalWindowsExecutable(string toolsDirectory, bool preferConsole)
    {
        var executables = Directory
            .EnumerateFiles(toolsDirectory, "Godot*_mono_*.exe", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("GodotSharp.dll", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var preferredSuffix = preferConsole ? "_console.exe" : ".exe";

        return executables.FirstOrDefault(path =>
                   preferConsole
                       ? path.EndsWith(preferredSuffix, StringComparison.OrdinalIgnoreCase)
                       : !path.EndsWith("_console.exe", StringComparison.OrdinalIgnoreCase))
               ?? executables.FirstOrDefault();
    }
}
