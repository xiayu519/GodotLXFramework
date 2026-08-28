using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LX.Core.Common;

namespace LX.Core.Persistence;

public sealed partial class SaveStore<TState>
{
    private sealed class SaveEnvelope
    {
        public int SchemaVersion { get; set; }

        public DateTimeOffset SavedAtUtc { get; set; }

        public string PayloadJson { get; set; } = string.Empty;

        public string Checksum { get; set; } = string.Empty;
    }

    private readonly string _directory;
    private readonly int _currentVersion;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Dictionary<int, ISaveMigration> _migrations;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public SaveStore(
        string directory,
        int currentVersion,
        IEnumerable<ISaveMigration>? migrations = null,
        JsonSerializerOptions? jsonOptions = null)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("A save directory is required.", nameof(directory));
        }

        _directory = Path.GetFullPath(directory);
        _currentVersion = currentVersion > 0
            ? currentVersion
            : throw new ArgumentOutOfRangeException(nameof(currentVersion));
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };
        _migrations = (migrations ?? [])
            .ToDictionary(migration => migration.FromVersion);

        foreach (var migration in _migrations.Values)
        {
            if (migration.ToVersion != migration.FromVersion + 1)
            {
                throw new ArgumentException(
                    $"Save migration {migration.FromVersion}->{migration.ToVersion} must advance exactly one version.",
                    nameof(migrations));
            }
        }
    }

    public async ValueTask SaveAsync(string slot, TState state, CancellationToken cancellationToken = default)
    {
        ValidateSlot(slot);
        ArgumentNullException.ThrowIfNull(state);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await SaveExclusiveAsync(slot, state, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async ValueTask SaveExclusiveAsync(
        string slot,
        TState state,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);

        var payloadJson = JsonSerializer.Serialize(state, _jsonOptions);
        var envelope = new SaveEnvelope
        {
            SchemaVersion = _currentVersion,
            SavedAtUtc = DateTimeOffset.UtcNow,
            PayloadJson = payloadJson,
            Checksum = ComputeChecksum(payloadJson),
        };
        var serialized = JsonSerializer.Serialize(envelope, _jsonOptions);
        var primaryPath = GetPrimaryPath(slot);
        var backupPath = GetBackupPath(slot);
        var temporaryPath = primaryPath + $".tmp.{Guid.NewGuid():N}";

        try
        {
            await File.WriteAllTextAsync(temporaryPath, serialized, new UTF8Encoding(false), cancellationToken);
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Open,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             4096,
                             FileOptions.WriteThrough))
            {
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(primaryPath))
            {
                try
                {
                    File.Replace(temporaryPath, primaryPath, backupPath, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(primaryPath, backupPath, overwrite: true);
                    File.Move(temporaryPath, primaryPath, overwrite: true);
                }
            }
            else
            {
                File.Move(temporaryPath, primaryPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async ValueTask<Result<SaveLoadResult<TState>>> LoadAsync(
        string slot,
        CancellationToken cancellationToken = default)
    {
        ValidateSlot(slot);
        var primary = await TryLoadFileAsync(GetPrimaryPath(slot), SaveSource.Primary, cancellationToken);
        if (primary.IsSuccess)
        {
            return primary;
        }

        var backup = await TryLoadFileAsync(GetBackupPath(slot), SaveSource.Backup, cancellationToken);
        if (backup.IsSuccess)
        {
            return backup;
        }

        var message = $"Primary failed: {primary.Error?.Message}; backup failed: {backup.Error?.Message}";
        return Result<SaveLoadResult<TState>>.Failure("save.load_failed", message);
    }

    public bool Exists(string slot)
    {
        ValidateSlot(slot);
        return File.Exists(GetPrimaryPath(slot)) || File.Exists(GetBackupPath(slot));
    }

    /// <summary>列出所有有效存档槽位及其最近可读元数据，不反序列化游戏状态。</summary>
    public IReadOnlyList<SaveSlotMetadata> ListSlots()
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(file => file is not null && !file.EndsWith(".bak.json", StringComparison.OrdinalIgnoreCase))
            .Select(file => Path.GetFileNameWithoutExtension(file!))
            .Where(slot => SlotRegex().IsMatch(slot))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(slot => slot, StringComparer.Ordinal)
            .Select(ReadMetadata)
            .Where(metadata => metadata is not null)
            .Cast<SaveSlotMetadata>()
            .ToArray();
    }

    /// <summary>删除指定槽位的主存档与备份；返回是否实际删除了文件。</summary>
    public async ValueTask<bool> DeleteAsync(
        string slot,
        CancellationToken cancellationToken = default)
    {
        ValidateSlot(slot);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var deleted = false;
            foreach (var path in new[] { GetPrimaryPath(slot), GetBackupPath(slot) })
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                File.Delete(path);
                deleted = true;
            }
            return deleted;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async ValueTask<Result<SaveLoadResult<TState>>> TryLoadFileAsync(
        string path,
        SaveSource source,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return Result<SaveLoadResult<TState>>.Failure("save.not_found", $"'{path}' does not exist.");
        }

        try
        {
            var serialized = await File.ReadAllTextAsync(path, cancellationToken);
            var envelope = JsonSerializer.Deserialize<SaveEnvelope>(serialized, _jsonOptions) ??
                           throw new InvalidDataException("Save envelope is empty.");
            if (!string.Equals(ComputeChecksum(envelope.PayloadJson), envelope.Checksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Save checksum mismatch.");
            }

            if (envelope.SchemaVersion > _currentVersion)
            {
                throw new InvalidDataException(
                    $"Save version {envelope.SchemaVersion} is newer than supported version {_currentVersion}.");
            }

            var payload = JsonNode.Parse(envelope.PayloadJson) ??
                          throw new InvalidDataException("Save payload is empty.");
            var loadedVersion = envelope.SchemaVersion;
            while (loadedVersion < _currentVersion)
            {
                if (!_migrations.TryGetValue(loadedVersion, out var migration))
                {
                    throw new InvalidDataException(
                        $"Missing save migration {loadedVersion}->{loadedVersion + 1}.");
                }

                payload = migration.Migrate(payload) ??
                          throw new InvalidDataException($"Migration {loadedVersion} returned null.");
                loadedVersion = migration.ToVersion;
            }

            var state = payload.Deserialize<TState>(_jsonOptions) ??
                        throw new InvalidDataException("Save payload could not be deserialized.");
            return Result<SaveLoadResult<TState>>.Success(new SaveLoadResult<TState>(
                state,
                source,
                envelope.SchemaVersion,
                loadedVersion,
                envelope.SavedAtUtc));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result<SaveLoadResult<TState>>.Failure("save.invalid", exception.Message, exception);
        }
    }

    private string GetPrimaryPath(string slot) => Path.Combine(_directory, slot + ".json");

    private string GetBackupPath(string slot) => Path.Combine(_directory, slot + ".bak.json");

    private SaveSlotMetadata? ReadMetadata(string slot)
    {
        foreach (var (path, source) in new[]
                 {
                     (GetPrimaryPath(slot), SaveSource.Primary),
                     (GetBackupPath(slot), SaveSource.Backup),
                 })
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var serialized = File.ReadAllText(path);
                var envelope = JsonSerializer.Deserialize<SaveEnvelope>(serialized, _jsonOptions);
                if (envelope is null ||
                    !string.Equals(
                        ComputeChecksum(envelope.PayloadJson),
                        envelope.Checksum,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return new SaveSlotMetadata(
                    slot,
                    source,
                    envelope.SchemaVersion,
                    envelope.SavedAtUtc,
                    new FileInfo(path).Length);
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    private static string ComputeChecksum(string payloadJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));

    private static void ValidateSlot(string slot)
    {
        if (string.IsNullOrWhiteSpace(slot) || !SlotRegex().IsMatch(slot))
        {
            throw new ArgumentException(
                "Save slots may contain only lowercase letters, digits, underscore, and dash.",
                nameof(slot));
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlotRegex();
}

/// <summary>一次成功读档实际采用的物理文件来源。</summary>
public enum SaveSource
{
    /// <summary>从主存档文件读取。</summary>
    Primary,

    /// <summary>主存档不可用时从备份文件读取。</summary>
    Backup,
}

/// <summary>无需载入完整游戏状态即可展示的存档槽位信息。</summary>
public sealed record SaveSlotMetadata(
    string Slot,
    SaveSource Source,
    int SchemaVersion,
    DateTimeOffset SavedAtUtc,
    long FileSizeBytes);

public sealed record SaveLoadResult<TState>(
    TState State,
    SaveSource Source,
    int StoredVersion,
    int LoadedVersion,
    DateTimeOffset SavedAtUtc);
