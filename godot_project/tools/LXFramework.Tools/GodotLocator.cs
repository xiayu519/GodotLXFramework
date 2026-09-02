using System.Diagnostics;

namespace LXFramework.Tools;

internal static class GodotLocator
{
    internal const string RequiredVersionPrefix = "4.7.2.stable.mono.";
    internal const string RequiredTemplateVersion = "4.7.2.stable.mono";

    public static string? Find(string? projectRoot = null, bool preferConsole = false)
    {
        foreach (var variable in new[] { "LX_GODOT", "GODOT4", "GODOT" })
        {
            var candidate = System.Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(candidate) &&
                File.Exists(candidate) &&
                IsRequiredVersion(candidate))
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

                var localExecutable = FindLocalWindowsExecutables(toolsDirectory, preferConsole)
                    .FirstOrDefault(IsRequiredVersion);
                if (localExecutable is not null)
                {
                    return localExecutable;
                }
            }

            foreach (var installDirectory in FindKnownWindowsInstallDirectories())
            {
                if (!Directory.Exists(installDirectory))
                {
                    continue;
                }
                var installedExecutable = FindLocalWindowsExecutables(installDirectory, preferConsole)
                    .FirstOrDefault(IsRequiredVersion);
                if (installedExecutable is not null)
                {
                    return installedExecutable;
                }
            }
        }

        var pathDirectories = (System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var names = OperatingSystem.IsWindows()
            ? new[] { "godot.exe", "godot4.exe", "Godot_v4.7.2-stable_mono_win64.exe" }
            : new[] { "godot", "godot4" };
        foreach (var directory in pathDirectories)
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate) && IsRequiredVersion(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    internal static string? ReadVersion(string executable)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(executable).ProductVersion;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsRequiredVersion(string executable) =>
        ReadVersion(executable)?.StartsWith(RequiredVersionPrefix, StringComparison.Ordinal) == true;

    private static IEnumerable<string> FindLocalWindowsExecutables(
        string toolsDirectory,
        bool preferConsole)
    {
        var firstLevel = EnumerateDirectoriesSafely(toolsDirectory).ToArray();
        var searchDirectories = new[] { toolsDirectory }
            .Concat(firstLevel)
            .Concat(firstLevel.SelectMany(EnumerateDirectoriesSafely))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return searchDirectories
            .SelectMany(directory => EnumerateFilesSafely(directory, "Godot*_mono_*.exe"))
            .Where(path => !path.EndsWith("GodotSharp.dll", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(
                path => preferConsole
                    ? path.EndsWith("_console.exe", StringComparison.OrdinalIgnoreCase)
                    : !path.EndsWith("_console.exe", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> FindKnownWindowsInstallDirectories()
    {
        const string versionDirectory = "Godot_v4.7.2-stable_mono_win64";
        var directories = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                {
                    continue;
                }
                directories.Add(Path.Combine(drive.RootDirectory.FullName, "Soft", versionDirectory));
                directories.Add(Path.Combine(drive.RootDirectory.FullName, "Tools", versionDirectory));
                directories.Add(Path.Combine(drive.RootDirectory.FullName, versionDirectory));
            }
            catch (IOException)
            {
                // A drive can disappear between enumeration and inspection.
            }
            catch (UnauthorizedAccessException)
            {
                // Inaccessible drives are not valid tool sources.
            }
        }

        var localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            versionDirectory);
        directories.Add(localPrograms);
        directories.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            versionDirectory));
        return directories.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateDirectoriesSafely(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path).ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> EnumerateFilesSafely(string path, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
