using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LXFramework.Tools;

internal enum ArchitectureLayer
{
    Core,
    Adapter,
    Product,
}

internal sealed record ArchitectureDiagnostic(
    string Code,
    int Line,
    int Column,
    string Message);

internal static class CSharpArchitectureAnalyzer
{
    public static IReadOnlyList<ArchitectureDiagnostic> Analyze(
        string source,
        ArchitectureLayer layer,
        string? productNamespace = null)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12));
        var root = tree.GetCompilationUnitRoot();
        var diagnostics = new List<ArchitectureDiagnostic>();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        if (layer == ArchitectureLayer.Core)
        {
            AnalyzeForbiddenNamespace(root, "Godot", "LX_ARCH_001",
                "Pure core must not reference the Godot namespace.", diagnostics, keys);
        }

        if (layer == ArchitectureLayer.Adapter && !string.IsNullOrWhiteSpace(productNamespace))
        {
            AnalyzeForbiddenNamespace(root, productNamespace, "LX_ARCH_002",
                "Framework adapter must not depend on the product namespace.", diagnostics, keys);
        }

        if (layer == ArchitectureLayer.Product)
        {
            AnalyzeDynamicLoads(root, diagnostics, keys);
        }

        AnalyzeStaticServiceState(root, diagnostics, keys);
        return diagnostics;
    }

    private static void AnalyzeForbiddenNamespace(
        CompilationUnitSyntax root,
        string forbiddenNamespace,
        string code,
        string message,
        ICollection<ArchitectureDiagnostic> diagnostics,
        ISet<string> keys)
    {
        foreach (var usingDirective in root.Usings)
        {
            if (usingDirective.Name is not null && IsNamespaceReference(usingDirective.Name.ToString(), forbiddenNamespace))
            {
                Add(code, usingDirective.Name, message, diagnostics, keys);
            }
        }

        foreach (var name in root.DescendantNodes().OfType<NameSyntax>())
        {
            if (name.Parent is UsingDirectiveSyntax || !IsNamespaceReference(name.ToString(), forbiddenNamespace))
            {
                continue;
            }
            Add(code, name, message, diagnostics, keys);
        }

        foreach (var access in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (IsNamespaceReference(access.Expression.ToString(), forbiddenNamespace))
            {
                Add(code, access.Expression, message, diagnostics, keys);
            }
        }
    }

    private static void AnalyzeDynamicLoads(
        CompilationUnitSyntax root,
        ICollection<ArchitectureDiagnostic> diagnostics,
        ISet<string> keys)
    {
        var aliases = root.Usings
            .Where(item => item.Alias is not null && item.Name is not null)
            .Where(item => IsGodotLoaderType(item.Name!.ToString()))
            .ToDictionary(item => item.Alias!.Name.Identifier.ValueText, item => item.Name!.ToString(), StringComparer.Ordinal);
        var hasStaticGd = root.Usings.Any(item =>
            !item.StaticKeyword.IsKind(SyntaxKind.None) &&
            item.Name is not null &&
            IsGodotLoaderType(item.Name.ToString()));

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is MemberAccessExpressionSyntax member &&
                IsForbiddenLoad(member.Expression.ToString(), member.Name.Identifier.ValueText, aliases))
            {
                Add("LX_ARCH_003", member,
                    "Product code must acquire dynamic resources through LX.Res.", diagnostics, keys);
            }
            else if (hasStaticGd && invocation.Expression is IdentifierNameSyntax identifier &&
                     IsLoadMethod(identifier.Identifier.ValueText))
            {
                Add("LX_ARCH_003", identifier,
                    "Product code must acquire dynamic resources through LX.Res.", diagnostics, keys);
            }
        }
    }

    private static void AnalyzeStaticServiceState(
        CompilationUnitSyntax root,
        ICollection<ArchitectureDiagnostic> diagnostics,
        ISet<string> keys)
    {
        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            if (field.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                !field.Modifiers.Any(SyntaxKind.ConstKeyword) &&
                ContainsServiceType(field.Declaration.Type))
            {
                Add("LX_ARCH_004", field.Declaration.Type,
                    "Service, context, scope, registry, hub, or pool instances must not be stored in static state.",
                    diagnostics, keys);
            }
        }

        foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            if (property.Modifiers.Any(SyntaxKind.StaticKeyword) && ContainsServiceType(property.Type))
            {
                Add("LX_ARCH_004", property.Type,
                    "Service, context, scope, registry, hub, or pool instances must not be exposed as static state.",
                    diagnostics, keys);
            }
        }
    }

    private static bool IsForbiddenLoad(
        string receiver,
        string method,
        IReadOnlyDictionary<string, string> aliases)
    {
        if (!IsLoadMethod(method))
        {
            return false;
        }

        var normalized = NormalizeName(receiver);
        if (aliases.TryGetValue(normalized, out var target))
        {
            normalized = NormalizeName(target);
        }
        return normalized is "GD" or "Godot.GD" or "ResourceLoader" or "Godot.ResourceLoader";
    }

    private static bool IsLoadMethod(string method) =>
        method == "Load" || method.StartsWith("LoadThreaded", StringComparison.Ordinal);

    private static bool IsGodotLoaderType(string name) =>
        NormalizeName(name) is "GD" or "Godot.GD" or "ResourceLoader" or "Godot.ResourceLoader";

    private static bool ContainsServiceType(TypeSyntax type) =>
        type.DescendantTokens()
            .Where(token => token.IsKind(SyntaxKind.IdentifierToken))
            .Select(token => token.ValueText)
            .Any(name => name is "LXContext" or "LifetimeScope" or "EventHub" or "AssetRegistry" ||
                         name.EndsWith("Service", StringComparison.Ordinal) ||
                         name.EndsWith("Registry", StringComparison.Ordinal) ||
                         name.EndsWith("Context", StringComparison.Ordinal) ||
                         name.EndsWith("Scope", StringComparison.Ordinal) ||
                         name.EndsWith("Hub", StringComparison.Ordinal) ||
                         name.EndsWith("Pool", StringComparison.Ordinal));

    private static bool IsNamespaceReference(string value, string expectedNamespace)
    {
        var normalized = NormalizeName(value);
        return normalized.Equals(expectedNamespace, StringComparison.Ordinal) ||
               normalized.StartsWith(expectedNamespace + ".", StringComparison.Ordinal);
    }

    private static string NormalizeName(string value) =>
        value.Replace("global::", string.Empty, StringComparison.Ordinal).Trim();

    private static void Add(
        string code,
        SyntaxNode node,
        string message,
        ICollection<ArchitectureDiagnostic> diagnostics,
        ISet<string> keys)
    {
        var position = node.GetLocation().GetLineSpan().StartLinePosition;
        var key = $"{code}:{position.Line}:{position.Character}";
        if (keys.Add(key))
        {
            diagnostics.Add(new ArchitectureDiagnostic(code, position.Line + 1, position.Character + 1, message));
        }
    }
}
