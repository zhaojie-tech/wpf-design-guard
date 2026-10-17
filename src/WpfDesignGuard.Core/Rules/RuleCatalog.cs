using System;
using System.Collections.Generic;
using System.Linq;

namespace WpfDesignGuard.Core.Rules;

/// <summary>
/// The single rule catalog. All v0.1-planned IDs are allocated here;
/// rules not yet implemented are skipped by the engine and surfaced in the
/// "unchecked surface" section of reports (never silently).
/// </summary>
public static class RuleCatalog
{
    private static readonly RuleDescriptor[] AllRules =
    {
        new(
            "DG0001",
            "Bare FontSize literal",
            "FontSize literal '{0}' is not a design token; use a fontSizes token (e.g. {{DynamicResource Font.Body}}).",
            RuleSeverity.Error, RuleDeterminism.Deterministic,
            RuleSurface.Generator | RuleSurface.CSharpAnalyzer, RuleFixSupport.CSharpCodeFix,
            "0.1.0", tokenDependent: true),
        new(
            "DG0002",
            "Margin/Padding off the spacing ladder",
            "Component '{0}' of {1} '{2}' is not on the spacing ladder {3}.",
            RuleSeverity.Error, RuleDeterminism.Deterministic,
            RuleSurface.Generator, RuleFixSupport.CliFix,
            "0.1.0", tokenDependent: true),
        new(
            "DG0003",
            "Uniform single-value Margin/Padding literal",
            "{0} uses a uniform single-value literal '{1}'; use a spacing token or directional form.",
            RuleSeverity.Error, RuleDeterminism.Deterministic,
            RuleSurface.Generator, RuleFixSupport.CliFix,
            "0.1.0", tokenDependent: true),
        new(
            "DG0004",
            "Uniform spacing on inline input elements",
            "Inline '{0}' uses uniform spacing '{1}'; the vertical component participates in Auto row height.",
            RuleSeverity.Warning, RuleDeterminism.Heuristic,
            RuleSurface.Generator, RuleFixSupport.None,
            "0.2.0", tokenDependent: true),
        new(
            "DG0005",
            "Bare color literal",
            "Bare color '{0}' must use a brush token.",
            RuleSeverity.Error, RuleDeterminism.Deterministic,
            RuleSurface.Generator | RuleSurface.CSharpAnalyzer, RuleFixSupport.CSharpCodeFix,
            "0.1.0", tokenDependent: true),
        new(
            "DG0006",
            "Bare CornerRadius/MinWidth/MaxWidth literal",
            "Bare '{0}' literal '{1}' is not a token reference.",
            RuleSeverity.Error, RuleDeterminism.Deterministic,
            RuleSurface.Generator | RuleSurface.CSharpAnalyzer, RuleFixSupport.None,
            "0.2.0", tokenDependent: true),
        new(
            "DG0007",
            "Wrapped TextBlock missing line height",
            "TextBlock with TextWrapping='{0}' must set LineHeight and LineStackingStrategy.",
            RuleSeverity.Error, RuleDeterminism.Deterministic,
            RuleSurface.Generator, RuleFixSupport.CliFix,
            "0.1.0"),
        new(
            "DG0008",
            "Clickable control below hit-target floor",
            "Explicit '{0}' value {1} on clickable type '{2}' is below the hit target floor {3}.",
            RuleSeverity.Error, RuleDeterminism.Deterministic,
            RuleSurface.Generator, RuleFixSupport.None,
            "0.2.0"),
        new(
            "DG0009",
            "Clickable control stretched across the row",
            "Clickable '{0}' uses HorizontalAlignment='Stretch' and is not registered as exempt.",
            RuleSeverity.Error, RuleDeterminism.Deterministic,
            RuleSurface.Generator, RuleFixSupport.None,
            "0.2.0"),
        new(
            "DG0010",
            "Inline style without BasedOn",
            "Inline <{0}.Style> has no BasedOn; it silently replaces the library style.",
            RuleSeverity.Error, RuleDeterminism.Deterministic,
            RuleSurface.Generator, RuleFixSupport.None,
            "0.1.0"),
        new(
            "DG0011",
            "Close glyph instead of close control",
            "Close semantics encoded as glyph '{0}'; use the close button component.",
            RuleSeverity.Error, RuleDeterminism.Deterministic,
            RuleSurface.Generator, RuleFixSupport.None,
            "0.2.0"),
        new(
            "DG0012",
            "Style Setter property existence",
            "Setter Property '{0}' does not exist on target type '{1}'.",
            RuleSeverity.Error, RuleDeterminism.Deterministic,
            RuleSurface.Generator, RuleFixSupport.None,
            "0.1.0"),
        new(
            "DG0013",
            "Parenthesis stacking / ellipsis abbreviation in button text",
            "Button text '{0}' uses {1}; rewrite the copy instead.",
            RuleSeverity.Warning, RuleDeterminism.Deterministic,
            RuleSurface.Generator, RuleFixSupport.None,
            "0.2.0"),
        new(
            "DG0014",
            "Implicit TextBlock style without base FontSize",
            "Implicit TextBlock style at '{0}' does not set a base FontSize (falls back to the WPF 12px default).",
            RuleSeverity.Error, RuleDeterminism.Deterministic,
            RuleSurface.Cli, RuleFixSupport.None,
            "0.2.0"),
        new(
            "DG0015",
            "Run.Text binding without explicit Mode=OneWay",
            "Run.Text binding on '{0}' has no explicit Mode=OneWay; the default TwoWay mode crashes on read-only sources.",
            RuleSeverity.Error, RuleDeterminism.Deterministic,
            RuleSurface.Generator, RuleFixSupport.CliFix,
            "0.1.0"),
        new(
            "DG0016",
            "MaxWidth combined with HorizontalAlignment=Left on input elements",
            "Input element '{0}' combines MaxWidth with HorizontalAlignment='Left' and collapses to content width.",
            RuleSeverity.Error, RuleDeterminism.Deterministic,
            RuleSurface.Generator, RuleFixSupport.None,
            "0.1.0"),
        new(
            "DG0017",
            "Hit-target type coverage gate",
            "Clickable type '{0}' has no MinHeight/MinWidth >= {1} setter at its style library definition site.",
            RuleSeverity.Error, RuleDeterminism.Deterministic,
            RuleSurface.Cli, RuleFixSupport.None,
            "0.2.0"),
    };

