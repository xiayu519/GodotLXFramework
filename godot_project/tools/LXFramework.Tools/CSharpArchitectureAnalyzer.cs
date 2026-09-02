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

internal sealed record ProductSourceDocument(
    string Path,
    string Source,
    bool IsChanged);

internal sealed record ProductSourceDiagnostic(
    string Code,
    string Path,
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

internal static class ProductSourceStructureAnalyzer
{
    private static readonly ProductSourceThresholds DefaultThresholds = new(
        FileLines: 1600,
        TypeLines: 1000,
        ExecutableMemberLines: 200,
        CompositionRootLines: 400);

    public static IReadOnlyList<ProductSourceDiagnostic> Analyze(
        IEnumerable<ProductSourceDocument> documents,
        string? compositionRootType)
    {
        return Analyze(documents, compositionRootType, DefaultThresholds);
    }

    public static string? ValidateRules()
    {
        var thresholds = new ProductSourceThresholds(12, 8, 4, 6);
        var oversized = Analyze(
        [
            new ProductSourceDocument(
                "script/Fixture/GameRoot.cs",
                """
                namespace Fixture;
                public partial class GameRoot
                {
                    private int _a;
                    private int _b;
                    private int _c;
                    private int _d;
                    private int _e;
                    public void Run()
                    {
                        _a++;
                        _b++;
                        _c++;
                        _d++;
                    }
                }
                """,
                true),
        ],
        "Fixture.GameRoot",
        thresholds);
        var expected = new[] { "LX_ARCH_005", "LX_ARCH_006", "LX_ARCH_007", "LX_ARCH_008" };
        if (expected.Any(code => oversized.All(diagnostic => diagnostic.Code != code)))
        {
            return "Product source structure analyzer did not reject oversized files, types, members, and composition roots.";
        }

        var partial = Analyze(
        [
            new ProductSourceDocument(
                "script/Fixture/Actor.State.cs",
                """
                namespace Fixture;
                public partial class Actor
                {
                    int A;
                    int B;
                    int C;
                    int D;
                    int E;
                }
                """,
                false),
            new ProductSourceDocument(
                "script/Fixture/Actor.Runtime.cs",
                """
                namespace Fixture;
                public partial class Actor
                {
                    int F;
                    int G;
                    int H;
                    int I;
                    int J;
                }
                """,
                true),
        ],
        null,
        thresholds with { FileLines = 40, ExecutableMemberLines = 20, CompositionRootLines = 20 });
        var partialDiagnostic = partial.SingleOrDefault(diagnostic => diagnostic.Code == "LX_ARCH_006");
        if (partialDiagnostic is null ||
            !string.Equals(partialDiagnostic.Path, "script/Fixture/Actor.Runtime.cs", StringComparison.Ordinal))
        {
            return "Product source structure analyzer did not aggregate partial type ownership around the changed declaration.";
        }

        var unchanged = Analyze(
        [
            new ProductSourceDocument(
                "script/Fixture/Legacy.cs",
                oversized[0].Message + "\n" + string.Join('\n', Enumerable.Repeat("class Legacy { int Value; }", 20)),
                false),
        ],
        null,
        new ProductSourceThresholds(2, 2, 2, 2));
        return unchanged.Count == 0
            ? null
            : "Product source structure analyzer reported an unchanged legacy file during affected validation.";
    }

    private static IReadOnlyList<ProductSourceDiagnostic> Analyze(
        IEnumerable<ProductSourceDocument> documents,
        string? compositionRootType,
        ProductSourceThresholds thresholds)
    {
        var parsed = documents
            .Select(document => new ParsedProductSource(
                document,
                CSharpSyntaxTree.ParseText(
                    document.Source,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12))
                    .GetCompilationUnitRoot()))
            .OrderBy(item => item.Document.Path, StringComparer.Ordinal)
            .ToArray();
        var diagnostics = new List<ProductSourceDiagnostic>();

        foreach (var item in parsed.Where(item => item.Document.IsChanged))
        {
            var fileLines = CountSignificantLines(item.Root);
            if (fileLines > thresholds.FileLines)
            {
                Add(
                    diagnostics,
                    "LX_ARCH_005",
                    item.Document,
                    item.Root,
                    $"Handwritten product file has {fileLines} significant lines (limit {thresholds.FileLines}); " +
                    "split it by runtime ownership instead of adding unrelated behavior.");
            }

            foreach (var member in item.Root.DescendantNodes().Where(IsExecutableMember))
            {
                var memberLines = CountSignificantLines(member);
                if (memberLines > thresholds.ExecutableMemberLines)
                {
                    Add(
                        diagnostics,
                        "LX_ARCH_007",
                        item.Document,
                        member,
                        $"Executable member has {memberLines} significant lines " +
                        $"(limit {thresholds.ExecutableMemberLines}); extract distinct behavior into a typed module or handler.");
                }
            }
        }

        var declarations = parsed
            .SelectMany(item => item.Root.DescendantNodes()
                .OfType<BaseTypeDeclarationSyntax>()
                .Select(declaration => new ProductTypeDeclaration(
                    item.Document,
                    declaration,
                    GetQualifiedTypeName(declaration),
                    CountSignificantLines(declaration))))
            .ToArray();
        foreach (var group in declarations.GroupBy(item => item.QualifiedName, StringComparer.Ordinal))
        {
            var changedDeclaration = group
                .Where(item => item.Document.IsChanged)
                .OrderBy(item => item.Document.Path, StringComparer.Ordinal)
                .FirstOrDefault();
            if (changedDeclaration is null)
            {
                continue;
            }

            var typeLines = group.Sum(item => item.SignificantLines);
            if (typeLines > thresholds.TypeLines)
            {
                Add(
                    diagnostics,
                    "LX_ARCH_006",
                    changedDeclaration.Document,
                    changedDeclaration.Declaration,
                    $"Product type '{group.Key}' spans {typeLines} significant lines across its declarations " +
                    $"(limit {thresholds.TypeLines}); extract independently owned modules instead of hiding one responsibility in partial files.");
            }
            if (!string.IsNullOrWhiteSpace(compositionRootType) &&
                string.Equals(group.Key, compositionRootType, StringComparison.Ordinal) &&
                typeLines > thresholds.CompositionRootLines)
            {
                Add(
                    diagnostics,
                    "LX_ARCH_008",
                    changedDeclaration.Document,
                    changedDeclaration.Declaration,
                    $"Composition root '{group.Key}' spans {typeLines} significant lines " +
                    $"(limit {thresholds.CompositionRootLines}); keep it limited to dependency composition, startup, and top-level flow transitions.");
            }
        }

        return diagnostics
            .OrderBy(diagnostic => diagnostic.Path, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Line)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsExecutableMember(SyntaxNode node) =>
        node is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax;

    private static int CountSignificantLines(SyntaxNode node) =>
        node.DescendantTokens(descendIntoTrivia: false)
            .Select(token => token.GetLocation().GetLineSpan().StartLinePosition.Line)
            .Distinct()
            .Count();

    private static string GetQualifiedTypeName(BaseTypeDeclarationSyntax declaration)
    {
        var namespaceSegments = declaration.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(item => item.Name.ToString());
        var typeSegments = declaration.Ancestors()
            .OfType<BaseTypeDeclarationSyntax>()
            .Reverse()
            .Select(item => item.Identifier.ValueText)
            .Append(declaration.Identifier.ValueText);
        return string.Join('.', namespaceSegments.Concat(typeSegments));
    }

    private static void Add(
        ICollection<ProductSourceDiagnostic> diagnostics,
        string code,
        ProductSourceDocument document,
        SyntaxNode node,
        string message)
    {
        var position = node.GetLocation().GetLineSpan().StartLinePosition;
        diagnostics.Add(new ProductSourceDiagnostic(
            code,
            document.Path,
            position.Line + 1,
            position.Character + 1,
            message));
    }

    private sealed record ProductSourceThresholds(
        int FileLines,
        int TypeLines,
        int ExecutableMemberLines,
        int CompositionRootLines);

    private sealed record ParsedProductSource(
        ProductSourceDocument Document,
        CompilationUnitSyntax Root);

    private sealed record ProductTypeDeclaration(
        ProductSourceDocument Document,
        BaseTypeDeclarationSyntax Declaration,
        string QualifiedName,
        int SignificantLines);
}
