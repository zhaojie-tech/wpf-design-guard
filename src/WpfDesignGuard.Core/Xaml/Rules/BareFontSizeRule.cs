using System.Collections.Generic;
using System.Globalization;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Core.Xaml.Rules;

/// <summary>
/// DG0001 (generator surface): bare FontSize number literals must become font tokens.
/// Markup extensions ({DynamicResource Font.Body}, {Binding …}, {"}{}" escapes) are tokens
/// or intentional literals and are skipped. The C# surface runs as a classic
/// analyzer; the fix ships as a C# code fix (placeholder token reference until the
/// v0.2 Tokens package).
/// </summary>
public sealed class BareFontSizeRule : XamlRule
{
    public BareFontSizeRule()
        : base(RuleCatalog.TryGet("DG0001")!)
    {
    }

    public override IReadOnlyList<RuleFinding> Evaluate(XamlIndex index, DesignGuardConfig config, string file)
    {
        var findings = new List<RuleFinding>();
        foreach (var occ in index.CollectPropertyOccurrences("FontSize"))
        {
            if (XamlValue.IsMarkupExtension(occ.Value))
            {
                continue;
            }

            if (!XamlValue.IsNumber(occ.Value, out _))
            {
                continue;
            }

            findings.Add(new RuleFinding(
                Id, file, occ.Line, occ.Column, occ.Length,
                string.Format(CultureInfo.InvariantCulture, Descriptor.MessageFormat, occ.Value.Trim())));
        }

        return findings;
    }
}