    /// <summary>All catalog entries, in ID order.</summary>
    public static IReadOnlyList<RuleDescriptor> All => AllRules;

    public static RuleDescriptor? TryGet(string id)
    {
        if (id is null)
        {
            throw new ArgumentNullException(nameof(id));
        }

        // IDs are case-sensitive by contract (they are public API surface).
        return AllRules.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));
    }

    /// <summary>True when the rule has a runtime implementation at the given semantic version.</summary>
    public static bool IsImplementedAt(RuleDescriptor rule, string semanticVersion)
    {
        return CompareSemVer(rule.FirstShippedIn, semanticVersion) <= 0;
    }

    /// <summary>Implemented rules whose judgement requires a configured token table.</summary>
    public static IReadOnlyList<RuleDescriptor> TokenDependentImplemented(string semanticVersion) =>
        AllRules.Where(r => r.TokenDependent && IsImplementedAt(r, semanticVersion)).ToList();

    private static int CompareSemVer(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        for (var i = 0; i < 3; i++)
        {
            var va = i < pa.Length ? int.Parse(pa[i], System.Globalization.CultureInfo.InvariantCulture) : 0;
            var vb = i < pb.Length ? int.Parse(pb[i], System.Globalization.CultureInfo.InvariantCulture) : 0;
            if (va != vb)
            {
                return va.CompareTo(vb);
            }
        }

        return 0;
    }
}
