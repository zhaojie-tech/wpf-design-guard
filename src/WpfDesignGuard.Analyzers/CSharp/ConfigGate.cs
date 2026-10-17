using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using WpfDesignGuard.Core;
using WpfDesignGuard.Core.Configuration;

namespace WpfDesignGuard.Analyzers.CSharp;

/// <summary>
/// Config gate for the C# analyzer surfaces (ADR-0005): the designguard.json injected
/// by the package targets is visible to analyzers as an additional file; token-dependent
/// rules honor the config's enabled flag so the init template's `"enabled": false`
/// covers the C# surface too (same rule ID, same semantics on both surfaces).
/// No config → rules behave as catalogued (enabled), matching the engine default.
/// </summary>
internal static class ConfigGate
{
    public static void RegisterConditional(AnalysisContext context, string ruleId, Action<SyntaxNodeAnalysisContext> analyze, SyntaxKind kind)
    {
        context.RegisterCompilationStartAction(compilationStart =>
        {
            if (!IsEnabled(compilationStart.Options, ruleId))
            {
                return;
            }

            compilationStart.RegisterSyntaxNodeAction(analyze, kind);
        });
    }

    internal static bool IsEnabled(AnalyzerOptions options, string ruleId) => IsEnabledDirect(options, ruleId);

    internal static bool IsEnabledDirect(AnalyzerOptions options, string ruleId)
    {
        foreach (var file in options.AdditionalFiles)
        {
            var name = file.Path;
            var slash = name.LastIndexOf('/');
            if (slash >= 0)
            {
                name = name.Substring(slash + 1);
            }

            var backslash = name.LastIndexOf('\\');
            if (backslash >= 0)
            {
                name = name.Substring(backslash + 1);
            }

            if (!name.Equals(XamlDesignGenerator.ConfigFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = file.GetText();
            if (text == null)
            {
                continue;
            }

            var load = ConfigLoader.LoadFromText(text.ToString());
            if (load.HasErrors || load.Config == null)
            {
                continue; // invalid config: analyzer stays enabled (generator self-disables separately)
            }

            return load.Config.IsEnabled(ruleId);
        }

        return true; // no config injected: engine default (enabled)
    }
}
