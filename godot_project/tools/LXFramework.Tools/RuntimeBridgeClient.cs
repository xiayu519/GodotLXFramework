using System.Diagnostics;

namespace LXFramework.Tools;

internal static class RuntimeBridgeClient
{
    private static readonly HashSet<string> Sections = new(StringComparer.OrdinalIgnoreCase)
    {
        "all", "runtime", "events", "scheduler", "actions", "metrics", "resources",
        "ui", "features", "audio", "input", "localization", "settings", "logs",
    };

    public static int Run(string root, IReadOnlyList<string> arguments)
    {
        var operation = arguments.Count == 0 ? "status" : arguments[0].ToLowerInvariant();
        return operation switch
        {
            "status" when arguments.Count == 1 || arguments.Count == 0 => Status(root),
            "snapshot" => Snapshot(root, arguments.Skip(1).ToArray()),
            _ => Usage(),
        };
    }

    private static int Status(string root)
    {
        if (!TryReadLiveSession(root, out var session, out var error))
        {
            Console.Error.WriteLine($"runtime: {error}");
            return 1;
        }

        var output = Path.Combine(root, ".lx", "runtime", "status.json");
        ToolFiles.WriteJson(output, session);
        Console.WriteLine(
            $"runtime active: session={session.SessionId}, generation={session.Generation}, " +
            $"pid={session.ProcessId} -> {ToolFiles.Relative(root, output)}");
        return 0;
    }

    private static int Snapshot(string root, IReadOnlyList<string> arguments)
    {
        if (arguments.Count > 1)
        {
            return Usage();
        }

        var section = arguments.Count == 0 ? "all" : arguments[0].ToLowerInvariant();
        if (!Sections.Contains(section))
        {
            Console.Error.WriteLine(
                $"runtime: unknown snapshot section '{section}'. Available: {string.Join(", ", Sections)}");
            return 2;
        }
        if (!TryReadLiveSession(root, out var session, out var error))
        {
            Console.Error.WriteLine($"runtime: {error}");
            return 1;
        }

        var runtimeRoot = Path.Combine(root, ".lx", "runtime");
        var requestPath = Path.Combine(runtimeRoot, "request.json");
        var responsePath = Path.Combine(runtimeRoot, "response.json");
        var requestId = Guid.NewGuid().ToString("N");
        ToolFiles.WriteJson(
            requestPath,
            new RuntimeRequest(
                "lx.runtime-request",
                1,
                requestId,
                session.SessionId,
                session.Generation,
                section));

        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            Thread.Sleep(50);
            if (!File.Exists(responsePath))
            {
                continue;
            }

            RuntimeResponse? response;
            try
            {
                response = ToolFiles.ReadJson<RuntimeResponse>(responsePath);
            }
            catch (IOException)
            {
                continue;
            }
            catch (System.Text.Json.JsonException)
            {
                continue;
            }

            if (response.RequestId != requestId)
            {
                continue;
            }
            if (response.SessionId != session.SessionId || response.Generation != session.Generation)
            {
                Console.Error.WriteLine("runtime: response belongs to a stale runtime generation.");
                return 1;
            }
            if (!response.Success)
            {
                Console.Error.WriteLine($"runtime: {response.Error ?? "snapshot failed"}");
                return 1;
            }

            var output = Path.Combine(runtimeRoot, $"snapshot-{section}.json");
            ToolFiles.WriteText(output, File.ReadAllText(responsePath) + "\n");
            Console.WriteLine(
                $"runtime snapshot '{section}' captured for session {session.SessionId} -> " +
                ToolFiles.Relative(root, output));
            return 0;
        }

        Console.Error.WriteLine("runtime: timed out waiting for the current Godot runtime.");
        return 1;
    }

    private static bool TryReadLiveSession(
        string root,
        out RuntimeSession session,
        out string error)
    {
        var path = Path.Combine(root, ".lx", "runtime", "session.json");
        if (!File.Exists(path))
        {
            session = null!;
            error = "no runtime session was published; run the project with the Godot editor/debug binary.";
            return false;
        }

        try
        {
            session = ToolFiles.ReadJson<RuntimeSession>(path);
            using var process = Process.GetProcessById(session.ProcessId);
            if (process.HasExited ||
                session.State != "running" ||
                DateTimeOffset.UtcNow - session.HeartbeatAtUtc > TimeSpan.FromSeconds(4))
            {
                error = "the published runtime session is stale or stopped.";
                return false;
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or IOException or
                System.Text.Json.JsonException)
        {
            session = null!;
            error = $"runtime session is unavailable: {exception.Message}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("runtime usage: lx runtime status | snapshot [section]");
        return 2;
    }
}

internal sealed record RuntimeSession(
    string Schema,
    int SchemaVersion,
    string SessionId,
    long Generation,
    int ProcessId,
    DateTimeOffset HeartbeatAtUtc,
    string State,
    IReadOnlyList<string> Sections);

internal sealed record RuntimeRequest(
    string Schema,
    int SchemaVersion,
    string RequestId,
    string SessionId,
    long Generation,
    string Section);

internal sealed record RuntimeResponse(
    string Schema,
    int SchemaVersion,
    string RequestId,
    string SessionId,
    long Generation,
    DateTimeOffset CapturedAtUtc,
    bool Success,
    string? Error,
    string Section,
    System.Text.Json.JsonElement? Payload);
