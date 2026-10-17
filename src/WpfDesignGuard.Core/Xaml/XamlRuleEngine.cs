using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Core.Xaml;

public sealed class XamlEvaluationResult
{
    public bool ParseSucceeded { get; set; }

    public string? ParseError { get; set; }

    /// <summary>Parsed index (comments stripped); null when parsing failed. Lets callers
    /// resolve finding positions to absolute offsets without re-parsing.</summary>
    public XamlIndex? Index { get; set; }

    public List<RuleFinding> Findings { get; } = new();

    /// <summary>Catalog rules whose implementation has not shipped at the engine version.</summary>
    public List<string> SkippedNotImplemented { get; } = new();
}

/// <summary>
/// Single-source XAML rule evaluation: one parser, one rule set, one
/// scope filter — consumed verbatim by the generator and the CLI. Governance layers
/// (exemptions / hard-zero / ratchet) stay in <see cref="GovernanceEvaluator"/>.
/// </summary>
public static class XamlRuleEngine
{
    /// <summary>Implemented rule instances, in ID order. Extend as rules ship.</summary>
    public static IReadOnlyList<Rules.XamlRule> CreateRules() => new Rules.XamlRule[]
    {
        new Rules.BareFontSizeRule(),
        new Rules.SpacingLadderRule(),
        new Rules.UniformSpacingLiteralRule(),
        new Rules.InlineUniformSpacingRule(),
        new Rules.BareColorRule(),
        new Rules.BareDimensionRule(),
        new Rules.WrappedTextLineHeightRule(),
        new Rules.HitTargetFloorRule(),
        new Rules.ClickableStretchRule(),
        new Rules.InlineStyleBasedOnRule(),
        new Rules.CloseGlyphRule(),
        new Rules.ButtonCopyRule(),
        new Rules.RunTextBindingRule(),
        new Rules.MaxWidthCollapseRule(),
    };

    public static XamlEvaluationResult Evaluate(string xamlText, string file, DesignGuardConfig config)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        var result = new XamlEvaluationResult();

        XamlIndex index;
        try
        {
            index = XamlIndex.Parse(xamlText);
        }
        catch (XmlException ex)
        {
            result.ParseSucceeded = false;
            result.ParseError = ex.Message;
            return result;
        }

        result.ParseSucceeded = true;
        result.Index = index;
        var rules = CreateRules();

        foreach (var rule in rules)
        {
            if (!config.IsEnabled(rule.Id))
            {
                continue;
            }

            // Per-rule scan surface (scope.perRule): a file outside a rule's surface
            // produces no findings for that rule — the same filter for both engines.
            var effective = EffectiveScope.Resolve(config.Scope, rule.Id);
            if (!effective.Matches(file))
            {
                continue;
            }

            if (!RuleCatalog.IsImplementedAt(rule.Descriptor, GuardVersion.Current))
            {
                result.SkippedNotImplemented.Add(rule.Id);
                continue;
            }

            result.Findings.AddRange(rule.Evaluate(index, config, file));
        }

        // Catalog rules not yet implemented at this engine version: visible as unchecked
        // surface, never silent. Only rules on the file's own surface count. DG0012 is a
        // generator-surface rule implemented by the symbol stage of the generator (it needs
        // the Compilation), so it is not a "not implemented" entry here.
        foreach (var descriptor in RuleCatalog.All)
        {
            if (descriptor.Surfaces.HasFlag(RuleSurface.Generator) &&
                !rules.Any(r => r.Id == descriptor.Id) &&
                descriptor.Id != "DG0012" &&
                config.IsEnabled(descriptor.Id) &&
                EffectiveScope.Resolve(config.Scope, descriptor.Id).Matches(file))
            {
                result.SkippedNotImplemented.Add(descriptor.Id);
            }
        }

        return result;
    }
}
