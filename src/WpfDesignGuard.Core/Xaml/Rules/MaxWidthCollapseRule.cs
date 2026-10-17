using System;
using System.Collections.Generic;
using System.Globalization;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Core.Xaml.Rules;

/// <summary>
/// DG0016: an input element (tokens.inlineInputTypes) given both MaxWidth and
/// HorizontalAlignment="Left" collapses to content width — the bounded-width
/// anti-pattern from layout rework waves. The MaxWidth value form does not matter
/// (literal or token reference both constrain), the combination does.
/// </summary>
public sealed class MaxWidthCollapseRule : XamlRule
{
    public MaxWidthCollapseRule()
        : base(RuleCatalog.TryGet("DG0016")!)
    {
    }

    public override IReadOnlyList<RuleFinding> Evaluate(XamlIndex index, DesignGuardConfig config, string file)
    {
        var findings = new List<RuleFinding>();
        var inlineInputs = new HashSet<string>(config.Tokens.InlineInputTypes, StringComparer.Ordinal);

        foreach (var element in index.Document.Descendants())
        {
            if (!inlineInputs.Contains(element.Name.LocalName))
            {
                continue;
            }

            var maxWidth = element.Attribute("MaxWidth");
            var alignment = element.Attribute("HorizontalAlignment");
            if (maxWidth == null || alignment == null)
            {
                continue;
            }

            if (!string.Equals(alignment.Value.Trim(), "Left", StringComparison.OrdinalIgnoreCase))
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
