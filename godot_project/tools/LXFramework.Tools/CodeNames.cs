using System.Text;
using System.Text.RegularExpressions;

namespace LXFramework.Tools;

internal static partial class CodeNames
{
    public static void RequireIdentifier(string value, string parameterName)
    {
        if (!IdentifierRegex().IsMatch(value))
        {
            throw new ArgumentException($"'{value}' is not a valid C# identifier.", parameterName);
        }
    }

    public static void RequireSnakeCase(string value, string parameterName)
    {
        if (!SnakeCaseRegex().IsMatch(value))
        {
            throw new ArgumentException($"'{value}' is not a lowercase snake_case ID.", parameterName);
        }
    }

    public static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsUpper(character) && index > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    public static string ToPascalCase(string snakeCase) => string.Concat(
        snakeCase.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SnakeCaseRegex();
}
