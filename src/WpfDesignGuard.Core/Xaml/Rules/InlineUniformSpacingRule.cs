using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Globbing;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Core.Xaml.Rules;

/// <summary>
/// DG0004 (heuristic, Warning): an inline input element (a horizontal stack panel's
/// child) using four-side-equal spacing. The vertical component participates in the
/// Auto row height, so inline rows end up with unequal heights.
/// Uniform spacing = a single-value literal, a four-equal literal, or a token key
/// matching tokens.uniformSpacingKeys ("Spacing.*" by default).
/// </summary>
public sealed class InlineUniformSpacingRule : XamlRule
{
    public InlineUniformSpacingRule()
        : base(RuleCatalog.TryGet("DG0004")!)
    {
    }

    public override IReadOnlyList<RuleFinding> Evaluate(XamlIndex index, DesignGuardConfig config, string file)
    {
        var findings = new List<RuleFinding>();
        var inputs = new HashSet<string>(config.Tokens.InlineInputTypes, StringComparer.Ordinal);
        var uniformKeys = config.Tokens.UniformSpacingKeys.Select(k => new GlobMatcher(k)).ToList();

        foreach (var element in index.Document.Descendants())
        {
            if (!inputs.Contains(element.Name.LocalName))
            {
                continue;
            }

            var parent = element.Parent;
            if (parent == null || parent.Name.LocalName != "StackPanel")
            {
                continue;
            }

            var orientation = index.EffectiveAttributeValue(parent, "Orientation");
            if (!string.Equals(orientation, "Horizontal", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var property in new[] { "Margin", "Padding" })
            {
                var value = index.EffectiveAttributeValue(element, property);
                if (value == null || !IsUniform(value, uniformKeys, out var isToken))
                {
                    continue;
                }

                var pos = index.PositionOf(element) ?? (1, 1);
                findings.Add(new RuleFinding(
                    Id, file, pos.Item1, pos.Item2, element.Name.LocalName.Length,
                    string.Format(CultureInfo.InvariantCulture, Descriptor.MessageFormat,
                        element.Name.LocalName, value)));
            }
        }

        return findings;
    }

    private static bool IsUniform(string value, IReadOnlyList<GlobMatcher> uniformKeys, out bool isToken)
    {
        isToken = false;
        if (XamlValue.IsMarkupExtension(value))
        {
            // {DynamicResource Spacing.Md} → key Spacing.Md; match the configured families.
            var key = value.Trim().Trim('{', '}').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()?.Trim();
            if (key == null)
            {
                return false;
            }

            foreach (var matcher in uniformKeys)
            {
                if (matcher.IsMatch(key))
                {
                    isToken = true;
                    return true;
                }
            }

            return false;
        }

        var components = XamlValue.TryParseThickness(value);
        if (components == null)
        {
            return false;
        }

        return components.All(c => Math.Abs(c - components[0]) < 0.01);
    }
}
