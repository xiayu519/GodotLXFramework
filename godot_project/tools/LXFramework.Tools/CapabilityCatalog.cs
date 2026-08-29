using System.Security.Cryptography;
using System.Text;

namespace LXFramework.Tools;

internal static class CapabilityCatalog
{
    private const string Schema = "lx.capability-catalog";
    private const int SchemaVersion = 1;

    public static int Run(string root, IReadOnlyList<string> arguments)
    {
        if (arguments.Count > 1)
        {
            return Usage();
        }

        var catalog = Build(root);
        var fullPath = Path.Combine(root, ".lx", "capabilities.json");
        ToolFiles.WriteJson(fullPath, catalog);
        if (arguments.Count == 0)
        {
            Console.WriteLine(
                $"capabilities: {catalog.Capabilities.Count} modules, " +
                $"{catalog.Capabilities.Sum(capability => capability.Commands.Count)} commands -> " +
                ToolFiles.Relative(root, fullPath));
            return 0;
        }

        var id = arguments[0].ToLowerInvariant();
        var capability = catalog.Capabilities.SingleOrDefault(candidate => candidate.Id == id);
        if (capability is null)
        {
            Console.Error.WriteLine(
                $"capabilities: unknown capability '{id}'. Available: " +
                string.Join(", ", catalog.Capabilities.Select(candidate => candidate.Id)));
            return 2;
        }

        var output = Path.Combine(root, ".lx", $"capability-{id}.json");
        ToolFiles.WriteJson(output, capability);
        Console.WriteLine(
            $"capability '{id}': {capability.Commands.Count} commands -> " +
            ToolFiles.Relative(root, output));
        return 0;
    }

