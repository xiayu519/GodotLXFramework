using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LXFramework.Tools;

internal static class VisualRunner
{
    private const string HiddenWindowArgument = "--lx-visual-hidden-window";
    private const string HiddenWindowMarker = "LX_VISUAL_HIDDEN_WINDOW_PASS";

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
        var log = Path.Combine(visualRoot, target.Id + ".log");
        var baseline = Path.GetFullPath(Path.Combine(root, target.BaselinePath.Replace('/', Path.DirectorySeparatorChar)));
        Directory.CreateDirectory(Path.GetDirectoryName(actual)!);
        if (File.Exists(log))
        {
            File.Delete(log);
        }

        var runtimeMode = mode == "compare" ? "compare" : "capture";
        var start = CreateProcessStartInfo(executable, root);
        var processArguments = BuildProcessArguments(
            root,
            runtimeMode,
            target,
            actual,
            baseline,
            diff,
            report,
            log);
        var processResult = await RunVisualProcessAsync(
            start,
            processArguments,
            log,
            target.CaptureMode == "RenderedViewport");
        var output = processResult.Output;
        if (mode == "approve" && processResult.ExitCode == 0 && File.Exists(actual))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baseline)!);
            File.Copy(actual, baseline, overwrite: true);
            Console.WriteLine($"visual approved       {ToolFiles.Relative(root, baseline)}");
            return true;
        }

        var hiddenWindowVerified = target.CaptureMode != "RenderedViewport" ||
                                   output.Contains(HiddenWindowMarker, StringComparison.Ordinal);
        var success = processResult.ExitCode == 0 &&
                      output.Contains("LX_VISUAL_PASS", StringComparison.Ordinal) &&
                      hiddenWindowVerified;
        Console.WriteLine($"visual {mode,-12} {(success ? "passed" : "failed")}");
        Console.WriteLine($"target               {target.Id}");
        Console.WriteLine($"actual               {ToolFiles.Relative(root, actual)}");
        Console.WriteLine($"report               {ToolFiles.Relative(root, report)}");
        if (!success)
        {
            if (!hiddenWindowVerified)
            {
                Console.Error.WriteLine(
                    "visual: rendered capture did not prove that its native window stayed hidden.");
            }
            Console.Error.WriteLine(output.Trim());
        }
        return success;
    }

    internal static string? ValidateProtocol()
    {
        var start = CreateProcessStartInfo("godot", "project");
        if (start.UseShellExecute || !start.CreateNoWindow ||
            start.WindowStyle != ProcessWindowStyle.Hidden)
        {
            return "Visual process startup does not suppress native process windows.";
        }

        var semantic = BuildProcessArguments(
            "project",
            "compare",
            CreateProtocolTarget("SemanticControl"),
            "actual.png",
            "baseline.png",
            "diff.png",
            "report.json",
            "visual.log");
        if (!semantic.Contains("--headless", StringComparer.Ordinal) ||
            semantic.Contains(HiddenWindowArgument, StringComparer.Ordinal))
        {
            return "Semantic visual validation is not strictly headless.";
        }

        var rendered = BuildProcessArguments(
            "project",
            "compare",
            CreateProtocolTarget("RenderedViewport"),
            "actual.png",
            "baseline.png",
            "diff.png",
            "report.json",
            "visual.log");
        if (rendered.Contains("--headless", StringComparer.Ordinal) ||
            !rendered.Contains(HiddenWindowArgument, StringComparer.Ordinal) ||
            !rendered.Contains("--disable-vsync", StringComparer.Ordinal) ||
            !rendered.Contains("--fixed-fps", StringComparer.Ordinal))
        {
            return "Rendered visual validation is not configured for hidden deterministic capture.";
        }
        return null;
    }

    private static ProcessStartInfo CreateProcessStartInfo(string executable, string root) => new()
    {
        FileName = executable,
        WorkingDirectory = root,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden,
    };

    private static List<string> BuildProcessArguments(
        string root,
        string runtimeMode,
        ResolvedVisualTarget target,
        string actual,
        string baseline,
        string diff,
        string report,
        string log)
    {
        var arguments = new List<string>
        {
            "--path", root,
        };
        if (target.CaptureMode == "SemanticControl")
        {
            arguments.Add("--headless");
        }
        arguments.AddRange([
            "--audio-driver", "Dummy",
            "--disable-vsync",
            "--fixed-fps", "60",
            "--quit-after", "120",
            "--log-file", log,
            "--",
        ]);
        if (target.CaptureMode == "RenderedViewport")
        {
            // Godot's headless display driver disables real rendering. Keep the
            // renderer, but require the runtime to hide and unfocus its root window.
            arguments.Add(HiddenWindowArgument);
        }
        arguments.AddRange([
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
            arguments.Add(
                $"--lx-visual-pointer={pointer.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                pointer.Y.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return arguments;
    }

    private static async Task<VisualProcessResult> RunVisualProcessAsync(
        ProcessStartInfo start,
        IReadOnlyList<string> arguments,
        string logPath,
        bool isolateNativeWindow)
    {
        if (OperatingSystem.IsWindows() && isolateNativeWindow)
        {
            var exitCode = HiddenDesktopProcess.Run(
                start.FileName,
                start.WorkingDirectory,
                arguments);
            var output = File.Exists(logPath)
                ? await File.ReadAllTextAsync(logPath)
                : string.Empty;
            return new VisualProcessResult(exitCode, output);
        }
        if (isolateNativeWindow)
        {
            throw new PlatformNotSupportedException(
                "Rendered visual validation requires an isolated native desktop on this platform; " +
                "refusing to expose an automated game window.");
        }

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ??
            throw new InvalidOperationException("Failed to start Godot visual runner.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new VisualProcessResult(
            process.ExitCode,
            string.Join(Environment.NewLine, await stdoutTask, await stderrTask));
    }

    private static ResolvedVisualTarget CreateProtocolTarget(string captureMode) => new(
        "protocol_probe",
        "res://scene/main.tscn",
        "tests/Visual/Baselines/protocol_probe.png",
        640,
        360,
        captureMode,
        1,
        0,
        0,
        null);

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

    private sealed record VisualProcessResult(int ExitCode, string Output);

    private static class HiddenDesktopProcess
    {
        private const uint GenericAll = 0x10000000;
        private const uint StartfUseShowWindow = 0x00000001;
        private const ushort SwHide = 0;
        private const uint VisualProcessTimeoutMilliseconds = 130_000;
        private const uint WaitTimeout = 0x00000102;
        private const uint WaitFailed = 0xFFFFFFFF;

        public static int Run(
            string executable,
            string workingDirectory,
            IReadOnlyList<string> arguments)
        {
            var desktopName = "LXVisual_" + Guid.NewGuid().ToString("N");
            var desktop = CreateDesktop(
                desktopName,
                IntPtr.Zero,
                IntPtr.Zero,
                0,
                GenericAll,
                IntPtr.Zero);
            if (desktop == IntPtr.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Failed to create an isolated desktop for visual validation.");
            }

            try
            {
                var startup = new StartupInfo
                {
                    Size = Marshal.SizeOf<StartupInfo>(),
                    Desktop = desktopName,
                    Flags = StartfUseShowWindow,
                    ShowWindow = SwHide,
                };
                var commandLine = new StringBuilder(
                    string.Join(" ", new[] { executable }.Concat(arguments).Select(QuoteArgument)));
                if (!CreateProcess(
                        executable,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        0,
                        IntPtr.Zero,
                        workingDirectory,
                        ref startup,
                        out var process))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Failed to start Godot on the isolated visual-validation desktop.");
                }

                try
                {
                    var waitResult = WaitForSingleObject(process.Process, VisualProcessTimeoutMilliseconds);
                    if (waitResult == WaitTimeout)
                    {
                        _ = TerminateProcess(process.Process, 1);
                        _ = WaitForSingleObject(process.Process, 5_000);
                        throw new TimeoutException(
                            "The isolated Godot visual-validation process exceeded 130 seconds.");
                    }
                    if (waitResult == WaitFailed)
                    {
                        throw new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            "Waiting for the isolated Godot process failed.");
                    }
                    if (!GetExitCodeProcess(process.Process, out var exitCode))
                    {
                        throw new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            "Failed to read the isolated Godot process exit code.");
                    }
                    return unchecked((int)exitCode);
                }
                finally
                {
                    _ = CloseHandle(process.Thread);
                    _ = CloseHandle(process.Process);
                }
            }
            finally
            {
                _ = CloseDesktop(desktop);
            }
        }

        private static string QuoteArgument(string value)
        {
            if (value.Length > 0 && value.All(character =>
                    !char.IsWhiteSpace(character) && character != '"'))
            {
                return value;
            }

            var result = new StringBuilder(value.Length + 2).Append('"');
            var backslashes = 0;
            foreach (var character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (character == '"')
                {
                    result.Append('\\', backslashes * 2 + 1).Append(character);
                    backslashes = 0;
                    continue;
                }
                result.Append('\\', backslashes).Append(character);
                backslashes = 0;
            }
            return result.Append('\\', backslashes * 2).Append('"').ToString();
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int Size;
            public string? Reserved;
            public string? Desktop;
            public string? Title;
            public uint X;
            public uint Y;
            public uint XSize;
            public uint YSize;
            public uint XCountChars;
            public uint YCountChars;
            public uint FillAttribute;
            public uint Flags;
            public ushort ShowWindow;
            public ushort Reserved2Size;
            public IntPtr Reserved2;
            public IntPtr StandardInput;
            public IntPtr StandardOutput;
            public IntPtr StandardError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr Process;
            public IntPtr Thread;
            public uint ProcessId;
            public uint ThreadId;
        }

        [DllImport("user32.dll", EntryPoint = "CreateDesktopW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateDesktop(
            string desktop,
            IntPtr device,
            IntPtr deviceMode,
            uint flags,
            uint desiredAccess,
            IntPtr securityAttributes);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseDesktop(IntPtr desktop);

        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcess(
            string applicationName,
            StringBuilder commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref StartupInfo startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr process, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
