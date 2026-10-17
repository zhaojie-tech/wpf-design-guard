using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Core.Xaml.Rules;

/// <summary>
/// DG0005 (generator surface): bare color hex literals (#RGB/#RRGGBB/#AARRGGBB) must be
/// brush tokens. Scans every attribute value, property-element text and Setter.Value text.
/// The C# surface (Colors.* references) runs as a classic analyzer.
/// </summary>
public sealed class BareColorRule : XamlRule
{
    private static readonly Regex HexColor = new(
        @"^#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$",
        RegexOptions.Compiled);

    public BareColorRule()
        : base(RuleCatalog.TryGet("DG0005")!)
    {
    }

    public override IReadOnlyList<RuleFinding> Evaluate(XamlIndex index, DesignGuardConfig config, string file)
    {
        var findings = new List<RuleFinding>();

        foreach (var element in index.Document.Descendants())
        {
            foreach (var attr in element.Attributes())
            {
                if (attr.IsNamespaceDeclaration)
                {
                    continue;
                }

                if (HexColor.IsMatch(attr.Value.Trim()))
                {
                    var span = index.ValueSpanOf(attr);
                    var pos = index.PositionOf(attr);
                    var (line, col, len) = span != null
                        ? (index.PositionOf(span.Value.Offset).Line, index.PositionOf(span.Value.Offset).Column, span.Value.Length)
                        : (pos?.Line ?? 1, pos?.Column ?? 1, attr.Value.Length);
                    findings.Add(Make(file, attr.Value.Trim(), line, col, len));
                }
            }

            // Property elements and Setter.Value children carry the literal as text.
            if (!element.HasElements && !element.HasAttributes && element.Name.LocalName.IndexOf('.') > 0)
            {
                var text = element.Value.Trim();
                if (text.Length > 0 && HexColor.IsMatch(text))
                {
                    var textNode = element.Nodes().OfType<System.Xml.Linq.XText>().First();
                    var pos = index.PositionOf(textNode);
                    var lead = textNode.Value.Length - textNode.Value.TrimStart().Length;
                    findings.Add(Make(file, text, pos?.Line ?? 1, (pos?.Column ?? 1) + lead, text.Length));
                }
            }
        }

        return findings;
    }

    private RuleFinding Make(string file, string value, int line, int col, int len) =>
        new(Id, file, line, col, len,
            string.Format(CultureInfo.InvariantCulture, Descriptor.MessageFormat, value));
}
