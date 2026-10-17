using System;
using System.Collections.Generic;
using System.Globalization;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Core.Xaml.Rules;

/// <summary>
/// DG0011: close semantics encoded as a glyph character (× ✕ ✖) instead of the close
/// button component. Scans Content/Text/Header values across attribute and
/// property-element forms.
/// </summary>
public sealed class CloseGlyphRule : XamlRule
{
    private static readonly char[] Glyphs = { '\u00D7', '\u2715', '\u2716' }; // × ✕ ✖
    private static readonly string[] Properties = { "Content", "Text", "Header" };

    public CloseGlyphRule()
        : base(RuleCatalog.TryGet("DG0011")!)
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

            var glyph = FindGlyph(occ.Value);
            if (glyph == null)
            {
                continue;
            }

            findings.Add(new RuleFinding(
                Id, file, occ.Line, occ.Column, occ.Length,
                string.Format(CultureInfo.InvariantCulture, Descriptor.MessageFormat, glyph)));
        }

        return findings;
    }

    private static string? FindGlyph(string value)
    {
        foreach (var c in Glyphs)
        {
            if (value.IndexOf(c) >= 0)
            {
                return c.ToString();
            }
        }

        return null;
    }
}
