using System.Collections.Generic;
using System.Globalization;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Core.Xaml.Rules;

/// <summary>
/// DG0006: bare CornerRadius / MinWidth / MaxWidth literals must be tokens.
/// Generator surface scans the three forms (attribute, property element, Setter Value);
/// the C# surface (assignments in code-behind) runs as a classic analyzer.
/// </summary>
public sealed class BareDimensionRule : XamlRule
{
    private static readonly string[] Properties = { "CornerRadius", "MinWidth", "MaxWidth" };

    public BareDimensionRule()
        : base(RuleCatalog.TryGet("DG0006")!)
    {
    }

    public override IReadOnlyList<RuleFinding> Evaluate(XamlIndex index, DesignGuardConfig config, string file)
    {
        var findings = new List<RuleFinding>();
        foreach (var occ in index.CollectPropertyOccurrences(Properties))
        {
            if (XamlValue.IsMarkupExtension(occ.Value))
            {
                continue;
            }

            var isNumeric = XamlValue.IsNumber(occ.Value, out _) || XamlValue.TryParseThickness(occ.Value) != null;
            if (!isNumeric)
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
