using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LXFramework.Tools;

internal static partial class ProductSmokeRunner
{
    public static async Task<int> RunAsync(
        string root,
        IReadOnlyList<string> arguments)
    {
        var selection = arguments.Count == 0 ? "all" : arguments[0];
        var affected = string.Equals(selection, "affected", StringComparison.OrdinalIgnoreCase);
        if ((!affected && arguments.Count > 1) || (affected && arguments.Count < 2))
        {
            Console.Error.WriteLine(
                "smoke product usage: lx smoke product [id|all|affected <changed-path> ...]");
            return 2;
        }
        if (affected && arguments.Skip(1).Any(path => !ProductSmokeImpact.IsValidChangedPath(path)))
        {
            Console.Error.WriteLine(
                "smoke product affected paths must stay inside the workspace and cannot contain '.' or '..' segments.");
            return 2;
        }

        var game = ToolFiles.ReadJson<GameManifest>(
            Path.Combine(root, "content", "game", "game-manifest.json"));
        GameGenerator.Validate(root, game);
        var productSmokes = game.GetProductSmokes();
        ProductValidationImpact? impact = affected
            ? ProductSmokeImpact.Analyze(game, arguments.Skip(1))
            : null;
        if (impact is not null)
        {
            PrintImpact(impact);
            if (impact.UnmatchedRuntimePaths.Count != 0)
            {
                Console.Error.WriteLine(
                    "product-smoke: runtime-affecting paths have no smoke, visual, or static-only coverage:");
                foreach (var path in impact.UnmatchedRuntimePaths)
                {
                    Console.Error.WriteLine($"  {path}");
                }
                Console.Error.WriteLine(
                    "Add productSmokes[].checkPaths, visualTargets[].checkPaths, or staticCheckPaths with a narrow pattern and reason.");
                WriteEmptyReport(root, game.Name, success: false, impact.Mappings);
                return 1;
            }
        }

        var selected = impact is not null
            ? impact.Smokes.ToArray()
            : string.Equals(selection, "all", StringComparison.OrdinalIgnoreCase)
                ? productSmokes.ToArray()
                : productSmokes
                    .Where(smoke => string.Equals(smoke.Id, selection, StringComparison.Ordinal))
                    .ToArray();
        if (selected.Length == 0 &&
            !affected &&
            !string.Equals(selection, "all", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"smoke product: unknown id '{selection}'. Available: " +
                string.Join(", ", productSmokes.Select(smoke => smoke.Id)));
            return 2;
        }

        if (selected.Length == 0)
        {
            WriteEmptyReport(root, game.Name, success: true, impact?.Mappings ?? []);
            Console.WriteLine(affected
                ? "product-smoke        skipped (affected paths are covered without a product smoke)"
                : "product-smoke        skipped (no product smoke scenarios)");
            Console.WriteLine("report               .lx/product-smoke.json");
            return 0;
        }

        var executable = GodotLocator.Find(root, preferConsole: true);
        if (executable is null)
        {
            Console.Error.WriteLine("smoke product: Godot .NET was not found. Run 'lx doctor'.");
            return 2;
        }

        SmokeCheck? preparation = null;
        if (!File.Exists(Path.Combine(root, ".godot", "uid_cache.bin")))
        {
            preparation = await GodotSmoke.RunCheckAsync(
                executable,
                root,
                "editor-import",
                ["--headless", "--editor", "--quit"]);
        }
        var results = new List<ProductSmokeResult>();
        if (preparation?.Success != false)
        {
            foreach (var smoke in selected)
            {
                results.Add(await RunScenarioAsync(executable, root, smoke));
            }
        }
        var report = new ProductSmokeReport(
            "lx.product-smoke-report",
            3,
            DateTimeOffset.UtcNow,
            game.Name,
            preparation?.Success != false &&
            results.Count == selected.Length &&
            results.All(result => result.Success),
            preparation,
            impact?.Mappings ?? [],
            results);
        var output = Path.Combine(root, ".lx", "product-smoke.json");
        ToolFiles.WriteJson(output, report);

        if (preparation is not null)
        {
            Console.WriteLine(
                $"product:editor-import {(preparation.Success ? "passed" : "failed")} " +
                $"(exit {preparation.ExitCode})");
            foreach (var error in preparation.Errors)
            {
                Console.Error.WriteLine($"smoke product: editor-import: {error}");
            }
        }
        foreach (var result in results)
        {
            Console.WriteLine(
                $"product:{result.Id,-20} {(result.Success ? "passed" : "failed")} " +
                $"({result.DurationMs} ms, {result.LogBytes} bytes, exit {result.ExitCode})");
            if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
            {
                Console.Error.WriteLine(
                    $"smoke product: {result.Id} [{result.FailureStage ?? "unknown"}]: {result.Error}");
            }
        }
        Console.WriteLine($"report               {ToolFiles.Relative(root, output)}");
        return report.Success ? 0 : 1;
    }

