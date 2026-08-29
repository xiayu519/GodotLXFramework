using System.Diagnostics;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using Godot;

namespace LX.Diagnostics;

internal sealed class RuntimeBridgeService : IDisposable
{
    private const string SessionSchema = "lx.runtime-session";
    private const string RequestSchema = "lx.runtime-request";
    private const string ResponseSchema = "lx.runtime-response";
    private const int ProtocolVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly DiagnosticsService _diagnostics;
    private readonly Stopwatch _heartbeat = Stopwatch.StartNew();
    private readonly Stopwatch _requestPoll = Stopwatch.StartNew();
    private readonly string _sessionPath = string.Empty;
    private readonly string _requestPath = string.Empty;
    private readonly string _responsePath = string.Empty;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly long _generation = DateTimeOffset.UtcNow.UtcTicks;
    private string? _lastRequestId;
    private long _retryNotBeforeTimestamp;
    private DateTimeOffset _lastIoFailureLogAtUtc;
    private string? _lastIoFailureOperation;
    private int _consecutiveIoFailures;
    private int _totalIoFailures;
    private int _injectedIoFailures;
    private bool _disposed;

    public RuntimeBridgeService(DiagnosticsService diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        if (!OS.HasFeature("editor") && !OS.IsDebugBuild())
        {
            return;
        }

        try
        {
            var projectRoot = ProjectSettings.GlobalizePath("res://")
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var bridgeRoot = Path.Combine(projectRoot, ".lx", "runtime");
            Directory.CreateDirectory(bridgeRoot);
            _sessionPath = Path.Combine(bridgeRoot, "session.json");
            _requestPath = Path.Combine(bridgeRoot, "request.json");
            _responsePath = Path.Combine(bridgeRoot, "response.json");
            Enabled = true;
            _ = TryWriteSession("running", ignoreBackoff: true);
        }
        catch (Exception exception)
        {
            _diagnostics.Log(
                Core.Diagnostics.DiagnosticSeverity.Warning,
                "runtime.bridge",
                "LX runtime bridge could not initialize and was disabled.",
                exception);
        }
    }

    public bool Enabled { get; private set; }

    public void Pump()
    {
        if (!Enabled || _disposed)
        {
            return;
        }

        try
        {
            PumpCore();
        }
        catch (Exception exception)
        {
            // A diagnostic side channel must never escape into LXHost._Process.
            RegisterIoFailure("pump", exception);
        }
    }

    private void PumpCore()
    {
        if (!CanAttemptIo())
        {
            return;
        }

        if (_heartbeat.Elapsed >= TimeSpan.FromSeconds(1) || _injectedIoFailures > 0)
        {
            // Restart before the write so a failure cannot retry on every frame.
            _heartbeat.Restart();
            if (!TryWriteSession("running"))
            {
                return;
            }
        }

        if (_requestPoll.Elapsed < TimeSpan.FromMilliseconds(100))
        {
            return;
        }
        _requestPoll.Restart();

        var requestExists = false;
        if (!TryIo(() => requestExists = File.Exists(_requestPath), "request.exists") || !requestExists)
        {
            return;
        }

        RuntimeBridgeRequest? request = null;
        if (!TryIo(
                () => request = JsonSerializer.Deserialize<RuntimeBridgeRequest>(
                File.ReadAllText(_requestPath),
                JsonOptions),
                "request.read"))
        {
            return;
        }

        if (request is null ||
            request.Schema != RequestSchema ||
            request.SchemaVersion != ProtocolVersion ||
            request.SessionId != _sessionId ||
            request.Generation != _generation ||
            request.RequestId == _lastRequestId)
        {
            return;
        }

        RuntimeBridgeResponse response;
        try
        {
            var payload = _diagnostics.SnapshotSection(request.Section);
            response = new RuntimeBridgeResponse(
                ResponseSchema,
                ProtocolVersion,
                request.RequestId,
                _sessionId,
                _generation,
                DateTimeOffset.UtcNow,
                true,
                null,
                request.Section,
                payload);
        }
        catch (Exception exception)
        {
            response = new RuntimeBridgeResponse(
                ResponseSchema,
                ProtocolVersion,
                request.RequestId,
                _sessionId,
                _generation,
                DateTimeOffset.UtcNow,
                false,
                exception.Message,
                request.Section,
                null);
        }

        if (TryIo(() => WriteJsonAtomic(_responsePath, response), "response.write"))
        {
            _lastRequestId = request.RequestId;
        }
    }

    public async Task RunSelfTestAsync(Node host, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            throw new InvalidOperationException("Runtime bridge is disabled during an editor smoke run.");
        }

        var failuresBefore = _totalIoFailures;
        _injectedIoFailures++;
        Pump();
        if (_totalIoFailures <= failuresBefore)
        {
            throw new InvalidOperationException("Runtime bridge I/O failure containment was not exercised.");
        }
        _retryNotBeforeTimestamp = 0;

