using System.Security.Cryptography;
using System.Text.Json;
using LX.Res;
using Godot;

namespace LX.Validation;

/// <summary>在固定 SubViewport 中捕获和比较框架视觉基准。</summary>
internal sealed class VisualCaptureRunner
{
    private const string ShowcaseScene = "res://scene/ui/examples/ui_components_showcase.tscn";
    private static readonly Vector2I CaptureSize = new(1280, 720);
    private readonly Node _host;
    private readonly AssetRegistry _assets;

    public VisualCaptureRunner(Node host, AssetRegistry assets)
    {
        _host = host;
        _assets = assets;
    }

    public async ValueTask<VisualComparisonReport> RunAsync(
        string mode,
        string actualPath,
        string? baselinePath,
        string? diffPath,
        CancellationToken cancellationToken)
    {
        if (mode is not ("capture" or "compare"))
        {
            throw new ArgumentException($"Unsupported visual mode '{mode}'.", nameof(mode));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(actualPath)!);
        using var lease = _assets.Acquire<PackedScene>(ShowcaseScene, AssetCachePolicy.Cached);
        var viewport = new SubViewport
        {
            Name = "LXVisualCapture",
            Size = CaptureSize,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
        };
        viewport.World2D = new World2D();
        _host.AddChild(viewport);
        var instance = lease.Resource.Instantiate();
        viewport.AddChild(instance);
        try
        {
            for (var frame = 0; frame < 4; frame++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _host.ToSignal(_host.GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            using var actual = RenderSemanticSnapshot(instance);
            var saveError = actual.SavePng(actualPath);
            if (saveError != Error.Ok)
            {
                throw new IOException($"Godot failed to save visual capture: {saveError}.");
            }

            if (mode == "capture")
            {
                return new VisualComparisonReport(
                    "lx.visual-report",
                    1,
                    "ui_components",
                    true,
                    CaptureSize.X,
                    CaptureSize.Y,
                    0,
                    ComputeHash(actualPath),
                    null,
                    actualPath,
                    baselinePath,
                    diffPath);
            }

            if (string.IsNullOrWhiteSpace(baselinePath) || !File.Exists(baselinePath))
            {
                return new VisualComparisonReport(
                    "lx.visual-report",
                    1,
                    "ui_components",
                    false,
                    CaptureSize.X,
                    CaptureSize.Y,
                    -1,
                    ComputeHash(actualPath),
                    null,
                    actualPath,
                    baselinePath,
                    diffPath);
            }

            using var baseline = Image.LoadFromFile(baselinePath);
            if (baseline.GetWidth() != actual.GetWidth() || baseline.GetHeight() != actual.GetHeight())
            {
                return new VisualComparisonReport(
                    "lx.visual-report",
                    1,
                    "ui_components",
                    false,
                    actual.GetWidth(),
                    actual.GetHeight(),
                    -1,
                    ComputeHash(actualPath),
                    ComputeHash(baselinePath),
                    actualPath,
                    baselinePath,
                    diffPath);
            }

            using var diff = Image.CreateEmpty(actual.GetWidth(), actual.GetHeight(), false, Image.Format.Rgba8);
            long changedPixels = 0;
            for (var y = 0; y < actual.GetHeight(); y++)
            {
                for (var x = 0; x < actual.GetWidth(); x++)
                {
                    var actualPixel = actual.GetPixel(x, y);
                    var baselinePixel = baseline.GetPixel(x, y);
                    var changed = !actualPixel.IsEqualApprox(baselinePixel);
                    if (changed)
                    {
                        changedPixels++;
                    }
                    diff.SetPixel(x, y, changed
                        ? new Color(1, 0, 0.7f, 1)
                        : new Color(actualPixel.R, actualPixel.G, actualPixel.B, 0.12f));
                }
            }

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
                1,
                "ui_components",
                changedPixels == 0,
                actual.GetWidth(),
                actual.GetHeight(),
                changedPixels,
                ComputeHash(actualPath),
                ComputeHash(baselinePath),
                actualPath,
                baselinePath,
                diffPath);
        }
        finally
        {
            instance.QueueFree();
            viewport.QueueFree();
        }
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

    private static Image RenderSemanticSnapshot(Node root)
    {
        var image = Image.CreateEmpty(CaptureSize.X, CaptureSize.Y, false, Image.Format.Rgba8);
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
    bool Success,
    int Width,
    int Height,
    long ChangedPixels,
    string ActualSha256,
    string? BaselineSha256,
    string ActualPath,
    string? BaselinePath,
    string? DiffPath);
