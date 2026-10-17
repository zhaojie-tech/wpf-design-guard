using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Analyzers.CSharp;

/// <summary>
/// DG0001 C# surface: numeric literals assigned to a member named FontSize in
/// code-behind/converters must be font tokens — token compliance covers code-behind
/// too, not only XAML. Syntax-level judgement on the member name; markup and
/// token indirections are not literals and never match.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BareFontSizeAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Descriptor = new(
        "DG0001",
        RuleCatalog.TryGet("DG0001")!.Title,
        RuleCatalog.TryGet("DG0001")!.MessageFormat,
        DiagnosticFactory.Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Bare FontSize literals in C# code must reference a design token.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Descriptor);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        ConfigGate.RegisterConditional(context, "DG0001", AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (assignment.Right is not LiteralExpressionSyntax literal ||
            !literal.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            return;
        }

        if (MemberName(assignment.Left) != "FontSize")
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptor,
            literal.GetLocation(),
            literal.ToString()));
    }

    internal static string? MemberName(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
        _ => null,
    };
}
