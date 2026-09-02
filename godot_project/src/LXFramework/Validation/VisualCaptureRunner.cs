using System.Security.Cryptography;
using System.Text.Json;
using LX.Res;
using LX.Runtime;
using Godot;

namespace LX.Validation;

/// <summary>在固定 SubViewport 中捕获和比较框架视觉基准。</summary>
internal sealed class VisualCaptureRunner
{
    private readonly Node _host;
    private readonly LXContext _context;

    public VisualCaptureRunner(Node host, LXContext context)
    {
        _host = host;
        _context = context;
    }

    public async ValueTask<VisualComparisonReport> RunAsync(
        string mode,
        string captureMode,
        string target,
        string scenePath,
        Vector2I captureSize,
        int readyFrames,
        float pixelTolerance,
        float maxChangedPixelRatio,
        Vector2? pointerPosition,
        string actualPath,
        string? baselinePath,
        string? diffPath,
        CancellationToken cancellationToken)
    {
        if (mode is not ("capture" or "compare"))
        {
            throw new ArgumentException($"Unsupported visual mode '{mode}'.", nameof(mode));
        }
        if (captureMode is not ("SemanticControl" or "RenderedViewport"))
        {
            throw new ArgumentException($"Unsupported visual capture mode '{captureMode}'.", nameof(captureMode));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(actualPath)!);
        if (captureSize.X is < 64 or > 4096 || captureSize.Y is < 64 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(captureSize));
        }
        if (readyFrames is < 1 or > 300 ||
            pixelTolerance is < 0 or > 1 ||
            maxChangedPixelRatio is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(readyFrames));
        }
        using var lease = _context.Res.Acquire<PackedScene>(scenePath, AssetCachePolicy.Cached);
        using var visualLifetime = _context.Lifetime.CreateChild($"VisualCapture:{target}");
        SubViewport? semanticViewport = null;
        Viewport captureViewport;
        Node captureParent;
        if (captureMode == "RenderedViewport")
        {
            var window = _host.GetWindow();
            window.ContentScaleSize = captureSize;
            window.Size = captureSize;
            captureViewport = _host.GetViewport();
            captureParent = _host;
        }
        else
        {
            semanticViewport = new SubViewport
            {
                Name = "LXVisualCapture",
                Size = captureSize,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                TransparentBg = false,
            };
            semanticViewport.World2D = new World2D();
            _host.AddChild(semanticViewport);
            captureViewport = semanticViewport;
            captureParent = semanticViewport;
        }
        var instance = lease.Resource.Instantiate();
        LXContextInjector.InitializeTree(instance, _context, visualLifetime);
        captureParent.AddChild(instance);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _host.ToSignal(_host.GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (var readiness in FindReadinessBarriers(instance))
            {
                await readiness.WaitForVisualCaptureReadyAsync(cancellationToken);
            }
            if (pointerPosition is { } pointer)
            {
                captureViewport.PushInput(new InputEventMouseMotion
                {
                    Position = pointer,
                    GlobalPosition = pointer,
                }, inLocalCoords: true);
            }
            for (var frame = 0; frame < readyFrames; frame++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _host.ToSignal(_host.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (captureMode == "RenderedViewport" && !RenderingServer.RenderLoopEnabled)
                {
                    // Hidden validation renders only the explicitly requested frames.
                    // swapBuffers=false keeps the GPU result off the desktop compositor.
                    RenderingServer.ForceDraw(false, 1.0 / 60.0);
                }
            }

            using var actual = captureMode == "RenderedViewport"
                ? await CaptureRenderedViewportAsync(captureViewport, cancellationToken)
                : RenderSemanticSnapshot(instance, captureSize);
            if (actual.GetWidth() != captureSize.X || actual.GetHeight() != captureSize.Y)
            {
                throw new IOException(
                    $"Visual capture produced {actual.GetWidth()}x{actual.GetHeight()} instead of " +
                    $"the declared {captureSize.X}x{captureSize.Y} target.");
            }
            var saveError = actual.SavePng(actualPath);
            if (saveError != Error.Ok)
            {
                throw new IOException($"Godot failed to save visual capture: {saveError}.");
            }

            if (mode == "capture")
            {
                return new VisualComparisonReport(
                    "lx.visual-report",
                    2,
                    target,
                    captureMode,
                    true,
                    actual.GetWidth(),
                    actual.GetHeight(),
                    0,
                    0,
                    pixelTolerance,
                    maxChangedPixelRatio,
                    ComputeHash(actualPath),
                    null,
                    actualPath,
                    baselinePath,
                    diffPath,
                    CaptureEnvironment());
            }

            if (string.IsNullOrWhiteSpace(baselinePath) || !File.Exists(baselinePath))
            {
                return new VisualComparisonReport(
                    "lx.visual-report",
                    2,
                    target,
                    captureMode,
                    false,
                    actual.GetWidth(),
                    actual.GetHeight(),
                    -1,
                    1,
                    pixelTolerance,
                    maxChangedPixelRatio,
                    ComputeHash(actualPath),
                    null,
                    actualPath,
                    baselinePath,
                    diffPath,
                    CaptureEnvironment());
            }

            using var baseline = Image.LoadFromFile(baselinePath);
            if (baseline.GetWidth() != actual.GetWidth() || baseline.GetHeight() != actual.GetHeight())
            {
                return new VisualComparisonReport(
                    "lx.visual-report",
                    2,
                    target,
                    captureMode,
                    false,
                    actual.GetWidth(),
                    actual.GetHeight(),
                    -1,
                    1,
                    pixelTolerance,
                    maxChangedPixelRatio,
                    ComputeHash(actualPath),
                    ComputeHash(baselinePath),
                    actualPath,
                    baselinePath,
                    diffPath,
                    CaptureEnvironment());
            }

            using var diff = Image.CreateEmpty(actual.GetWidth(), actual.GetHeight(), false, Image.Format.Rgba8);
            long changedPixels = 0;
            for (var y = 0; y < actual.GetHeight(); y++)
            {
                for (var x = 0; x < actual.GetWidth(); x++)
                {
                    var actualPixel = actual.GetPixel(x, y);
                    var baselinePixel = baseline.GetPixel(x, y);
                    var changed = MaxChannelDelta(actualPixel, baselinePixel) > pixelTolerance;
                    if (changed)
                    {
                        changedPixels++;
                    }
                    diff.SetPixel(x, y, changed
                        ? new Color(1, 0, 0.7f, 1)
                        : new Color(actualPixel.R, actualPixel.G, actualPixel.B, 0.12f));
                }
            }

            var changedPixelRatio = changedPixels / (double)(actual.GetWidth() * actual.GetHeight());

            if (!string.IsNullOrWhiteSpace(diffPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(diffPath)!);
                var diffError = diff.SavePng(diffPath);
                if (diffError != Error.Ok)
                {
                    throw new IOException($"Godot failed to save visual diff: {diffError}.");
                }
            }

            return new VisualComparisonReport(
                "lx.visual-report",
                2,
                target,
                captureMode,
                changedPixelRatio <= maxChangedPixelRatio,
                actual.GetWidth(),
                actual.GetHeight(),
                changedPixels,
                changedPixelRatio,
                pixelTolerance,
                maxChangedPixelRatio,
                ComputeHash(actualPath),
                ComputeHash(baselinePath),
                actualPath,
                baselinePath,
                diffPath,
                CaptureEnvironment());
        }
        finally
        {
            instance.QueueFree();
            semanticViewport?.QueueFree();
        }
    }

    private async ValueTask<Image> CaptureRenderedViewportAsync(
        Viewport viewport,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (RenderingServer.RenderLoopEnabled)
        {
            await _host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        else
        {
            RenderingServer.ForceDraw(false, 1.0 / 60.0);
        }
        RenderingServer.ForceSync();
        cancellationToken.ThrowIfCancellationRequested();
        return viewport.GetTexture().GetImage();
    }

    private static IReadOnlyList<IVisualCaptureReady> FindReadinessBarriers(Node root)
    {
        var barriers = new List<IVisualCaptureReady>();
        CollectReadinessBarriers(root, barriers);
        return barriers;
    }

    private static void CollectReadinessBarriers(Node node, ICollection<IVisualCaptureReady> barriers)
    {
        if (node is IVisualCaptureReady barrier)
        {
            barriers.Add(barrier);
        }
        for (var index = 0; index < node.GetChildCount(); index++)
        {
            CollectReadinessBarriers(node.GetChild(index), barriers);
        }
    }

    private static float MaxChannelDelta(Color left, Color right) =>
        Math.Max(
            Math.Max(Math.Abs(left.R - right.R), Math.Abs(left.G - right.G)),
            Math.Max(Math.Abs(left.B - right.B), Math.Abs(left.A - right.A)));

    private static VisualCaptureEnvironment CaptureEnvironment()
    {
        var version = Engine.GetVersionInfo();
        return new VisualCaptureEnvironment(
            version.TryGetValue("string", out var engineVersion)
                ? engineVersion.AsString()
                : "unknown",
            ProjectSettings.GetSetting("rendering/renderer/rendering_method").AsString(),
            OS.GetName(),
            TranslationServer.GetLocale());
    }

    public static void WriteReport(string path, VisualComparisonReport report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }));
    }

    private static Image RenderSemanticSnapshot(Node root, Vector2I captureSize)
    {
        var image = Image.CreateEmpty(captureSize.X, captureSize.Y, false, Image.Format.Rgba8);
        image.Fill(Color.FromHtml("#EFF6FF"));
        DrawNode(root, image);
        return image;
    }

    private static void DrawNode(Node node, Image image)
    {
        if (node is Control { Visible: true } control)
        {
            var rect = control.GetGlobalRect();
            switch (control)
            {
                case ColorRect colorRect:
                    FillRect(image, rect, colorRect.Color);
                    break;
                case PanelContainer:
                    FillRect(image, rect, Colors.White);
                    StrokeRect(image, rect, Color.FromHtml("#CBD5E1"), 2);
                    break;
                case Button button:
                    FillRect(image, rect, Color.FromHtml("#DBEAFE"));
                    StrokeRect(image, rect, Color.FromHtml("#2563EB"), 1);
                    DrawTextFingerprint(image, rect, button.Text, Color.FromHtml("#2563EB"));
                    break;
                case ProgressBar progress:
                    FillRect(image, rect, Color.FromHtml("#E2E8F0"));
                    var ratio = progress.MaxValue <= progress.MinValue
                        ? 0
                        : (float)((progress.Value - progress.MinValue) /
                                  (progress.MaxValue - progress.MinValue));
                    FillRect(image, new Rect2(rect.Position, new Vector2(rect.Size.X * ratio, rect.Size.Y)),
                        Color.FromHtml("#2563EB"));
                    break;
                case Label label:
                    DrawTextFingerprint(image, rect, label.Text, label.GetThemeColor("font_color"));
                    break;
            }
        }

        var childCount = node.GetChildCount();
        for (var index = 0; index < childCount; index++)
        {
            DrawNode(node.GetChild(index), image);
        }
    }

    private static void DrawTextFingerprint(Image image, Rect2 rect, string text, Color color)
    {
        if (string.IsNullOrEmpty(text) || rect.Size.X <= 0 || rect.Size.Y <= 0)
        {
            return;
        }
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        var y = Mathf.RoundToInt(rect.Position.Y + rect.Size.Y / 2);
        var maxWidth = Math.Min(Mathf.RoundToInt(rect.Size.X), Math.Max(8, text.Length * 7));
        var startX = Mathf.RoundToInt(rect.Position.X + 2);
        for (var x = 0; x < maxWidth; x++)
        {
            var byteValue = hash[x % hash.Length];
            var height = 2 + byteValue % 5;
            for (var offset = 0; offset < height; offset++)
            {
                SetPixelSafe(image, startX + x, y + offset - height / 2, color);
            }
        }
    }

    private static void FillRect(Image image, Rect2 rect, Color color)
    {
        var left = Math.Clamp(Mathf.FloorToInt(rect.Position.X), 0, image.GetWidth());
        var top = Math.Clamp(Mathf.FloorToInt(rect.Position.Y), 0, image.GetHeight());
        var right = Math.Clamp(Mathf.CeilToInt(rect.End.X), 0, image.GetWidth());
        var bottom = Math.Clamp(Mathf.CeilToInt(rect.End.Y), 0, image.GetHeight());
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                image.SetPixel(x, y, color);
            }
        }
    }

    private static void StrokeRect(Image image, Rect2 rect, Color color, int width)
    {
        FillRect(image, new Rect2(rect.Position, new Vector2(rect.Size.X, width)), color);
        FillRect(image, new Rect2(rect.Position, new Vector2(width, rect.Size.Y)), color);
        FillRect(image, new Rect2(rect.Position + new Vector2(0, rect.Size.Y - width),
            new Vector2(rect.Size.X, width)), color);
        FillRect(image, new Rect2(rect.Position + new Vector2(rect.Size.X - width, 0),
            new Vector2(width, rect.Size.Y)), color);
    }

    private static void SetPixelSafe(Image image, int x, int y, Color color)
    {
        if (x >= 0 && x < image.GetWidth() && y >= 0 && y < image.GetHeight())
        {
            image.SetPixel(x, y, color);
        }
    }

    private static string ComputeHash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}

internal sealed record VisualComparisonReport(
    string Schema,
    int SchemaVersion,
    string Target,
    string CaptureMode,
    bool Success,
    int Width,
    int Height,
    long ChangedPixels,
    double ChangedPixelRatio,
    float PixelTolerance,
    float MaxChangedPixelRatio,
    string ActualSha256,
    string? BaselineSha256,
    string ActualPath,
    string? BaselinePath,
    string? DiffPath,
    VisualCaptureEnvironment Environment);

internal sealed record VisualCaptureEnvironment(
    string EngineVersion,
    string RenderingMethod,
    string OperatingSystem,
    string Locale);