    public static CapabilityCatalogDocument Build(string root)
    {
        var capabilities = new[]
        {
            new CapabilityDescriptor(
                "project",
                CapabilityState.Available,
                "读取结构、生成派生代码并执行分层验收。",
                [
                    Command("inspect", "lx inspect [--full]", CapabilityCommandKind.LocalArtifact,
                        ["writes-.lx-project-index"], ["project-root-valid"], "project-index-written"),
                    Command("capabilities", "lx capabilities [capability-id]", CapabilityCommandKind.LocalArtifact,
                        ["writes-.lx-capability-catalog"], ["project-root-valid"], "capability-catalog-written"),
                    Command("generate", "lx generate", CapabilityCommandKind.ProjectMutation,
                        ["updates-generated-code"], ["manifests-valid"], "generated-output-current"),
                    Command("check", "lx check <changed-path> [...]", CapabilityCommandKind.ProjectMutation,
                        ["may-update-generated-code", "runs-selected-gates"], ["changed-paths-provided"], "changed-paths-pass"),
                    Command("validate", "lx validate", CapabilityCommandKind.ProjectMutation,
                        ["updates-luban-generated-output", "may-import-godot-uids", "writes-.lx-validation-reports"],
                        ["toolchain-available"], "full-validation-pass"),
                    Command("api", "lx api check", CapabilityCommandKind.ReadOnly,
                        [], [], "public-api-compatible"),
                    Command("api", "lx api update", CapabilityCommandKind.ProjectMutation,
                        ["updates-public-api-baseline"], ["api-change-reviewed"], "public-api-compatible"),
                ]),
            new CapabilityDescriptor(
                "scaffolding",
                CapabilityState.Available,
                "从框架事实源创建产品、世界、功能、界面、内容、输入、资源和节点。",
                [
                    Command("create", "lx create <kind> ...", CapabilityCommandKind.ProjectMutation,
                        ["creates-source", "updates-manifest", "updates-generated-code"],
                        ["kind-and-name-valid"], "scaffold-check-pass"),
                    Command("data", "lx data", CapabilityCommandKind.ProjectMutation,
                        ["updates-luban-generated-code", "updates-luban-binary-data"],
                        ["pinned-luban-toolchain"], "luban-deterministic"),
                ]),
            new CapabilityDescriptor(
                "runtime",
                CapabilityState.Available,
                "启动 Godot、执行 headless 验收并读取当前 Editor/Debug 会话。",
                [
                    Command("run", "lx run [--headless] [godot args]", CapabilityCommandKind.UserAction,
                        ["starts-godot-process"], ["godot-4.7.2-dotnet"], "godot-process-result"),
                    Command("smoke", "lx smoke", CapabilityCommandKind.ProjectMutation,
                        ["may-import-godot-uids", "writes-.lx-smoke-report"],
                        ["godot-4.7.2-dotnet"], "runtime-smoke-pass"),
                    Command("runtime", "lx runtime status", CapabilityCommandKind.LocalArtifact,
                        ["writes-.lx-runtime-status"], ["debug-runtime-live"], "runtime-session-current"),
                    Command("runtime", "lx runtime snapshot [section]", CapabilityCommandKind.LocalArtifact,
                        ["writes-.lx-runtime-snapshot"], ["debug-runtime-live"], "runtime-snapshot-current"),
                ],
                ["all", "runtime", "events", "scheduler", "actions", "metrics", "resources",
                    "ui", "features", "audio", "input", "localization", "settings", "logs"]),
            new CapabilityDescriptor(
                "maintenance",
                CapabilityState.Available,
                "检查环境，并以可审查计划执行项目内修复或升级。",
                [
                    Command("doctor", "lx doctor", CapabilityCommandKind.LocalArtifact,
                        ["writes-.lx-doctor-report"], [], "doctor-report-complete"),
                    Command("doctor", "lx doctor --plan", CapabilityCommandKind.LocalArtifact,
                        ["writes-.lx-repair-plan"], [], "repair-plan-valid"),
                    Command("doctor", "lx doctor --apply <plan-id>", CapabilityCommandKind.ProjectMutation,
                        ["updates-planned-generated-files", "writes-.lx-transaction-journal"],
                        ["plan-current", "no-external-blockers"], "repair-verified-or-rolled-back"),
                    Command("doctor", "lx doctor --rollback|--recover <plan-id>", CapabilityCommandKind.ProjectMutation,
                        ["may-restore-planned-generated-files", "updates-.lx-transaction-journal"],
                        ["transaction-present"], "repair-verified-or-rolled-back"),
                    Command("upgrade", "lx upgrade --plan", CapabilityCommandKind.LocalArtifact,
                        ["writes-.lx-upgrade-plan"], [], "upgrade-plan-valid"),
                    Command("upgrade", "lx upgrade --apply <plan-id>", CapabilityCommandKind.ProjectMutation,
                        ["updates-current-checkout-derived-state", "writes-.lx-transaction-journal"],
                        ["plan-current"], "upgrade-verified-or-rolled-back"),
                    Command("upgrade", "lx upgrade --rollback|--recover <plan-id>", CapabilityCommandKind.ProjectMutation,
                        ["may-restore-current-checkout-derived-state", "updates-.lx-transaction-journal"],
                        ["transaction-present"], "upgrade-verified-or-rolled-back"),
                ]),
            new CapabilityDescriptor(
                "quality",
                CapabilityState.Available,
                "视觉、性能与发行门禁。",
                [
                    Command("visual", "lx visual capture|compare", CapabilityCommandKind.LocalArtifact,
                        ["writes-.lx-visual-artifacts"],
                        ["godot-4.7.2-dotnet"], "visual-result-explicit"),
                    Command("visual", "lx visual approve", CapabilityCommandKind.ProjectMutation,
                        ["updates-visual-baseline", "writes-.lx-visual-artifacts"],
                        ["godot-4.7.2-dotnet"], "visual-result-explicit"),
                    Command("benchmark", "lx benchmark", CapabilityCommandKind.LocalArtifact,
                        ["writes-.lx-benchmark-report"], [], "benchmark-gates-pass"),
                    Command("soak", "lx soak [cycles]", CapabilityCommandKind.ProjectMutation,
                        ["may-import-godot-uids", "writes-.lx-soak-reports"],
                        ["godot-4.7.2-dotnet"], "soak-pass"),
                    Command("export", "lx export windows", CapabilityCommandKind.ProjectMutation,
                        ["writes-release-build"], ["export-templates-installed"], "export-smoke-pass"),
                ]),
            new CapabilityDescriptor(
                "actions",
                CapabilityState.Available,
                "由 LifetimeScope 持有、可取消和可观测的纯 C# 动作编排。",
                [],
                ["sequence", "parallel", "race", "invoke", "async", "delay", "timeout", "retry", "finally"]),
        };
        var recipes = new[]
        {
            Recipe("project-index-written", ["command-success", "artifact-schema-valid"]),
            Recipe("capability-catalog-written", ["command-success", "catalog-schema-valid"]),
            Recipe("generated-output-current", ["command-success", "validate-generated-pass"]),
            Recipe("changed-paths-pass", ["command-success", "selected-gates-pass"]),
            Recipe("full-validation-pass", ["command-success", "workflow-pass", "data-pass", "tests-pass", "benchmark-pass", "smoke-pass", "visual-pass"]),
            Recipe("public-api-compatible", ["baseline-present", "public-signatures-match"]),
            Recipe("scaffold-check-pass", ["command-success", "manifest-registered", "generated-output-current"]),
            Recipe("luban-deterministic", ["command-success", "input-hash-current", "negative-fixtures-rejected"]),
            Recipe("godot-process-result", ["process-exit-observed"]),
            Recipe("runtime-smoke-pass", ["command-success", "all-scenarios-pass"]),
            Recipe("runtime-session-current", ["process-live", "heartbeat-current", "session-generation-current"]),
            Recipe("runtime-snapshot-current", ["response-success", "session-generation-current", "payload-complete"]),
            Recipe("doctor-report-complete", ["checks-observed", "missing-list-complete"]),
            Recipe("repair-plan-valid", ["actions-bounded", "side-effects-declared", "hashes-recorded"]),
            Recipe("upgrade-plan-valid", ["actions-bounded", "side-effects-declared", "hashes-recorded"]),
            Recipe("repair-verified-or-rolled-back", ["plan-current", "validator-pass", "transaction-terminal"]),
            Recipe("upgrade-verified-or-rolled-back", ["plan-current", "validator-pass", "transaction-terminal"]),
            Recipe("visual-result-explicit", ["capture-produced", "comparison-result-observed"]),
            Recipe("benchmark-gates-pass", ["command-success", "allocation-gates-pass"]),
            Recipe("soak-pass", ["requested-cycles-complete", "all-smoke-cycles-pass"]),
            Recipe("export-smoke-pass", ["export-produced", "framework-smoke-pass", "product-smoke-pass"]),
        };
        return new CapabilityCatalogDocument(
            Schema,
            SchemaVersion,
            "LXFramework",
            "Godot 4.7.2 .NET",
            SourceHash(root),
            capabilities,
            recipes);
    }

