using Luban;

namespace LX.Core.Data;

/// <summary>
/// Adapts Luban's generated table constructor to an application-owned binary source.
/// </summary>
public static class LubanTableLoader
{
    public static TTables Load<TTables>(
        Func<string, byte[]> readBytes,
        Func<Func<string, ByteBuf>, TTables> createTables)
        where TTables : notnull
    {
        ArgumentNullException.ThrowIfNull(readBytes);
        ArgumentNullException.ThrowIfNull(createTables);
        var loadedBuffers = new Dictionary<string, ByteBuf>(StringComparer.Ordinal);

        ByteBuf LoadTable(string tableName)
        {
            ValidateTableName(tableName);
            var bytes = readBytes(tableName) ??
                throw new InvalidDataException($"Luban table '{tableName}' produced no bytes.");
            if (bytes.Length == 0)
            {
                throw new InvalidDataException(
                    $"Luban table '{tableName}' contains an empty binary payload.");
            }

            var buffer = ByteBuf.Wrap(bytes);
            loadedBuffers.Add(tableName, buffer);
            return buffer;
        }

        var tables = createTables(LoadTable) ??
            throw new InvalidDataException("Luban table factory produced no table set.");
        var unread = loadedBuffers.FirstOrDefault(entry => !entry.Value.Empty);
        if (!string.IsNullOrEmpty(unread.Key))
        {
            throw new InvalidDataException(
                $"Luban table '{unread.Key}' has {unread.Value.Remaining} unread binary byte(s).");
        }
        return tables;
    }

    private static void ValidateTableName(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName) ||
            tableName is "." or ".." ||
            tableName.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')))
        {
            throw new InvalidDataException(
                $"Luban emitted unsafe table name '{tableName}'.");
        }
    }
}
