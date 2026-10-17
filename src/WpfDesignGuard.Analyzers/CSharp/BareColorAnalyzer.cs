using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Analyzers.CSharp;

/// <summary>
/// DG0005 C# surface: Colors.* references and Color.FromRgb/FromArgb calls in
/// code-behind must be brush tokens (production lesson: token compliance must scan
/// comments and code alike). Syntax-level judgement on the receiver name.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BareColorAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Descriptor = new(
        "DG0005",
        RuleCatalog.TryGet("DG0005")!.Title,
        RuleCatalog.TryGet("DG0005")!.MessageFormat,
        DiagnosticFactory.Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Bare color construction in C# code must reference a brush token.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Descriptor);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        ConfigGate.RegisterConditional(context, "DG0005", AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        ConfigGate.RegisterConditional(context, "DG0005", AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var access = (MemberAccessExpressionSyntax)context.Node;
        if (BareFontSizeAnalyzer.MemberName(access.Expression) != "Colors")
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptor,
            access.GetLocation(),
            access.ToString()));
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation ||
            invocation.Expression is not MemberAccessExpressionSyntax access ||
            BareFontSizeAnalyzer.MemberName(access.Expression) != "Color")
        {
            return;
        }

        var method = access.Name.Identifier.Text;
        if (method is not ("FromRgb" or "FromArgb" or "FromScRgb"))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptor,
            invocation.GetLocation(),
            invocation.ToString()));
    }
}
