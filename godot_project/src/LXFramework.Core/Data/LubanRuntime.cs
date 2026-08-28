using System.Collections;
using System.Globalization;
using System.Text;

namespace Luban;

/// <summary>
/// Minimal compatibility contract required by Luban's C# binary generator.
/// </summary>
public interface ITypeId
{
    int GetTypeId();
}

/// <summary>
/// Base type emitted by Luban for generated reference types.
/// </summary>
public abstract class BeanBase : ITypeId
{
    public abstract int GetTypeId();
}

/// <summary>
/// Represents invalid generated-table payloads.
/// </summary>
public sealed class SerializationException : Exception
{
    public SerializationException()
    {
    }

    public SerializationException(string? message)
        : base(message)
    {
    }

    public SerializationException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Deterministic formatting helpers referenced by Luban-generated diagnostics.
/// </summary>
public static class StringUtil
{
    public static string ToStr(object? value)
    {
        var builder = new StringBuilder();
        Append(builder, value);
        return builder.ToString();
    }

    public static string CollectionToString<T>(IEnumerable<T>? values) => ToStr(values);

    public static string CollectionToString<TKey, TValue>(IDictionary<TKey, TValue>? values)
        where TKey : notnull => ToStr(values);

    private static void Append(StringBuilder builder, object? value)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                return;
            case string text:
                builder.Append('"').Append(text).Append('"');
                return;
            case IDictionary dictionary:
                AppendDictionary(builder, dictionary);
                return;
            case IEnumerable sequence:
                AppendSequence(builder, sequence);
                return;
            case IFormattable formattable:
                builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                return;
            default:
                builder.Append(value);
                return;
        }
    }

    private static void AppendDictionary(StringBuilder builder, IDictionary dictionary)
    {
        builder.Append('{');
        var first = true;
        foreach (DictionaryEntry entry in dictionary)
        {
            if (!first)
            {
                builder.Append(", ");
            }
            first = false;
            Append(builder, entry.Key);
            builder.Append(": ");
            Append(builder, entry.Value);
        }
        builder.Append('}');
    }

    private static void AppendSequence(StringBuilder builder, IEnumerable sequence)
    {
        builder.Append('[');
        var first = true;
        foreach (var item in sequence)
        {
            if (!first)
            {
                builder.Append(", ");
            }
            first = false;
            Append(builder, item);
        }
        builder.Append(']');
    }
}
