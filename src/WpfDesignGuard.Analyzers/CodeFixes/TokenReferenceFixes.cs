using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace WpfDesignGuard.Analyzers.CodeFixes;

/// <summary>
/// Shared machinery for the v0.1 C# code fixes: without the Tokens
/// runtime package the fix produces a PLACEHOLDER token reference (nearest configured
/// ladder entry by name) plus a TODO comment — the mechanical rewrite is correct, the
/// token type becomes real when WpfDesignGuard.Tokens lands in v0.2.
/// </summary>
internal static class TokenFixHelper
{
    /// <summary>Default font-size key table (matches TokenTable.Default); configurable table arrives with v0.2.</summary>
    internal static readonly (string Key, double Size)[] DefaultFontSizes =
    {
        ("Caption", 14), ("Body", 16), ("Subtitle", 18), ("Title", 20), ("Display", 28),
    };

    internal static (string Key, double Size) Nearest(double size)
    {
        var best = DefaultFontSizes[0];
        var bestDelta = System.Math.Abs(best.Size - size);
        foreach (var candidate in DefaultFontSizes)
        {
            var delta = System.Math.Abs(candidate.Size - size);
            if (delta < bestDelta || (delta == bestDelta && candidate.Size < best.Size)) // tie → smaller
            {
                best = candidate;
                bestDelta = delta;
            }
        }

        return best;
    }

    // Parsed whole (comments ride as trivia) — no WithTriviaFrom, that would strip them.
    internal static ExpressionSyntax Placeholder(string tokenExpression, string original) =>
        SyntaxFactory.ParseExpression($"/* DesignGuard: placeholder until the Tokens package ships */ {tokenExpression} /* was {original} */");
}

/// <summary>Fixes DG0001: rewrites a bare FontSize literal to the nearest font-size token key.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BareFontSizeCodeFix))]
[Shared]
public sealed class BareFontSizeCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create("DG0001");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics.Where(d => d.Id == "DG0001"))
        {
            var action = CodeAction.Create(
                "Replace with nearest font-size token (placeholder)",
                cancellationToken => RewriteAsync(context.Document, diagnostic, cancellationToken),
                nameof(BareFontSizeCodeFix));
            context.RegisterCodeFix(action, diagnostic);
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> RewriteAsync(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root?.FindNode(diagnostic.Location.SourceSpan) is not LiteralExpressionSyntax literal)
        {
            return document;
        }

        if (!WpfDesignGuard.Core.Xaml.XamlValue.IsNumber(literal.ToString(), out var size))
        {
            return document;
        }

        var (key, _) = TokenFixHelper.Nearest(size);
        var replacement = TokenFixHelper.Placeholder($"FontSizeTokens.{key}", literal.ToString());
        var newRoot = root.ReplaceNode(literal, replacement);
        return document.WithSyntaxRoot(newRoot);
    }
}

/// <summary>Fixes DG0005: replaces bare Colors.* / Color.FromRgb with a placeholder brush-token reference.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BareColorCodeFix))]
[Shared]
public sealed class BareColorCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create("DG0005");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics.Where(d => d.Id == "DG0005"))
        {
            var action = CodeAction.Create(
                "Replace with brush token placeholder",
                cancellationToken => RewriteAsync(context.Document, diagnostic, cancellationToken),
                nameof(BareColorCodeFix));
            context.RegisterCodeFix(action, diagnostic);
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> RewriteAsync(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var node = root?.FindNode(diagnostic.Location.SourceSpan);
        if (node == null)
        {
            return document;
        }

        var replacement = TokenFixHelper.Placeholder("BrushTokens.Accent", node.ToString());
        var newRoot = root!.ReplaceNode(node, replacement);
        return document.WithSyntaxRoot(newRoot);
    }
}
