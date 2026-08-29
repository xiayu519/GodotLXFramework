using System.Text.Json;
using System.Text.Json.Serialization;
using LX.Core.Data;
using LX.Res;
using Luban;
using Godot;

namespace LX.Content;

public sealed class ContentService
{
    private readonly JsonSerializerOptions _jsonOptions;

    public ContentService(JsonSerializerOptions? jsonOptions = null)
    {
        _jsonOptions = jsonOptions is null
            ? new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }
            : new JsonSerializerOptions(jsonOptions);
        if (!_jsonOptions.Converters.OfType<JsonStringEnumConverter>().Any())
        {
            _jsonOptions.Converters.Add(new JsonStringEnumConverter());
        }
    }

    public T LoadJson<T>(string resourcePath) where T : notnull
    {
        ValidateJsonPath(resourcePath);
        try
        {
            return JsonSerializer.Deserialize<T>(ReadContentText(resourcePath), _jsonOptions) ??
                   throw new InvalidDataException($"Content '{resourcePath}' produced no value.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Content '{resourcePath}' contains invalid JSON at line {exception.LineNumber}, byte {exception.BytePositionInLine}.",
                exception);
        }
    }

    public T Load<T>(ContentRef<T> content) where T : notnull => LoadJson<T>(content.Path);

    public TTables LoadLubanTables<TTables>(
        Func<Func<string, ByteBuf>, TTables> createTables,
        string resourceDirectory = "res://content/data/luban")
        where TTables : notnull
    {
        ValidateContentDirectory(resourceDirectory);
        var normalizedDirectory = resourceDirectory.TrimEnd('/');
        return LubanTableLoader.Load(
            tableName => ReadContentBytes($"{normalizedDirectory}/{tableName}.bytes"),
            createTables);
    }

    public DataCatalog<TId, TValue> LoadCatalog<TId, TValue>(
        string resourcePath,
        IEqualityComparer<TId>? comparer = null)
        where TId : notnull
        where TValue : IDataRecord<TId> =>
        new(LoadJson<TValue[]>(resourcePath), comparer);

    private static void ValidateJsonPath(string resourcePath)
    {
        if (!GodotResourcePath.IsCanonical(resourcePath, ".json") ||
            !resourcePath.StartsWith("res://content/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Runtime JSON content must use a res://content/*.json path.",
                nameof(resourcePath));
        }
    }

    private static void ValidateContentDirectory(string resourceDirectory)
    {
        if (string.IsNullOrWhiteSpace(resourceDirectory))
        {
            throw new ArgumentException(
                "Luban content directory must be a res://content/* path without traversal segments.",
                nameof(resourceDirectory));
        }

        var normalizedDirectory = resourceDirectory.TrimEnd('/');
        if (!GodotResourcePath.IsCanonical(normalizedDirectory) ||
            !normalizedDirectory.StartsWith("res://content/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Luban content directory must be a res://content/* path without traversal segments.",
                nameof(resourceDirectory));
        }
    }

    private static string ReadContentText(string resourcePath)
    {
        using var file = Godot.FileAccess.Open(resourcePath, Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            throw new IOException(
                $"Could not open content '{resourcePath}': {Godot.FileAccess.GetOpenError()}.");
        }

        return file.GetAsText();
    }

    private static byte[] ReadContentBytes(string resourcePath)
    {
        using var file = Godot.FileAccess.Open(resourcePath, Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            throw new IOException(
                $"Could not open content '{resourcePath}': {Godot.FileAccess.GetOpenError()}.");
        }

        var length = file.GetLength();
        if (length <= 0 || length > int.MaxValue)
        {
            throw new InvalidDataException(
                $"Binary content '{resourcePath}' has invalid length '{length}'.");
        }
        return file.GetBuffer((long)length);
    }
}
