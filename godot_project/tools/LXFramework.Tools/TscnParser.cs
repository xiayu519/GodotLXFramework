using System.Text.RegularExpressions;

namespace LXFramework.Tools;

internal static partial class TscnParser
{
    private static readonly Regex NodeHeader = NodeHeaderRegex();
    private static readonly Regex Attribute = AttributeRegex();

    public static TscnScene Parse(string path)
    {
        var nodes = new List<TscnNode>();
        TscnNode? current = null;
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            var match = NodeHeader.Match(line);
            if (match.Success)
            {
                var attributes = Attribute.Matches(match.Groups["attributes"].Value)
                    .ToDictionary(
                        attribute => attribute.Groups["key"].Value,
                        attribute => attribute.Groups["value"].Value,
                        StringComparer.Ordinal);
                if (!attributes.TryGetValue("name", out var name))
                {
                    throw new InvalidDataException($"Scene '{path}' contains a node without a name.");
                }

                var hasExplicitType = attributes.TryGetValue("type", out var type);
                current = new TscnNode(
                    name,
                    type ?? "Node",
                    attributes.GetValueOrDefault("parent"),
                    hasExplicitType);
                nodes.Add(current);
                continue;
            }

            if (current is not null && line.Equals("unique_name_in_owner = true", StringComparison.Ordinal))
            {
                current.UniqueNameInOwner = true;
            }
        }

        if (nodes.Count == 0)
        {
            throw new InvalidDataException($"Scene '{path}' contains no node declarations.");
        }

        return new TscnScene(path, nodes);
    }

    [GeneratedRegex(
        "^\\[node\\s+(?<attributes>.*)\\]$",
        RegexOptions.CultureInvariant)]
    private static partial Regex NodeHeaderRegex();

    [GeneratedRegex(
        "(?<key>[A-Za-z_][A-Za-z0-9_]*)=\\\"(?<value>(?:\\\\\\\\.|[^\\\"])*)\\\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex AttributeRegex();
}

internal sealed record TscnScene(string Path, IReadOnlyList<TscnNode> Nodes)
{
    public TscnNode Root => Nodes[0];
}

internal sealed record TscnNode(
    string Name,
    string Type,
    string? Parent,
    bool HasExplicitType)
{
    public bool UniqueNameInOwner { get; set; }
}
