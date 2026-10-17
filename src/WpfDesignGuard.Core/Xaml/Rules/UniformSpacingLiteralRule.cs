using System.Collections.Generic;
using System.Globalization;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Core.Xaml.Rules;

/// <summary>
/// DG0003: uniform single-value Margin/Padding literals ("8") — use a spacing token or
/// the directional form. Multi-component literals belong to DG0002; markup extensions skip.
/// </summary>
public sealed class UniformSpacingLiteralRule : XamlRule
{
    public UniformSpacingLiteralRule()
        : base(RuleCatalog.TryGet("DG0003")!)
    {
    }

    public override IReadOnlyList<RuleFinding> Evaluate(XamlIndex index, DesignGuardConfig config, string file)
    {
        var findings = new List<RuleFinding>();
        foreach (var occ in index.CollectPropertyOccurrences("Margin", "Padding"))
        {
            if (XamlValue.IsMarkupExtension(occ.Value))
            {
                continue;
            }

            var components = XamlValue.TryParseThickness(occ.Value);
            if (components == null || components.Length != 1)
            {
                continue;
            }

            findings.Add(new RuleFinding(
                Id, file, occ.Line, occ.Column, occ.Length,
                string.Format(CultureInfo.InvariantCulture, Descriptor.MessageFormat,
                    occ.PropertyName, occ.Value.Trim())));
        }

        return findings;
    }
}
