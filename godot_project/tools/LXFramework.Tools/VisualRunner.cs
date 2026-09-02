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
        var affected = arguments.Count > 1 &&
                       string.Equals(arguments[1], "affected", StringComparison.OrdinalIgnoreCase);
        if ((!affected && arguments.Count > 2) || (affected && arguments.Count < 3))
        {
            Console.Error.WriteLine(
                "visual usage: lx visual capture|compare|approve [ui_components|product|target-id|affected <changed-path> ...]");
            return 2;
        }
        var requestedTarget = arguments.Count > 1 ? arguments[1] : "ui_components";
        if (affected && arguments.Skip(2).Any(path => !ProductSmokeImpact.IsValidChangedPath(path)))
        {
            Console.Error.WriteLine(
                "visual affected paths must stay inside the workspace and cannot contain '.' or '..' segments.");
            return 2;
        }

        var targets = ResolveTargets(
            root,
            requestedTarget,
            affected ? arguments.Skip(2).ToArray() : []);
        if (targets is null)
        {
            return 2;
        }
        if (targets.Count == 0)
        {
            Console.WriteLine("visual product      skipped (no product visual targets)");
            return 0;
        }

        var executable = GodotLocator.Find(root, preferConsole: true);
        if (executable is null)
        {
            Console.Error.WriteLine("visual: Godot .NET was not found. Run 'lx doctor'.");
            return 2;
        }

        var success = true;
        foreach (var target in targets)
        {
            success &= await RunTargetAsync(root, executable, mode, target);
        }
        return success ? 0 : 1;
    }

    private static async Task<bool> RunTargetAsync(
        string root,
        string executable,
        string mode,
        ResolvedVisualTarget target)
    {
        var visualRoot = Path.Combine(root, ".lx", "visual");
        var actual = Path.Combine(visualRoot, "actual", target.Id + ".png");
        var diff = Path.Combine(visualRoot, "diff", target.Id + ".png");
        var report = Path.Combine(visualRoot, target.Id + ".json");
        var baseline = Path.GetFullPath(Path.Combine(root, target.BaselinePath.Replace('/', Path.DirectorySeparatorChar)));
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
        var processArguments = new List<string>
        {
            "--path", root,
        };
        if (target.CaptureMode == "SemanticControl")
        {
            processArguments.Add("--headless");
        }
        processArguments.AddRange([
            "--audio-driver", "Dummy",
            "--quit-after", "120",
            "--",
            $"--lx-visual-mode={runtimeMode}",
            $"--lx-visual-capture-mode={target.CaptureMode}",
            $"--lx-visual-target={target.Id}",
            $"--lx-visual-scene={target.ScenePath}",
            $"--lx-visual-width={target.Width}",
            $"--lx-visual-height={target.Height}",
            $"--lx-visual-ready-frames={target.ReadyFrames}",
            $"--lx-visual-pixel-tolerance={target.PixelTolerance.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"--lx-visual-max-changed-ratio={target.MaxChangedPixelRatio.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"--lx-visual-actual={actual}",
            $"--lx-visual-baseline={baseline}",
            $"--lx-visual-diff={diff}",
            $"--lx-visual-report={report}",
        ]);
        if (target.Pointer is { } pointer)
        {
            processArguments.Add(
                $"--lx-visual-pointer={pointer.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                pointer.Y.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        foreach (var argument in processArguments)
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
            return true;
        }

        var success = process.ExitCode == 0 && output.Contains("LX_VISUAL_PASS", StringComparison.Ordinal);
        Console.WriteLine($"visual {mode,-12} {(success ? "passed" : "failed")}");
        Console.WriteLine($"target               {target.Id}");
        Console.WriteLine($"actual               {ToolFiles.Relative(root, actual)}");
        Console.WriteLine($"report               {ToolFiles.Relative(root, report)}");
        if (!success)
        {
            Console.Error.WriteLine(output.Trim());
        }
        return success;
    }

    private static IReadOnlyList<ResolvedVisualTarget>? ResolveTargets(
        string root,
        string requested,
        IReadOnlyList<string> changedPaths)
    {
        if (string.Equals(requested, "ui_components", StringComparison.Ordinal))
        {
            return
            [
                new ResolvedVisualTarget(
                    "ui_components",
                    "res://scene/ui/examples/ui_components_showcase.tscn",
                    "tests/Visual/Baselines/ui_components.png",
                    1280,
                    720,
                    "SemanticControl",
                    4,
                    0,
                    0,
                    null),
            ];
        }
        if (string.Equals(requested, "rendered_probe", StringComparison.Ordinal))
        {
            return
            [
                new ResolvedVisualTarget(
                    "rendered_probe",
                    "res://scene/ui/examples/ui_components_showcase.tscn",
                    "tests/Visual/Baselines/rendered_probe.png",
                    1280,
                    720,
                    "RenderedViewport",
                    4,
                    0.01f,
                    0.001f,
                    new VisualPointerManifestEntry { X = 640, Y = 360 }),
            ];
        }

        var game = ToolFiles.ReadJson<GameManifest>(
            Path.Combine(root, "content", "game", "game-manifest.json"));
        GameGenerator.Validate(root, game);
        var productTargets = game.VisualTargets.Select(target => new ResolvedVisualTarget(
            target.Id,
            target.ScenePath,
            target.BaselinePath,
            target.Width,
            target.Height,
            target.CaptureMode,
            target.ReadyFrames,
            target.PixelTolerance,
            target.MaxChangedPixelRatio,
            target.Pointer)).ToArray();
        if (string.Equals(requested, "affected", StringComparison.OrdinalIgnoreCase))
        {
            var selectedIds = ProductSmokeImpact.SelectAffectedVisuals(game.VisualTargets, changedPaths)
                .Select(target => target.Id)
                .ToHashSet(StringComparer.Ordinal);
            return productTargets.Where(target => selectedIds.Contains(target.Id)).ToArray();
        }
        if (string.Equals(requested, "product", StringComparison.Ordinal))
        {
            return productTargets;
        }
        var selected = productTargets
            .Where(target => string.Equals(target.Id, requested, StringComparison.Ordinal))
            .ToArray();
        if (selected.Length != 0)
        {
            return selected;
        }

        Console.Error.WriteLine(
            $"visual: unknown target '{requested}'. Available: ui_components, rendered_probe, product" +
            (productTargets.Length == 0 ? string.Empty : ", " + string.Join(", ", productTargets.Select(target => target.Id))));
        return null;
    }

    private sealed record ResolvedVisualTarget(
        string Id,
        string ScenePath,
        string BaselinePath,
        int Width,
        int Height,
        string CaptureMode,
        int ReadyFrames,
        float PixelTolerance,
        float MaxChangedPixelRatio,
        VisualPointerManifestEntry? Pointer);
}