    private static void PrintImpact(ProductValidationImpact impact)
    {
        foreach (var mapping in impact.Mappings)
        {
            Console.WriteLine(
                $"validation impact    {mapping.Path} -> " +
                (mapping.Gates.Count == 0 ? "UNMAPPED" : string.Join(", ", mapping.Gates)));
        }
    }

    private static void WriteEmptyReport(
        string root,
        string product,
        bool success,
        IReadOnlyList<ProductValidationPathMapping> mappings)
    {
        var report = new ProductSmokeReport(
            "lx.product-smoke-report",
            3,
            DateTimeOffset.UtcNow,
            product,
            success,
            null,
            mappings,
            []);
        ToolFiles.WriteJson(Path.Combine(root, ".lx", "product-smoke.json"), report);
    }

    private static async Task<ProductSmokeResult> RunScenarioAsync(
        string executable,
        string root,
        ProductSmokeManifestEntry smoke)
    {
        var logDirectory = Path.Combine(root, ".lx", "product-smoke");
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, smoke.Id + ".log");
        if (File.Exists(logPath))
        {
            File.Delete(logPath);
        }

        var scanner = new SmokeEvidenceScanner(smoke);
        var stopwatch = Stopwatch.StartNew();
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
            "--headless",
            "--audio-driver", "Dummy",
            "--fixed-fps", "60",
            "--log-file", logPath,
        };
        if (!string.IsNullOrWhiteSpace(smoke.ScenePath))
        {
            processArguments.Add(smoke.ScenePath);
        }
        processArguments.Add("--");
        processArguments.Add(smoke.Argument);
        foreach (var argument in processArguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ??
                            throw new InvalidOperationException("Failed to start Godot for product smoke.");
        var stdoutTask = ScanReaderAsync(process.StandardOutput, "stdout", scanner);
        var stderrTask = ScanReaderAsync(process.StandardError, "stderr", scanner);
        var timedOut = false;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(smoke.TimeoutSeconds)))
        {
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                timedOut = true;
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        await Task.WhenAll(stdoutTask, stderrTask);
        if (File.Exists(logPath))
        {
            foreach (var line in File.ReadLines(logPath))
            {
                scanner.Scan("godot-log", line);
            }
        }
        stopwatch.Stop();

        var evaluation = scanner.Evaluate(smoke.StatePolicy);
        var errors = new List<string>();
        if (timedOut)
        {
            errors.Add($"Exceeded timeout of {smoke.TimeoutSeconds} seconds.");
        }
        errors.AddRange(evaluation.Errors);
        if (!timedOut && process.ExitCode != 0)
        {
            errors.Add($"Godot exited with code {process.ExitCode}.");
        }
        var success = !timedOut && process.ExitCode == 0 && errors.Count == 0;
        var failureStage = success
            ? null
            : timedOut
                ? $"timeout:{scanner.ActiveStage ?? "process"}"
                : evaluation.FailureStage ?? (process.ExitCode == 0 ? "evidence" : "process-exit");
        return new ProductSmokeResult(
            smoke.Id,
            success,
            timedOut ? -1 : process.ExitCode,
            smoke.Argument,
            stopwatch.ElapsedMilliseconds,
            File.Exists(logPath) ? new FileInfo(logPath).Length : 0,
            ToolFiles.Relative(root, logPath),
            failureStage,
            evaluation.Checkpoints,
            scanner.Tail,
            errors.Count == 0 ? null : string.Join(" ", errors));
    }

    private static async Task ScanReaderAsync(
        StreamReader reader,
        string source,
        SmokeEvidenceScanner scanner)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            scanner.Scan(source, line);
        }
    }

    public static string? ValidateProtocol()
    {
        var smoke = new ProductSmokeManifestEntry
        {
            Id = "fixture",
            SuccessMarker = "FIXTURE_PASS",
            Checkpoints =
            [
                new ProductSmokeCheckpointManifestEntry
                {
                    Id = "loaded",
                    SuccessMarker = "FIXTURE_LOADED",
                },
            ],
        };
        var scanner = new SmokeEvidenceScanner(smoke);
        scanner.Scan("fixture", "LX_SMOKE_EVENT {\"kind\":\"started\",\"id\":\"loaded\"}");
        scanner.Scan("fixture", "LX_SMOKE_EVENT {\"kind\":\"checkpoint\",\"id\":\"loaded\",\"success\":true}");
        scanner.Scan("fixture", "FIXTURE_PASS");
        const string state = "{\"resources\":[],\"metrics\":{\"gauges\":{\"product.pool.borrowed\":0}}}";
        scanner.Scan("fixture", $"LX_SMOKE_EVENT {{\"kind\":\"snapshot\",\"stage\":\"before\",\"state\":{state}}}");
        scanner.Scan("fixture", $"LX_SMOKE_EVENT {{\"kind\":\"snapshot\",\"stage\":\"after\",\"state\":{state}}}");
        var evaluation = scanner.Evaluate(new ProductSmokeStatePolicyManifestEntry
        {
            Required = true,
            Compare = ["resources"],
            MetricGauges = ["product.pool.borrowed"],
        });
        if (evaluation.Errors.Count != 0 ||
            evaluation.Checkpoints.Count != 2 ||
            evaluation.Checkpoints.Any(checkpoint => !checkpoint.Success))
        {
            return "Structured product smoke protocol did not accept valid checkpoints and balanced snapshots.";
        }

        var incomplete = new SmokeEvidenceScanner(smoke);
        incomplete.Scan("fixture", "FIXTURE_PASS");
        var incompleteEvaluation = incomplete.Evaluate(null);
        if (incompleteEvaluation.Errors.Count == 0 ||
            incompleteEvaluation.FailureStage != "checkpoint:loaded")
        {
            return "Structured product smoke protocol accepted a missing checkpoint.";
        }
        return null;
    }

    [GeneratedRegex("\\x1B(?:[@-Z\\\\-_]|\\[[0-?]*[ -/]*[@-~])", RegexOptions.CultureInvariant)]
    private static partial Regex AnsiEscapeRegex();

    private sealed class SmokeEvidenceScanner
    {
        private const string EventPrefix = "LX_SMOKE_EVENT ";
        private const int TailCapacity = 200;
        private readonly object _gate = new();
        private readonly Dictionary<string, ExpectedCheckpoint> _checkpoints;
        private readonly HashSet<string> _engineErrors = new(StringComparer.Ordinal);
        private readonly HashSet<string> _protocolErrors = new(StringComparer.Ordinal);
        private readonly Queue<string> _tail = new();
        private JsonElement? _before;
        private JsonElement? _after;
        private string? _activeStage;

        public SmokeEvidenceScanner(ProductSmokeManifestEntry smoke)
        {
            _checkpoints = smoke.Checkpoints.ToDictionary(
                checkpoint => checkpoint.Id,
                checkpoint => new ExpectedCheckpoint(checkpoint.Id, checkpoint.SuccessMarker),
                StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(smoke.SuccessMarker))
            {
                _checkpoints.Add(smoke.Id, new ExpectedCheckpoint(smoke.Id, smoke.SuccessMarker));
            }
        }

        public string? ActiveStage
        {
            get
            {
                lock (_gate)
                {
                    return _activeStage;
                }
            }
        }

        public IReadOnlyList<string> Tail
        {
            get
            {
                lock (_gate)
                {
                    return _tail.ToArray();
                }
            }
        }

        public void Scan(string source, string rawLine)
        {
            var line = AnsiEscapeRegex().Replace(rawLine, string.Empty).Trim();
            if (line.Length == 0)
            {
                return;
            }

            lock (_gate)
            {
                var displayLine = line.Length <= 8192 ? line : line[..8192] + "…";
                _tail.Enqueue($"[{source}] {displayLine}");
                while (_tail.Count > TailCapacity)
                {
                    _tail.Dequeue();
                }

                foreach (var checkpoint in _checkpoints.Values)
                {
                    if (line.Contains(checkpoint.Marker, StringComparison.Ordinal))
                    {
                        checkpoint.Passed = true;
                    }
                }
                if (line.StartsWith("ERROR:", StringComparison.Ordinal) ||
                    line.StartsWith("SCRIPT ERROR:", StringComparison.Ordinal) ||
                    line.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase))
                {
                    _engineErrors.Add(displayLine);
                }

                var eventIndex = line.IndexOf(EventPrefix, StringComparison.Ordinal);
                if (eventIndex >= 0)
                {
                    ParseEvent(line[(eventIndex + EventPrefix.Length)..]);
                }
            }
        }

        public SmokeEvidenceEvaluation Evaluate(ProductSmokeStatePolicyManifestEntry? policy)
        {
            lock (_gate)
            {
                var checkpointResults = _checkpoints.Values
                    .Select(checkpoint => new ProductSmokeCheckpointResult(
                        checkpoint.Id,
                        checkpoint.Passed,
                        checkpoint.Marker,
                        checkpoint.Message))
                    .ToArray();
                var errors = new List<string>();
                errors.AddRange(checkpointResults
                    .Where(checkpoint => !checkpoint.Success)
                    .Select(checkpoint =>
                        $"Checkpoint '{checkpoint.Id}' did not emit marker '{checkpoint.SuccessMarker}'."));
                errors.AddRange(_engineErrors);
                errors.AddRange(_protocolErrors);
                errors.AddRange(CompareState(policy));
                var firstMissing = checkpointResults.FirstOrDefault(checkpoint => !checkpoint.Success)?.Id;
                var failureStage = firstMissing is not null
                    ? $"checkpoint:{firstMissing}"
                    : _engineErrors.Count != 0
                        ? $"engine:{_activeStage ?? "process"}"
                        : _protocolErrors.Count != 0
                            ? $"protocol:{_activeStage ?? "process"}"
                            : errors.Count != 0 ? "state" : null;
                return new SmokeEvidenceEvaluation(checkpointResults, errors, failureStage);
            }
        }

        private void ParseEvent(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var kind = root.GetProperty("kind").GetString();
                switch (kind)
                {
                    case "started":
                        _activeStage = root.GetProperty("id").GetString();
                        break;
                    case "checkpoint":
                    {
                        var id = root.GetProperty("id").GetString() ?? string.Empty;
                        if (!_checkpoints.TryGetValue(id, out var checkpoint))
                        {
                            _protocolErrors.Add($"Unknown structured checkpoint '{id}'.");
                            break;
                        }
                        var success = !root.TryGetProperty("success", out var successElement) ||
                                      successElement.GetBoolean();
                        checkpoint.Passed = success;
                        checkpoint.Message = root.TryGetProperty("message", out var message)
                            ? message.GetString()
                            : null;
                        if (!success)
                        {
                            _protocolErrors.Add(
                                $"Checkpoint '{id}' failed: {checkpoint.Message ?? "no message"}.");
                        }
                        _activeStage = id;
                        break;
                    }
                    case "failed":
                    {
                        var id = root.TryGetProperty("id", out var idElement)
                            ? idElement.GetString() ?? "process"
                            : "process";
                        var message = root.TryGetProperty("message", out var messageElement)
                            ? messageElement.GetString() ?? "no message"
                            : "no message";
                        _activeStage = id;
                        _protocolErrors.Add($"Structured failure at '{id}': {message}");
                        break;
                    }
                    case "snapshot":
                    {
                        var stage = root.GetProperty("stage").GetString();
                        var state = root.GetProperty("state").Clone();
                        if (stage == "before")
                        {
                            _before = state;
                        }
                        else if (stage == "after")
                        {
                            _after = state;
                        }
                        else
                        {
                            _protocolErrors.Add($"Unknown snapshot stage '{stage}'.");
                        }
                        break;
                    }
                    default:
                        _protocolErrors.Add($"Unknown smoke event kind '{kind}'.");
                        break;
                }
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                _protocolErrors.Add($"Invalid structured smoke event: {exception.Message}");
            }
        }

        private IReadOnlyList<string> CompareState(ProductSmokeStatePolicyManifestEntry? policy)
        {
            if (policy is null ||
                (!policy.Required && policy.Compare.Count == 0 && policy.MetricGauges.Count == 0))
            {
                return [];
            }
            if (_before is null || _after is null)
            {
                return ["State policy requires both 'before' and 'after' structured snapshots."];
            }

            var errors = new List<string>();
            foreach (var section in policy.Compare)
            {
                if (!TryGetProperty(_before.Value, section, out var beforeSection) ||
                    !TryGetProperty(_after.Value, section, out var afterSection))
                {
                    errors.Add($"State snapshot is missing section '{section}'.");
                    continue;
                }
                if (!string.Equals(Canonicalize(beforeSection), Canonicalize(afterSection), StringComparison.Ordinal))
                {
                    errors.Add($"State section '{section}' did not return to its declared baseline.");
                }
            }

            foreach (var gauge in policy.MetricGauges)
            {
                if (!TryGetGauge(_before.Value, gauge, out var beforeGauge) ||
                    !TryGetGauge(_after.Value, gauge, out var afterGauge))
                {
                    errors.Add($"State snapshots are missing metric gauge '{gauge}'.");
                    continue;
                }
                if (!string.Equals(beforeGauge.GetRawText(), afterGauge.GetRawText(), StringComparison.Ordinal))
                {
                    errors.Add($"Metric gauge '{gauge}' did not return to its declared baseline.");
                }
            }
            return errors;
        }

        private static bool TryGetGauge(JsonElement state, string name, out JsonElement gauge)
        {
            gauge = default;
            return TryGetProperty(state, "metrics", out var metrics) &&
                   TryGetProperty(metrics, "gauges", out var gauges) &&
                   TryGetProperty(gauges, name, out gauge);
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            value = default;
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value);
        }

        private static string Canonicalize(JsonElement element)
        {
            var builder = new StringBuilder();
            AppendCanonical(element, builder);
            return builder.ToString();
        }

        private static void AppendCanonical(JsonElement element, StringBuilder builder)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    builder.Append('{');
                    foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                    {
                        builder.Append(JsonSerializer.Serialize(property.Name)).Append(':');
                        AppendCanonical(property.Value, builder);
                    }
                    builder.Append('}');
                    break;
                case JsonValueKind.Array:
                    builder.Append('[');
                    foreach (var item in element.EnumerateArray())
                    {
                        AppendCanonical(item, builder);
                        builder.Append(',');
                    }
                    builder.Append(']');
                    break;
                default:
                    builder.Append(element.GetRawText());
                    break;
            }
        }

        private sealed class ExpectedCheckpoint(string id, string marker)
        {
            public string Id { get; } = id;

            public string Marker { get; } = marker;

            public bool Passed { get; set; }

            public string? Message { get; set; }
        }
    }
}

internal sealed record ProductSmokeReport(
    string Schema,
    int SchemaVersion,
    DateTimeOffset ValidatedAtUtc,
    string Product,
    bool Success,
    SmokeCheck? Preparation,
    IReadOnlyList<ProductValidationPathMapping> Impact,
    IReadOnlyList<ProductSmokeResult> Scenarios);

internal sealed record ProductSmokeResult(
    string Id,
    bool Success,
    int ExitCode,
    string Argument,
    long DurationMs,
    long LogBytes,
    string LogPath,
    string? FailureStage,
    IReadOnlyList<ProductSmokeCheckpointResult> Checkpoints,
    IReadOnlyList<string> Tail,
    string? Error);

internal sealed record ProductSmokeCheckpointResult(
    string Id,
    bool Success,
    string SuccessMarker,
    string? Message);

internal sealed record SmokeEvidenceEvaluation(
    IReadOnlyList<ProductSmokeCheckpointResult> Checkpoints,
    IReadOnlyList<string> Errors,
    string? FailureStage);
