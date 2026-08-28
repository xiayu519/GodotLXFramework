using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LXFramework.Tools;

internal sealed record PublicApiDocumentationDiagnostic(
    int Line,
    int Column,
    string Message);

/// <summary>检查人工最常查阅、也最容易因语义不明而误用的枚举和常量文档。</summary>
internal static class PublicApiDocumentationAnalyzer
{
    public static IReadOnlyList<PublicApiDocumentationDiagnostic> Analyze(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12));
        var root = tree.GetCompilationUnitRoot();
        var diagnostics = new List<PublicApiDocumentationDiagnostic>();

        foreach (var declaration in root.DescendantNodes().OfType<EnumDeclarationSyntax>()
                     .Where(item => item.Modifiers.Any(SyntaxKind.PublicKeyword)))
        {
            RequireDocumentation(declaration, $"Public enum '{declaration.Identifier.ValueText}'", diagnostics);
            foreach (var member in declaration.Members)
            {
                RequireDocumentation(
                    member,
                    $"Public enum member '{declaration.Identifier.ValueText}.{member.Identifier.ValueText}'",
                    diagnostics);
            }
        }

        foreach (var declaration in root.DescendantNodes().OfType<FieldDeclarationSyntax>()
                     .Where(IsPublicConstantLike))
        {
            foreach (var variable in declaration.Declaration.Variables)
            {
                RequireDocumentation(
                    declaration,
                    $"Public constant '{variable.Identifier.ValueText}'",
                    diagnostics);
            }
        }

        return diagnostics;
    }

    private static bool IsPublicConstantLike(FieldDeclarationSyntax declaration)
    {
        if (!declaration.Modifiers.Any(SyntaxKind.PublicKeyword))
        {
            return false;
        }

        return declaration.Modifiers.Any(SyntaxKind.ConstKeyword) ||
               declaration.Modifiers.Any(SyntaxKind.StaticKeyword) &&
               declaration.Modifiers.Any(SyntaxKind.ReadOnlyKeyword);
    }

    private static void RequireDocumentation(
        SyntaxNode node,
        string label,
        ICollection<PublicApiDocumentationDiagnostic> diagnostics)
    {
        if (node.GetLeadingTrivia().Any(trivia => trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                                                  trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)))
        {
            return;
        }

        var position = node.GetLocation().GetLineSpan().StartLinePosition;
        diagnostics.Add(new PublicApiDocumentationDiagnostic(
            position.Line + 1,
            position.Character + 1,
            $"{label} must have a detailed XML documentation comment."));
    }
}
