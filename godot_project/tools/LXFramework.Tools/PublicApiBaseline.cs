using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LXFramework.Tools;

internal static class PublicApiBaseline
{
    private const string Header = "# LXFramework public API baseline v1";

    public static int Run(string root, IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 1 || arguments[0] is not ("check" or "update"))
        {
            Console.Error.WriteLine("api usage: lx api check|update");
            return 2;
        }

        if (arguments[0] == "update")
        {
            var path = BaselinePath(root);
            ToolFiles.WriteText(path, Build(root));
            Console.WriteLine($"public API baseline updated -> {ToolFiles.Relative(root, path)}");
            return 0;
        }

        if (Validate(root) is { } error)
        {
            Console.Error.WriteLine($"api: {error}");
            return 1;
        }
        Console.WriteLine("public API baseline passed");
        return 0;
    }

    public static string? Validate(string root)
    {
        var path = BaselinePath(root);
        if (!File.Exists(path))
        {
            return "api/LXFramework.PublicApi.txt is missing; review the current API and run 'lx api update'.";
        }

        var expected = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        var actual = Build(root);
        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            return null;
        }

        var expectedLines = expected.Split('\n').ToHashSet(StringComparer.Ordinal);
        var actualLines = actual.Split('\n').ToHashSet(StringComparer.Ordinal);
        var removed = expectedLines.Except(actualLines).Where(line => line.Length != 0).Take(5).ToArray();
        var added = actualLines.Except(expectedLines).Where(line => line.Length != 0).Take(5).ToArray();
        return "public API differs from the reviewed baseline. " +
               $"Removed/changed=[{string.Join(" | ", removed)}]; " +
               $"added/changed=[{string.Join(" | ", added)}]. " +
               "Preserve compatibility or explicitly review and run 'lx api update'.";
    }

    private static string Build(string root)
    {
        var declarations = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var sourceRoot in new[]
                 {
                     Path.Combine(root, "src", "LXFramework.Core"),
                     Path.Combine(root, "src", "LXFramework"),
                 })
        {
            foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                         .Where(path => !path.Split(Path.DirectorySeparatorChar)
                             .Any(segment => segment is "bin" or "obj" or "Generated")))
            {
                var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(File.ReadAllText(path));
                Collect(tree.GetRoot(), declarations);
            }
        }

        return Header + "\n" + string.Join('\n', declarations) + "\n";
    }

    private static void Collect(SyntaxNode root, ISet<string> declarations)
    {
        foreach (var type in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
                     .Where(IsPubliclyReachable))
        {
            var typeName = QualifiedTypeName(type);
            declarations.Add($"type {typeName} {TypeHeader(type)}");
            if (type is EnumDeclarationSyntax enumDeclaration)
            {
                foreach (var member in enumDeclaration.Members)
                {
                    declarations.Add(
                        $"enum {typeName}.{member.Identifier.ValueText}" +
                        (member.EqualsValue is null ? string.Empty : $" = {Compact(member.EqualsValue.Value)}"));
                }
            }

            var members = type is TypeDeclarationSyntax typeDeclaration
                ? typeDeclaration.Members
                : default;
            foreach (var member in members.Where(IsApiMember))
            {
                foreach (var signature in MemberSignatures(typeName, member))
                {
                    declarations.Add(signature);
                }
            }
        }

        foreach (var declaration in root.DescendantNodes().OfType<DelegateDeclarationSyntax>()
                     .Where(IsPubliclyReachable))
        {
            declarations.Add(
                $"delegate {QualifiedTypeName(declaration)} {Modifiers(declaration.Modifiers)} " +
                $"{Compact(declaration.ReturnType)} {declaration.Identifier.ValueText}" +
                $"{Compact(declaration.TypeParameterList)}{Compact(declaration.ParameterList)}" +
                $" {string.Join(' ', declaration.ConstraintClauses.Select(Compact))}".TrimEnd());
        }
    }

    private static IEnumerable<string> MemberSignatures(string typeName, MemberDeclarationSyntax member)
    {
        var prefix = $"member {typeName} ";
        switch (member)
        {
            case FieldDeclarationSyntax field:
                foreach (var variable in field.Declaration.Variables)
                {
                    yield return prefix +
                                 $"{Modifiers(field.Modifiers)} {Compact(field.Declaration.Type)} " +
                                 variable.Identifier.ValueText +
                                 (field.Modifiers.Any(token => token.ValueText == "const") &&
                                  variable.Initializer is not null
                                     ? $" = {Compact(variable.Initializer.Value)}"
                                     : string.Empty);
                }
                yield break;
            case EventFieldDeclarationSyntax eventField:
                foreach (var variable in eventField.Declaration.Variables)
                {
                    yield return prefix +
                                 $"{Modifiers(eventField.Modifiers)} event " +
                                 $"{Compact(eventField.Declaration.Type)} {variable.Identifier.ValueText}";
                }
                yield break;
            case EventDeclarationSyntax eventDeclaration:
                yield return prefix +
                             $"{Modifiers(eventDeclaration.Modifiers)} event " +
                             $"{Compact(eventDeclaration.Type)} {eventDeclaration.Identifier.ValueText} " +
                             Accessors(eventDeclaration.AccessorList);
                yield break;
            case PropertyDeclarationSyntax property:
                yield return prefix +
                             $"{Modifiers(property.Modifiers)} {Compact(property.Type)} " +
                             $"{Compact(property.ExplicitInterfaceSpecifier)}{property.Identifier.ValueText} " +
                             (property.AccessorList is null ? "{ get; }" : Accessors(property.AccessorList));
                yield break;
            case IndexerDeclarationSyntax indexer:
                yield return prefix +
                             $"{Modifiers(indexer.Modifiers)} {Compact(indexer.Type)} " +
                             $"{Compact(indexer.ExplicitInterfaceSpecifier)}this{Compact(indexer.ParameterList)} " +
                             Accessors(indexer.AccessorList);
                yield break;
            case ConstructorDeclarationSyntax constructor:
                yield return prefix +
                             $"{Modifiers(constructor.Modifiers)} {constructor.Identifier.ValueText}" +
                             Compact(constructor.ParameterList);
                yield break;
            case MethodDeclarationSyntax method:
                yield return prefix +
                             $"{Modifiers(method.Modifiers)} {Compact(method.ReturnType)} " +
                             $"{Compact(method.ExplicitInterfaceSpecifier)}{method.Identifier.ValueText}" +
                             $"{Compact(method.TypeParameterList)}{Compact(method.ParameterList)}" +
                             $" {string.Join(' ', method.ConstraintClauses.Select(Compact))}".TrimEnd();
                yield break;
            case OperatorDeclarationSyntax operatorDeclaration:
                yield return prefix +
                             $"{Modifiers(operatorDeclaration.Modifiers)} {Compact(operatorDeclaration.ReturnType)} " +
                             $"operator {operatorDeclaration.OperatorToken.ValueText}" +
                             Compact(operatorDeclaration.ParameterList);
                yield break;
            case ConversionOperatorDeclarationSyntax conversion:
                yield return prefix +
                             $"{Modifiers(conversion.Modifiers)} {conversion.ImplicitOrExplicitKeyword.ValueText} " +
                             $"operator {Compact(conversion.Type)}{Compact(conversion.ParameterList)}";
                yield break;
        }
    }

    private static string TypeHeader(BaseTypeDeclarationSyntax declaration)
    {
        var kind = declaration switch
        {
            RecordDeclarationSyntax record => record.ClassOrStructKeyword.IsKind(
                Microsoft.CodeAnalysis.CSharp.SyntaxKind.StructKeyword) ? "record struct" : "record",
            ClassDeclarationSyntax => "class",
            StructDeclarationSyntax => "struct",
            InterfaceDeclarationSyntax => "interface",
            EnumDeclarationSyntax => "enum",
            _ => declaration.Kind().ToString(),
        };
        var primaryParameters = declaration is TypeDeclarationSyntax type
            ? Compact(type.ParameterList)
            : string.Empty;
        var typeParameters = declaration is TypeDeclarationSyntax typed
            ? Compact(typed.TypeParameterList)
            : string.Empty;
        var constraints = declaration is TypeDeclarationSyntax constrained
            ? string.Join(' ', constrained.ConstraintClauses.Select(Compact))
            : string.Empty;
        return ($"{Modifiers(declaration.Modifiers)} {kind} {declaration.Identifier.ValueText}" +
                $"{typeParameters}{primaryParameters} {Compact(declaration.BaseList)} {constraints}")
            .Trim();
    }

    private static bool IsApiMember(MemberDeclarationSyntax member) =>
        member is not BaseTypeDeclarationSyntax &&
        member is not DelegateDeclarationSyntax &&
        HasApiAccessibility(member.GetModifiers());

    private static bool IsPubliclyReachable(MemberDeclarationSyntax declaration)
    {
        if (!HasApiAccessibility(declaration.GetModifiers()))
        {
            return false;
        }

        return declaration.Ancestors().OfType<BaseTypeDeclarationSyntax>()
            .All(type => HasApiAccessibility(type.Modifiers));
    }

    private static bool HasApiAccessibility(SyntaxTokenList modifiers) =>
        modifiers.Any(token => token.ValueText is "public" or "protected");

    private static string QualifiedTypeName(MemberDeclarationSyntax declaration)
    {
        var namespaceName = declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault()?.Name.ToString();
        var containers = declaration.Ancestors().OfType<BaseTypeDeclarationSyntax>()
            .Reverse()
            .Select(type => type.Identifier.ValueText);
        return string.Join('.', new[] { namespaceName }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Concat(containers)
            .Append(declaration switch
            {
                BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
                DelegateDeclarationSyntax delegated => delegated.Identifier.ValueText,
                _ => throw new ArgumentOutOfRangeException(nameof(declaration)),
            }));
    }

    private static string Accessors(AccessorListSyntax? accessors) =>
        accessors is null
            ? string.Empty
            : "{ " + string.Join(' ', accessors.Accessors.Select(accessor =>
                $"{Modifiers(accessor.Modifiers)} {accessor.Keyword.ValueText};".TrimStart())) + " }";

    private static string Modifiers(SyntaxTokenList modifiers) => string.Join(' ', modifiers
        .Where(token => token.ValueText is not ("async" or "partial"))
        .Select(token => token.ValueText));

    private static string Compact(SyntaxNode? node) => node is null
        ? string.Empty
        : string.Join(' ', node.NormalizeWhitespace().ToFullString()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string BaselinePath(string root) =>
        Path.Combine(root, "api", "LXFramework.PublicApi.txt");
}

internal static class MemberDeclarationSyntaxExtensions
{
    public static SyntaxTokenList GetModifiers(this MemberDeclarationSyntax declaration) => declaration switch
    {
        BaseTypeDeclarationSyntax type => type.Modifiers,
        DelegateDeclarationSyntax delegated => delegated.Modifiers,
        FieldDeclarationSyntax field => field.Modifiers,
        EventFieldDeclarationSyntax eventField => eventField.Modifiers,
        EventDeclarationSyntax eventDeclaration => eventDeclaration.Modifiers,
        PropertyDeclarationSyntax property => property.Modifiers,
        IndexerDeclarationSyntax indexer => indexer.Modifiers,
        ConstructorDeclarationSyntax constructor => constructor.Modifiers,
        MethodDeclarationSyntax method => method.Modifiers,
        OperatorDeclarationSyntax operation => operation.Modifiers,
        ConversionOperatorDeclarationSyntax conversion => conversion.Modifiers,
        _ => default,
    };
}
