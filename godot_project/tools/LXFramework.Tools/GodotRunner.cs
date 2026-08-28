using System.Diagnostics;

namespace LXFramework.Tools;

internal static class GodotRunner
{
    public static async Task<int> RunAsync(string root, IReadOnlyList<string> args)
    {
        var headless = args.Any(argument =>
            string.Equals(argument, "--headless", StringComparison.OrdinalIgnoreCase));
        var executable = GodotLocator.Find(root, preferConsole: headless);
        if (executable is null)
        {
            Console.Error.WriteLine(
                "Godot .NET editor was not found. Set LX_GODOT to the full executable path.");
            return 2;
        }

        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            WorkingDirectory = root,
        };
        start.ArgumentList.Add("--path");
        start.ArgumentList.Add(root);
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ??
            throw new InvalidOperationException("Failed to start Godot.");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