    public static void Write(string root) =>
        ToolFiles.WriteJson(Path.Combine(root, ".lx", "capabilities.json"), Build(root));

    private static CapabilityCommand Command(
        string rootCommand,
        string invocation,
        CapabilityCommandKind kind,
        IReadOnlyList<string> sideEffects,
        IReadOnlyList<string> preconditions,
        string verifyRecipe) =>
        new(rootCommand, invocation, kind, sideEffects, preconditions, verifyRecipe);

    private static VerifyRecipe Recipe(string id, IReadOnlyList<string> gates) => new(id, gates);

    private static string SourceHash(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var relative in new[]
                 {
                     "tools/LXFramework.Tools/CapabilityCatalog.cs",
                     "tools/LXFramework.Tools/Program.cs",
                     "lx.ps1",
                 })
        {
            var bytes = Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(root, relative)));
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static int Usage()
    {
        Console.Error.WriteLine("capabilities usage: lx capabilities [capability-id]");
        return 2;
    }
}

internal enum CapabilityState
{
    Available,
    Experimental,
    Deferred,
}

internal enum CapabilityCommandKind
{
    ReadOnly,
    LocalArtifact,
    ProjectMutation,
    UserAction,
    ExternalMutation,
    Destructive,
}

internal sealed record CapabilityCatalogDocument(
    string Schema,
    int SchemaVersion,
    string Framework,
    string EngineBaseline,
    string SourceHash,
    IReadOnlyList<CapabilityDescriptor> Capabilities,
    IReadOnlyList<VerifyRecipe> VerifyRecipes);

internal sealed record CapabilityDescriptor(
    string Id,
    CapabilityState State,
    string Description,
    IReadOnlyList<CapabilityCommand> Commands,
    IReadOnlyList<string>? Snapshots = null);

internal sealed record CapabilityCommand(
    string RootCommand,
    string Invocation,
    CapabilityCommandKind Kind,
    IReadOnlyList<string> SideEffects,
    IReadOnlyList<string> Preconditions,
    string VerifyRecipe);

internal sealed record VerifyRecipe(string Id, IReadOnlyList<string> Gates);