        var requestId = "smoke-" + Guid.NewGuid().ToString("N");
        WriteJsonAtomic(
            _requestPath,
            new RuntimeBridgeRequest(
                RequestSchema,
                ProtocolVersion,
                requestId,
                _sessionId,
                _generation,
                "runtime"));
        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!File.Exists(_responsePath))
            {
                continue;
            }

            var response = JsonSerializer.Deserialize<RuntimeBridgeResponse>(
                File.ReadAllText(_responsePath),
                JsonOptions);
            if (response is
                {
                    Success: true,
                    RequestId: var responseRequest,
                    SessionId: var responseSession,
                    Generation: var responseGeneration,
                } &&
                responseRequest == requestId &&
                responseSession == _sessionId &&
                responseGeneration == _generation)
            {
                return;
            }
        }

        throw new TimeoutException("Runtime bridge did not answer its smoke request.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Enabled)
        {
            _ = TryWriteSession("stopped", ignoreBackoff: true);
        }
    }

    private bool TryWriteSession(string state, bool ignoreBackoff = false) =>
        TryIo(
            () => WriteJsonAtomic(
                _sessionPath,
                new RuntimeBridgeSession(
                    SessionSchema,
                    ProtocolVersion,
                    _sessionId,
                    _generation,
                    System.Environment.ProcessId,
                    DateTimeOffset.UtcNow,
                    state,
                    DiagnosticsService.AvailableSections)),
            $"session.{state}",
            ignoreBackoff);

    private bool TryIo(Action action, string operation, bool ignoreBackoff = false)
    {
        if (!ignoreBackoff && !CanAttemptIo())
        {
            return false;
        }

        try
        {
            if (_injectedIoFailures > 0)
            {
                _injectedIoFailures--;
                throw new IOException("Injected runtime bridge I/O failure.");
            }

            action();
            if (string.Equals(_lastIoFailureOperation, operation, StringComparison.Ordinal))
            {
                _consecutiveIoFailures = 0;
                _lastIoFailureOperation = null;
            }
            _retryNotBeforeTimestamp = 0;
            return true;
        }
        catch (Exception exception)
        {
            RegisterIoFailure(operation, exception);
            return false;
        }
    }

    private bool CanAttemptIo() =>
        _retryNotBeforeTimestamp == 0 || Stopwatch.GetTimestamp() >= _retryNotBeforeTimestamp;

    private void RegisterIoFailure(string operation, Exception exception)
    {
        _totalIoFailures++;
        if (string.Equals(_lastIoFailureOperation, operation, StringComparison.Ordinal))
        {
            _consecutiveIoFailures++;
        }
        else
        {
            _lastIoFailureOperation = operation;
            _consecutiveIoFailures = 1;
        }
        var exponent = Math.Min(_consecutiveIoFailures - 1, 6);
        var delay = TimeSpan.FromMilliseconds(Math.Min(10_000, 250 * (1 << exponent)));
        _retryNotBeforeTimestamp = Stopwatch.GetTimestamp() +
                                   (long)(delay.TotalSeconds * Stopwatch.Frequency);

        var now = DateTimeOffset.UtcNow;
        if (_consecutiveIoFailures != 1 && now - _lastIoFailureLogAtUtc < TimeSpan.FromSeconds(10))
        {
            return;
        }

        _lastIoFailureLogAtUtc = now;
        try
        {
            _diagnostics.Log(
                Core.Diagnostics.DiagnosticSeverity.Warning,
                "runtime.bridge",
                $"Runtime bridge I/O '{operation}' failed; retrying after {delay.TotalMilliseconds:0} ms.",
                exception);
        }
        catch
        {
            // Failure reporting is part of the same non-critical side channel.
        }
    }

    private static void WriteJsonAtomic<T>(string path, T value)
    {
        var content = JsonSerializer.Serialize(value, JsonOptions) + "\n";
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, new System.Text.UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch
            {
                // A later heartbeat uses a unique temporary name and can recover.
            }
        }
    }
}

internal sealed record RuntimeBridgeSession(
    string Schema,
    int SchemaVersion,
    string SessionId,
    long Generation,
    int ProcessId,
    DateTimeOffset HeartbeatAtUtc,
    string State,
    IReadOnlyList<string> Sections);

internal sealed record RuntimeBridgeRequest(
    string Schema,
    int SchemaVersion,
    string RequestId,
    string SessionId,
    long Generation,
    string Section);

internal sealed record RuntimeBridgeResponse(
    string Schema,
    int SchemaVersion,
    string RequestId,
    string SessionId,
    long Generation,
    DateTimeOffset CapturedAtUtc,
    bool Success,
    string? Error,
    string Section,
    object? Payload);
