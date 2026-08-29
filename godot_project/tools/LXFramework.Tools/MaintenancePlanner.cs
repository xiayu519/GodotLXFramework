using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LXFramework.Tools;

internal static partial class MaintenancePlanner
{
    private const string PlanSchema = "lx.maintenance-plan";
    private const string TransactionSchema = "lx.maintenance-transaction";
    private const int SchemaVersion = 2;

    public static int Run(string root, string kind, IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 1 && arguments[0] == "--plan")
        {
            return WritePlan(root, kind, includeEnvironment: kind == "doctor");
        }
        if (arguments.Count == 2 && arguments[0] == "--apply")
        {
            return Apply(root, kind, arguments[1]);
        }
        if (arguments.Count == 2 && arguments[0] == "--rollback")
        {
            return Rollback(root, kind, arguments[1]);
        }
        if (arguments.Count == 2 && arguments[0] == "--recover")
        {
            return Recover(root, kind, arguments[1]);
        }

        Console.Error.WriteLine(
            $"{kind} usage: lx {kind} --plan | --apply <plan-id> | " +
            "--rollback <plan-id> | --recover <plan-id>");
        return 2;
    }

    public static MaintenancePlan BuildPlan(string root, string kind, bool includeEnvironment)
    {
        var actions = new List<MaintenanceAction>();
        var expected = ProjectGenerator.BuildOutputs(root);
        foreach (var output in expected)
        {
            var before = CurrentHash(output.Key);
            var after = HashText(output.Value);
            if (before == after)
            {
                continue;
            }

            actions.Add(new MaintenanceAction(
                $"write:{ToolFiles.Relative(root, output.Key)}",
                MaintenanceActionKind.WriteGenerated,
                $"Regenerate {ToolFiles.Relative(root, output.Key)}.",
                ToolFiles.Relative(root, output.Key),
                before,
                after,
                false,
                true));
        }
        foreach (var path in ProjectGenerator.FindOrphanedOutputs(root, expected.Keys))
        {
            actions.Add(new MaintenanceAction(
                $"delete:{ToolFiles.Relative(root, path)}",
                MaintenanceActionKind.DeleteGenerated,
                $"Remove orphaned generated output {ToolFiles.Relative(root, path)}.",
                ToolFiles.Relative(root, path),
                HashFile(path),
                null,
                false,
                true));
        }

        var blockers = new List<string>();
        if (includeEnvironment)
        {
            var dotnet = Doctor.ReadProcess("dotnet", "--version");
            if (!string.Equals(dotnet, Doctor.RequiredDotnetSdk, StringComparison.Ordinal))
            {
                blockers.Add($"Requires .NET SDK {Doctor.RequiredDotnetSdk}; found {dotnet ?? "none"}.");
                actions.Add(new MaintenanceAction(
                    "external:dotnet-sdk",
                    MaintenanceActionKind.ExternalInstall,
                    $"Install .NET SDK {Doctor.RequiredDotnetSdk}.",
                    null,
                    null,
                    null,
                    true,
                    false));
            }
            if (GodotLocator.Find(root, preferConsole: true) is null)
            {
                blockers.Add("Requires the exact Godot 4.7.2 .NET editor.");
                actions.Add(new MaintenanceAction(
                    "external:godot-editor",
                    MaintenanceActionKind.ExternalInstall,
                    "Install or configure the exact Godot 4.7.2 .NET editor.",
                    null,
                    null,
                    null,
                    true,
                    false));
            }
        }

        return new MaintenancePlan(
            PlanSchema,
            SchemaVersion,
            Guid.NewGuid().ToString("N"),
            kind,
            DateTimeOffset.UtcNow,
            blockers.Count == 0,
            blockers,
            actions);
    }

    public static string? ValidateTransactionEngine()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "lx-maintenance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        try
        {
            var existing = Path.Combine(testRoot, "existing.txt");
            var created = Path.Combine(testRoot, "created.txt");
            File.WriteAllText(existing, "before", Encoding.UTF8);
            var transactionRoot = Path.Combine(testRoot, "transaction");
            var actions = new[]
            {
                new PendingFileAction(existing, "after"),
                new PendingFileAction(created, "created"),
            };
            var entries = PrepareEntries(testRoot, transactionRoot, actions);

            // Simulate termination after the first mutation but before its journal
            // index is advanced. Recovery must infer the applied state from hashes.
            ApplyFileAction(testRoot, actions[0], entries[0]);
            if (!TryRestoreEntries(testRoot, transactionRoot, entries, out var recoveryConflicts) ||
                recoveryConflicts.Count != 0 ||
                File.ReadAllText(existing, Encoding.UTF8) != "before" ||
                File.Exists(created))
            {
                return "Maintenance interruption recovery did not restore the original files.";
            }

            entries = PrepareEntries(testRoot, transactionRoot, actions);
            ApplyFileAction(testRoot, actions[0], entries[0]);
            File.WriteAllText(existing, "user-edit", Encoding.UTF8);
            if (TryRestoreEntries(testRoot, transactionRoot, entries, out var conflicts) ||
                conflicts.Count != 1 ||
                File.ReadAllText(existing, Encoding.UTF8) != "user-edit")
            {
                return "Maintenance rollback overwrote or failed to detect a post-apply edit.";
            }

            return null;
        }
        catch (Exception exception)
        {
            return $"Maintenance transaction self-test failed: {exception.Message}";
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static int WritePlan(string root, string kind, bool includeEnvironment)
    {
        var plan = BuildPlan(root, kind, includeEnvironment);
        var output = PlanPath(root, plan.Id);
        ToolFiles.WriteJson(output, plan);
        Console.WriteLine(
            $"{kind} plan {plan.Id}: {plan.Actions.Count} actions, " +
            $"{plan.Blockers.Count} blockers -> {ToolFiles.Relative(root, output)}");
        return 0;
    }

    private static int Apply(string root, string kind, string planId)
    {
        if (!TryReadPlan(root, kind, planId, out var plan, out var error))
        {
            Console.Error.WriteLine(error);
            return error!.Contains("invalid plan id", StringComparison.Ordinal) ||
                   error.Contains("not found", StringComparison.Ordinal) ? 2 : 1;
        }
        if (!plan!.CanApply)
        {
            Console.Error.WriteLine(
                $"{kind}: plan '{planId}' has external blockers and cannot be applied automatically.");
            return 1;
        }

        var current = BuildPlan(root, kind, includeEnvironment: kind == "doctor");
        if (!EquivalentActions(plan.Actions, current.Actions))
        {
            Console.Error.WriteLine($"{kind}: plan '{planId}' is stale; create a new plan.");
            return 1;
        }

        var transactionPath = TransactionPath(root, planId);
        if (File.Exists(transactionPath))
        {
            var existing = ToolFiles.ReadJson<MaintenanceTransaction>(transactionPath);
            Console.Error.WriteLine(
                $"{kind}: transaction '{planId}' already exists in state {existing.State}; " +
                "use --rollback or --recover as appropriate.");
            return 1;
        }

        var expected = ProjectGenerator.BuildOutputs(root);
        var pending = plan.Actions
            .Where(action => action.Path is not null)
            .Select(action => new PendingFileAction(
                ResolveProjectPath(root, action.Path!),
                action.Kind == MaintenanceActionKind.WriteGenerated
                    ? expected[ResolveProjectPath(root, action.Path!)]
                    : null))
            .ToArray();
        var transactionRoot = TransactionRoot(root, planId);
        Directory.CreateDirectory(transactionRoot);
        MaintenanceTransaction? transaction = null;
        try
        {
            var entries = PrepareEntries(root, transactionRoot, pending);
            var now = DateTimeOffset.UtcNow;
            transaction = new MaintenanceTransaction(
                TransactionSchema,
                SchemaVersion,
                plan.Id,
                plan.Kind,
                now,
                now,
                null,
                MaintenanceTransactionState.Prepared,
                0,
                entries,
                null);
            WriteTransaction(root, transaction);

            for (var index = 0; index < pending.Length; index++)
            {
                transaction = transaction with
                {
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    State = MaintenanceTransactionState.Applying,
                    NextActionIndex = index,
                    Error = null,
                };
                WriteTransaction(root, transaction);
                ApplyFileAction(root, pending[index], entries[index]);
                transaction = transaction with
                {
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    NextActionIndex = index + 1,
                };
                WriteTransaction(root, transaction);
            }

            if (Validator.Run(root) != 0)
            {
                return RollBackFailedApply(
                    root,
                    transaction,
                    "Validation failed after applying the planned files.");
            }

            transaction = transaction with
            {
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                State = MaintenanceTransactionState.Applied,
                Error = null,
            };
            WriteTransaction(root, transaction);
            Console.WriteLine($"{kind}: plan '{planId}' applied and validated.");
            return 0;
        }
        catch (Exception exception)
        {
            if (transaction is not null)
            {
                try
                {
                    _ = RollBackFailedApply(root, transaction, exception.Message);
                }
                catch (Exception recoveryException)
                {
                    WriteTransaction(root, transaction with
                    {
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                        State = MaintenanceTransactionState.RecoveryRequired,
                        Error = $"{exception.Message}; recovery failed: {recoveryException.Message}",
                    });
                }
            }
            Console.Error.WriteLine(
                $"{kind}: apply failed; transaction is rolled back or marked RecoveryRequired: {exception.Message}");
            return 1;
        }
    }

    private static int RollBackFailedApply(
        string root,
        MaintenanceTransaction transaction,
        string error)
    {
        var transactionRoot = TransactionRoot(root, transaction.PlanId);
        if (!TryRestoreEntries(root, transactionRoot, transaction.Entries, out var conflicts))
        {
            var conflictError = error + " Recovery conflicts: " + string.Join("; ", conflicts);
            WriteTransaction(root, transaction with
            {
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                State = MaintenanceTransactionState.RecoveryRequired,
                Error = conflictError,
            });
            Console.Error.WriteLine(
                $"{transaction.Kind}: automatic rollback stopped to preserve post-apply edits; " +
                $"run --recover after resolving: {string.Join("; ", conflicts)}");
            return 1;
        }

        WriteTransaction(root, transaction with
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            State = MaintenanceTransactionState.RolledBack,
            Error = error,
        });
        return 1;
    }

    private static int Rollback(string root, string kind, string planId)
    {
        if (!TryReadTransaction(root, kind, planId, out var transaction, out var error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }
        if (transaction!.State != MaintenanceTransactionState.Applied)
        {
            Console.Error.WriteLine(
                $"{kind}: transaction '{planId}' is {transaction.State}; only Applied can use --rollback.");
            return 1;
        }

        return RestoreTransaction(root, transaction, "rolled back");
    }

    private static int Recover(string root, string kind, string planId)
    {
        if (!TryReadTransaction(root, kind, planId, out var transaction, out var error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }
        if (transaction!.State == MaintenanceTransactionState.Applied)
        {
            Console.Error.WriteLine($"{kind}: transaction '{planId}' is applied; use --rollback.");
            return 1;
        }
        if (transaction.State == MaintenanceTransactionState.RolledBack)
        {
            Console.WriteLine($"{kind}: transaction '{planId}' is already rolled back.");
            return 0;
        }

        return RestoreTransaction(root, transaction, "recovered and rolled back");
    }

    private static int RestoreTransaction(
        string root,
        MaintenanceTransaction transaction,
        string successMessage)
    {
        var transactionRoot = TransactionRoot(root, transaction.PlanId);
        if (!TryRestoreEntries(root, transactionRoot, transaction.Entries, out var conflicts))
        {
            var blocked = transaction with
            {
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                State = MaintenanceTransactionState.RecoveryRequired,
                Error = "Recovery conflicts: " + string.Join("; ", conflicts),
            };
            WriteTransaction(root, blocked);
            Console.Error.WriteLine(
                $"{transaction.Kind}: transaction '{transaction.PlanId}' requires manual conflict resolution: " +
                string.Join("; ", conflicts));
            return 1;
        }

        WriteTransaction(root, transaction with
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            State = MaintenanceTransactionState.RolledBack,
            Error = null,
        });
        Console.WriteLine($"{transaction.Kind}: transaction '{transaction.PlanId}' {successMessage}.");
        return 0;
    }

    private static IReadOnlyList<TransactionEntry> PrepareEntries(
        string root,
        string transactionRoot,
        IReadOnlyList<PendingFileAction> actions)
    {
        var entries = new List<TransactionEntry>(actions.Count);
        foreach (var action in actions)
        {
            var path = EnsureInsideRoot(root, action.Path);
            var relative = ToolFiles.Relative(root, path);
            var originalHash = CurrentHash(path);
            string? backupRelative = null;
            if (originalHash is not null)
            {
                var backup = Path.Combine(
                    transactionRoot,
                    "backup",
                    relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(path, backup, overwrite: true);
                backupRelative = Path.GetRelativePath(transactionRoot, backup)
                    .Replace(Path.DirectorySeparatorChar, '/');
            }

            entries.Add(new TransactionEntry(
                relative,
                originalHash is not null,
                originalHash,
                backupRelative,
                action.Content is null ? null : HashText(action.Content)));
        }

        return entries;
    }

    private static void ApplyFileAction(
        string root,
        PendingFileAction action,
        TransactionEntry entry)
    {
        var path = EnsureInsideRoot(root, action.Path);
        if (!string.Equals(ToolFiles.Relative(root, path), entry.Path, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Prepared transaction entry no longer matches its action path.");
        }
        if (CurrentHash(path) != entry.OriginalHash)
        {
            throw new InvalidOperationException(
                $"Transaction target '{entry.Path}' changed after preparation and before mutation.");
        }

        if (action.Content is null)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        else
        {
            ToolFiles.WriteText(path, action.Content);
        }

        if (CurrentHash(path) != entry.AppliedHash)
        {
            throw new IOException($"Transaction target '{entry.Path}' did not reach its planned hash.");
        }
    }

    private static bool TryRestoreEntries(
        string root,
        string transactionRoot,
        IReadOnlyList<TransactionEntry> entries,
        out IReadOnlyList<string> conflicts)
    {
        var detected = new List<string>();
        foreach (var entry in entries)
        {
            var current = CurrentHash(ResolveProjectPath(root, entry.Path));
            if (current != entry.OriginalHash && current != entry.AppliedHash)
            {
                detected.Add($"{entry.Path} has unexpected hash {current ?? "<missing>"}");
            }
        }
        if (detected.Count != 0)
        {
            conflicts = detected;
            return false;
        }

        for (var index = entries.Count - 1; index >= 0; index--)
        {
            var entry = entries[index];
            var target = ResolveProjectPath(root, entry.Path);
            if (CurrentHash(target) == entry.OriginalHash)
            {
                continue;
            }
            if (!entry.OriginalExisted)
            {
                if (File.Exists(target))
                {
                    File.Delete(target);
                }
                continue;
            }

            var backup = EnsureInsideRoot(
                transactionRoot,
                Path.Combine(
                    transactionRoot,
                    (entry.BackupPath ?? throw new InvalidDataException("Transaction backup path is missing."))
                        .Replace('/', Path.DirectorySeparatorChar)));
            if (HashFile(backup) != entry.OriginalHash)
            {
                throw new InvalidDataException($"Transaction backup for '{entry.Path}' failed its hash check.");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(backup, target, overwrite: true);
        }

        conflicts = [];
        return true;
    }

    private static bool TryReadPlan(
        string root,
        string kind,
        string planId,
        out MaintenancePlan? plan,
        out string? error)
    {
        plan = null;
        if (!PlanIdRegex().IsMatch(planId))
        {
            error = $"{kind}: invalid plan id '{planId}'.";
            return false;
        }
        var path = PlanPath(root, planId);
        if (!File.Exists(path))
        {
            error = $"{kind}: plan '{planId}' was not found.";
            return false;
        }

        plan = ToolFiles.ReadJson<MaintenancePlan>(path);
        if (plan.Schema != PlanSchema || plan.SchemaVersion != SchemaVersion || plan.Kind != kind)
        {
            error = $"{kind}: plan '{planId}' has an incompatible schema or kind.";
            plan = null;
            return false;
        }
        error = null;
        return true;
    }

    private static bool TryReadTransaction(
        string root,
        string kind,
        string planId,
        out MaintenanceTransaction? transaction,
        out string? error)
    {
        transaction = null;
        if (!PlanIdRegex().IsMatch(planId))
        {
            error = $"{kind}: invalid plan id '{planId}'.";
            return false;
        }
        var path = TransactionPath(root, planId);
        if (!File.Exists(path))
        {
            error = $"{kind}: transaction '{planId}' was not found.";
            return false;
        }

        transaction = ToolFiles.ReadJson<MaintenanceTransaction>(path);
        if (transaction.Schema != TransactionSchema ||
            transaction.SchemaVersion != SchemaVersion ||
            transaction.Kind != kind ||
            transaction.PlanId != planId)
        {
            error = $"{kind}: transaction '{planId}' has an incompatible schema, kind, or ID.";
            transaction = null;
            return false;
        }
        error = null;
        return true;
    }

    private static void WriteTransaction(string root, MaintenanceTransaction transaction) =>
        ToolFiles.WriteJson(TransactionPath(root, transaction.PlanId), transaction);

    private static bool EquivalentActions(
        IReadOnlyList<MaintenanceAction> left,
        IReadOnlyList<MaintenanceAction> right) =>
        JsonSerializer.Serialize(left, ToolFiles.JsonOptions) ==
        JsonSerializer.Serialize(right, ToolFiles.JsonOptions);

    private static string ResolveProjectPath(string root, string relativePath) =>
        EnsureInsideRoot(
            root,
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string EnsureInsideRoot(string root, string candidate)
    {
        var absoluteRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var absolute = Path.GetFullPath(candidate);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!absolute.StartsWith(absoluteRoot + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidDataException($"Maintenance path '{candidate}' resolves outside '{root}'.");
        }
        return absolute;
    }

    private static string? CurrentHash(string path) => File.Exists(path) ? HashFile(path) : null;

    private static string HashFile(string path) => HashText(File.ReadAllText(path));

    private static string HashText(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            content.Replace("\r\n", "\n", StringComparison.Ordinal)))).ToLowerInvariant();

    private static string PlanPath(string root, string id) =>
        Path.Combine(root, ".lx", "plans", id + ".json");

    private static string TransactionRoot(string root, string id) =>
        Path.Combine(root, ".lx", "transactions", id);

    private static string TransactionPath(string root, string id) =>
        Path.Combine(TransactionRoot(root, id), "transaction.json");

    [GeneratedRegex("^[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex PlanIdRegex();
}

internal enum MaintenanceActionKind
{
    WriteGenerated,
    DeleteGenerated,
    ExternalInstall,
}

internal enum MaintenanceTransactionState
{
    Prepared,
    Applying,
    Applied,
    RolledBack,
    RecoveryRequired,
}

internal sealed record MaintenancePlan(
    string Schema,
    int SchemaVersion,
    string Id,
    string Kind,
    DateTimeOffset CreatedAtUtc,
    bool CanApply,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<MaintenanceAction> Actions);

internal sealed record MaintenanceAction(
    string Id,
    MaintenanceActionKind Kind,
    string Description,
    string? Path,
    string? BeforeHash,
    string? AfterHash,
    bool RequiresApproval,
    bool Reversible);

internal sealed record MaintenanceTransaction(
    string Schema,
    int SchemaVersion,
    string PlanId,
    string Kind,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    MaintenanceTransactionState State,
    int NextActionIndex,
    IReadOnlyList<TransactionEntry> Entries,
    string? Error);

internal sealed record TransactionEntry(
    string Path,
    bool OriginalExisted,
    string? OriginalHash,
    string? BackupPath,
    string? AppliedHash);

internal sealed record PendingFileAction(string Path, string? Content);
