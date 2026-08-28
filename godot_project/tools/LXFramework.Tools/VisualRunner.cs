using System.Diagnostics;

namespace LXFramework.Tools;

internal static class VisualRunner
{
    public static async Task<int> RunAsync(string root, IReadOnlyList<string> arguments)
    {
        var mode = arguments.Count == 0 ? "compare" : arguments[0].ToLowerInvariant();
        if (mode is not ("capture" or "compare" or "approve"))
        {
            Console.Error.WriteLine("visual: expected capture, compare, or approve.");
            return 2;
        }
        var target = arguments.Count > 1 ? arguments[1] : "ui_components";
        if (!string.Equals(target, "ui_components", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"visual: unknown target '{target}'.");
            return 2;
        }

        var executable = GodotLocator.Find(root, preferConsole: true);
        if (executable is null)
        {
            Console.Error.WriteLine("visual: Godot .NET was not found. Run 'lx doctor'.");
            return 2;
        }

        var visualRoot = Path.Combine(root, ".lx", "visual");
        var actual = Path.Combine(visualRoot, "actual", target + ".png");
        var diff = Path.Combine(visualRoot, "diff", target + ".png");
        var report = Path.Combine(visualRoot, target + ".json");
        var baseline = Path.Combine(root, "tests", "Visual", "Baselines", target + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(actual)!);

        var runtimeMode = mode == "compare" ? "compare" : "capture";
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
                     "--quit-after", "120",
                     "--",
                     $"--lx-visual-mode={runtimeMode}",
                     $"--lx-visual-actual={actual}",
                     $"--lx-visual-baseline={baseline}",
                     $"--lx-visual-diff={diff}",
                     $"--lx-visual-report={report}",
                 })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ??
            throw new InvalidOperationException("Failed to start Godot visual runner.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = string.Join(Environment.NewLine, await stdoutTask, await stderrTask);
        if (mode == "approve" && process.ExitCode == 0 && File.Exists(actual))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baseline)!);
            File.Copy(actual, baseline, overwrite: true);
            Console.WriteLine($"visual approved       {ToolFiles.Relative(root, baseline)}");
            return 0;
        }

        var successMarker = runtimeMode == "capture" ? "LX_VISUAL_PASS" : "LX_VISUAL_PASS";
        var success = process.ExitCode == 0 && output.Contains(successMarker, StringComparison.Ordinal);
        Console.WriteLine($"visual {mode,-12} {(success ? "passed" : "failed")}");
        Console.WriteLine($"actual               {ToolFiles.Relative(root, actual)}");
        Console.WriteLine($"report               {ToolFiles.Relative(root, report)}");
        if (!success)
        {
            Console.Error.WriteLine(output.Trim());
        }
        return success ? 0 : 1;
    }
}
