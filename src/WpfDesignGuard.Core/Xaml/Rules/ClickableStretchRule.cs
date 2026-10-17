using System;
using System.Collections.Generic;
using System.Globalization;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Core.Xaml.Rules;

/// <summary>
/// DG0009: a clickable control stretched across the row (HorizontalAlignment="Stretch").
/// Exceptions are registered in the exemption ledger; the rule reports every occurrence
/// and the governance layer consumes registered exemptions.
/// </summary>
public sealed class ClickableStretchRule : XamlRule
{
    public ClickableStretchRule()
        : base(RuleCatalog.TryGet("DG0009")!)
    {
    }

    public override IReadOnlyList<RuleFinding> Evaluate(XamlIndex index, DesignGuardConfig config, string file)
    {
        var findings = new List<RuleFinding>();
        var clickable = new HashSet<string>(config.Tokens.ClickableTypes, StringComparer.Ordinal);

        foreach (var element in index.Document.Descendants())
        {
            if (!clickable.Contains(element.Name.LocalName))
            {
                continue;
            }

            var alignment = index.EffectiveAttributeValue(element, "HorizontalAlignment");
            if (!string.Equals(alignment, "Stretch", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var (line, column) = index.PositionOf(element) ?? (1, 1);
            findings.Add(new RuleFinding(
                Id, file, line, column, element.Name.LocalName.Length,
                string.Format(CultureInfo.InvariantCulture, Descriptor.MessageFormat, element.Name.LocalName)));
        }

        return findings;
    }
}
