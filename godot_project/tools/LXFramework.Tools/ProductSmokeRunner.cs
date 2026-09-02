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
            4,
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
            4,
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
            evaluation.Performance,
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
            PerformanceChecks =
            [
                new ProductSmokePerformanceCheckManifestEntry
                {
                    Id = "steady_state",
                    MinSamples = 60,
                    MaxP95HostWorkMilliseconds = 2,
                    MaxManagedHeapGrowthBytes = 1_024,
                    MaxAllocatedBytes = 2_048,
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
        const string performanceBefore = "{\"windowSeconds\":15,\"frames\":{\"sampleCount\":60,\"hostWorkMilliseconds\":{\"p95\":1,\"p99\":1.2,\"maximum\":1.4}},\"physicsFrames\":{\"sampleCount\":60,\"hostWorkMilliseconds\":{\"p95\":0.5,\"p99\":0.7,\"maximum\":0.8}},\"memory\":{\"totalAllocatedBytes\":1000,\"managedHeapBytes\":2000}}";
        const string performanceAfter = "{\"windowSeconds\":15,\"frames\":{\"sampleCount\":120,\"hostWorkMilliseconds\":{\"p95\":1.5,\"p99\":1.7,\"maximum\":1.9}},\"physicsFrames\":{\"sampleCount\":120,\"hostWorkMilliseconds\":{\"p95\":0.6,\"p99\":0.8,\"maximum\":0.9}},\"memory\":{\"totalAllocatedBytes\":2500,\"managedHeapBytes\":2500}}";
        scanner.Scan("fixture", $"LX_SMOKE_EVENT {{\"kind\":\"performance\",\"id\":\"steady_state\",\"stage\":\"before\",\"sample\":{performanceBefore}}}");
        scanner.Scan("fixture", $"LX_SMOKE_EVENT {{\"kind\":\"performance\",\"id\":\"steady_state\",\"stage\":\"after\",\"sample\":{performanceAfter}}}");
        scanner.Scan("duplicate-log", $"LX_SMOKE_EVENT {{\"kind\":\"performance\",\"id\":\"steady_state\",\"stage\":\"before\",\"sample\":{performanceBefore}}}");
        scanner.Scan("duplicate-log", $"LX_SMOKE_EVENT {{\"kind\":\"performance\",\"id\":\"steady_state\",\"stage\":\"after\",\"sample\":{performanceAfter}}}");
        var evaluation = scanner.Evaluate(new ProductSmokeStatePolicyManifestEntry
        {
            Required = true,
            Compare = ["resources"],
            MetricGauges = ["product.pool.borrowed"],
        });
        if (evaluation.Errors.Count != 0 ||
            evaluation.Checkpoints.Count != 2 ||
            evaluation.Checkpoints.Any(checkpoint => !checkpoint.Success) ||
            evaluation.Performance.Count != 1 ||
            evaluation.Performance.Any(performance => !performance.Success))
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

        var overBudget = new SmokeEvidenceScanner(smoke);
        overBudget.Scan("fixture", "LX_SMOKE_EVENT {\"kind\":\"checkpoint\",\"id\":\"loaded\",\"success\":true}");
        overBudget.Scan("fixture", "FIXTURE_PASS");
        overBudget.Scan("fixture", $"LX_SMOKE_EVENT {{\"kind\":\"performance\",\"id\":\"steady_state\",\"stage\":\"before\",\"sample\":{performanceBefore}}}");
        var slowPerformance = performanceAfter.Replace("\"p95\":1.5", "\"p95\":3", StringComparison.Ordinal);
        overBudget.Scan("fixture", $"LX_SMOKE_EVENT {{\"kind\":\"performance\",\"id\":\"steady_state\",\"stage\":\"after\",\"sample\":{slowPerformance}}}");
        var overBudgetEvaluation = overBudget.Evaluate(null);
        if (overBudgetEvaluation.Errors.Count == 0 ||
            overBudgetEvaluation.FailureStage != "performance:steady_state" ||
            overBudgetEvaluation.Performance.Single().Success)
        {
            return "Structured product smoke protocol accepted an over-budget performance sample.";
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
        private readonly Dictionary<string, ExpectedPerformance> _performance;
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
            _performance = smoke.PerformanceChecks.ToDictionary(
                check => check.Id,
                check => new ExpectedPerformance(check),
                StringComparer.Ordinal);
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
                var performanceResults = EvaluatePerformance();
                errors.AddRange(performanceResults.SelectMany(result => result.Errors));
                var firstMissing = checkpointResults.FirstOrDefault(checkpoint => !checkpoint.Success)?.Id;
                var failureStage = firstMissing is not null
                    ? $"checkpoint:{firstMissing}"
                    : _engineErrors.Count != 0
                        ? $"engine:{_activeStage ?? "process"}"
                        : _protocolErrors.Count != 0
                            ? $"protocol:{_activeStage ?? "process"}"
                            : performanceResults.FirstOrDefault(result => !result.Success) is { } failedPerformance
                                ? $"performance:{failedPerformance.Id}"
                                : errors.Count != 0 ? "state" : null;
                return new SmokeEvidenceEvaluation(
                    checkpointResults,
                    performanceResults,
                    errors,
                    failureStage);
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
                    case "performance":
                    {
                        var id = root.GetProperty("id").GetString() ?? string.Empty;
                        if (!_performance.TryGetValue(id, out var performance))
                        {
                            _protocolErrors.Add($"Unknown performance check '{id}'.");
                            break;
                        }
                        var stage = root.GetProperty("stage").GetString();
                        var sample = root.GetProperty("sample").Clone();
                        if (stage == "before")
                        {
                            if (performance.Before is { } existingBefore &&
                                !string.Equals(
                                    Canonicalize(existingBefore),
                                    Canonicalize(sample),
                                    StringComparison.Ordinal))
                            {
                                _protocolErrors.Add($"Performance check '{id}' emitted conflicting before samples.");
                            }
                            performance.Before = sample;
                        }
                        else if (stage == "after")
                        {
                            if (performance.After is { } existingAfter &&
                                !string.Equals(
                                    Canonicalize(existingAfter),
                                    Canonicalize(sample),
                                    StringComparison.Ordinal))
                            {
                                _protocolErrors.Add($"Performance check '{id}' emitted conflicting after samples.");
                            }
                            performance.After = sample;
                        }
                        else
                        {
                            _protocolErrors.Add($"Unknown performance stage '{stage}' for '{id}'.");
                        }
                        _activeStage = id;
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

        private IReadOnlyList<ProductSmokePerformanceResult> EvaluatePerformance() =>
            _performance.Values.Select(EvaluatePerformance).ToArray();

        private static ProductSmokePerformanceResult EvaluatePerformance(ExpectedPerformance expected)
        {
            var errors = new List<string>();
            if (expected.Before is null || expected.After is null)
            {
                errors.Add(
                    $"Performance check '{expected.Manifest.Id}' requires both before and after samples.");
                return new ProductSmokePerformanceResult(
                    expected.Manifest.Id,
                    false,
                    expected.Manifest.SampleSource,
                    0,
                    null,
                    null,
                    null,
                    null,
                    null,
                    errors);
            }

            var before = expected.Before.Value;
            var after = expected.After.Value;
            if (!TryGetDouble(before, "windowSeconds", out var beforeWindowSeconds) ||
                !TryGetDouble(after, "windowSeconds", out var afterWindowSeconds) ||
                Math.Abs(beforeWindowSeconds - expected.Manifest.WindowSeconds) > 0.001 ||
                Math.Abs(afterWindowSeconds - expected.Manifest.WindowSeconds) > 0.001)
            {
                errors.Add(
                    $"Performance check '{expected.Manifest.Id}' expected a " +
                    $"{expected.Manifest.WindowSeconds:0.###} second sample window.");
            }

            var sourceName = expected.Manifest.SampleSource == "Frames" ? "frames" : "physicsFrames";
            var sampleCount = 0;
            var p95 = 0.0;
            var p99 = 0.0;
            var maximum = 0.0;
            var hasFrameData = TryGetProperty(after, sourceName, out var frames) &&
                               TryGetInt32(frames, "sampleCount", out sampleCount) &&
                               TryGetProperty(frames, "hostWorkMilliseconds", out var hostWork) &&
                               TryGetDouble(hostWork, "p95", out p95) &&
                               TryGetDouble(hostWork, "p99", out p99) &&
                               TryGetDouble(hostWork, "maximum", out maximum);
            if (!hasFrameData)
            {
                errors.Add($"Performance check '{expected.Manifest.Id}' sample is missing frame statistics.");
            }
            else
            {
                if (sampleCount < expected.Manifest.MinSamples)
                {
                    errors.Add(
                        $"Performance check '{expected.Manifest.Id}' captured {sampleCount} samples; " +
                        $"at least {expected.Manifest.MinSamples} are required.");
                }
                AddMaximumError(
                    expected.Manifest.Id,
                    "p95 host work",
                    p95,
                    expected.Manifest.MaxP95HostWorkMilliseconds,
                    errors);
                AddMaximumError(
                    expected.Manifest.Id,
                    "p99 host work",
                    p99,
                    expected.Manifest.MaxP99HostWorkMilliseconds,
                    errors);
                AddMaximumError(
                    expected.Manifest.Id,
                    "maximum host work",
                    maximum,
                    expected.Manifest.MaxHostWorkMilliseconds,
                    errors);
            }

            long? heapGrowth = null;
            long? allocatedBytes = null;
            if (TryGetProperty(before, "memory", out var beforeMemory) &&
                TryGetProperty(after, "memory", out var afterMemory) &&
                TryGetInt64(beforeMemory, "managedHeapBytes", out var beforeHeap) &&
                TryGetInt64(afterMemory, "managedHeapBytes", out var afterHeap) &&
                TryGetInt64(beforeMemory, "totalAllocatedBytes", out var beforeAllocated) &&
                TryGetInt64(afterMemory, "totalAllocatedBytes", out var afterAllocated))
            {
                heapGrowth = afterHeap - beforeHeap;
                allocatedBytes = afterAllocated - beforeAllocated;
                if (allocatedBytes < 0)
                {
                    errors.Add(
                        $"Performance check '{expected.Manifest.Id}' total allocation counter moved backwards.");
                }
                if (expected.Manifest.MaxManagedHeapGrowthBytes is { } heapLimit && heapGrowth > heapLimit)
                {
                    errors.Add(
                        $"Performance check '{expected.Manifest.Id}' managed heap grew by {heapGrowth} bytes; " +
                        $"the budget is {heapLimit} bytes.");
                }
                if (expected.Manifest.MaxAllocatedBytes is { } allocationLimit && allocatedBytes > allocationLimit)
                {
                    errors.Add(
                        $"Performance check '{expected.Manifest.Id}' allocated {allocatedBytes} bytes; " +
                        $"the budget is {allocationLimit} bytes.");
                }
            }
            else
            {
                errors.Add($"Performance check '{expected.Manifest.Id}' sample is missing memory counters.");
            }

            return new ProductSmokePerformanceResult(
                expected.Manifest.Id,
                errors.Count == 0,
                expected.Manifest.SampleSource,
                sampleCount,
                hasFrameData ? p95 : null,
                hasFrameData ? p99 : null,
                hasFrameData ? maximum : null,
                heapGrowth,
                allocatedBytes,
                errors);
        }

        private static void AddMaximumError(
            string id,
            string metric,
            double value,
            double? limit,
            ICollection<string> errors)
        {
            if (limit is { } maximum && value > maximum)
            {
                errors.Add(
                    $"Performance check '{id}' {metric} was {value:0.###} ms; " +
                    $"the budget is {maximum:0.###} ms.");
            }
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

        private static bool TryGetDouble(JsonElement element, string name, out double value)
        {
            value = default;
            return TryGetProperty(element, name, out var property) && property.TryGetDouble(out value);
        }

        private static bool TryGetInt32(JsonElement element, string name, out int value)
        {
            value = default;
            return TryGetProperty(element, name, out var property) && property.TryGetInt32(out value);
        }

        private static bool TryGetInt64(JsonElement element, string name, out long value)
        {
            value = default;
            return TryGetProperty(element, name, out var property) && property.TryGetInt64(out value);
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

        private sealed class ExpectedPerformance(ProductSmokePerformanceCheckManifestEntry manifest)
        {
            public ProductSmokePerformanceCheckManifestEntry Manifest { get; } = manifest;

            public JsonElement? Before { get; set; }

            public JsonElement? After { get; set; }
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
    IReadOnlyList<ProductSmokePerformanceResult> Performance,
    IReadOnlyList<string> Tail,
    string? Error);

internal sealed record ProductSmokeCheckpointResult(
    string Id,
    bool Success,
    string SuccessMarker,
    string? Message);

internal sealed record ProductSmokePerformanceResult(
    string Id,
    bool Success,
    string SampleSource,
    int SampleCount,
    double? P95HostWorkMilliseconds,
    double? P99HostWorkMilliseconds,
    double? MaxHostWorkMilliseconds,
    long? ManagedHeapGrowthBytes,
    long? AllocatedBytes,
    IReadOnlyList<string> Errors);

internal sealed record SmokeEvidenceEvaluation(
    IReadOnlyList<ProductSmokeCheckpointResult> Checkpoints,
    IReadOnlyList<ProductSmokePerformanceResult> Performance,
    IReadOnlyList<string> Errors,
    string? FailureStage);
