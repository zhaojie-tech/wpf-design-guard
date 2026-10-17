using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Core.Xaml.Rules;

/// <summary>
/// DG0013 (Warning): button copy carries parenthesized clauses or ellipsis abbreviation.
/// Two judgements: parenthesis stacking and ellipsis abbreviation. No automatic fix —
/// rewriting user copy is not a mechanical operation.
/// </summary>
public sealed class ButtonCopyRule : XamlRule
{
    private static readonly HashSet<string> ButtonTypes = new(StringComparer.Ordinal)
    {
        "Button", "ToggleButton", "RepeatButton",
    };

    private static readonly string[] Properties = { "Content", "Text", "Header" };

    public ButtonCopyRule()
        : base(RuleCatalog.TryGet("DG0013")!)
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

            var owner = occ.SetterOwner ?? occ.OwnerElement;
            if (!ButtonTypes.Contains(owner.Name.LocalName))
            {
                continue;
            }

            var judgement = Judge(occ.Value);
            if (judgement == null)
            {
                continue;
            }

            findings.Add(new RuleFinding(
                Id, file, occ.Line, occ.Column, occ.Length,
                string.Format(CultureInfo.InvariantCulture, Descriptor.MessageFormat,
                    occ.Value.Trim(), judgement)));
        }

        return findings;
    }

    private static string? Judge(string value)
    {
        if (HasParenthesizedClause(value))
        {
            return "parenthesis stacking";
        }

        if (value.IndexOf('\u2026') >= 0 || value.Contains("..."))
        {
            return "ellipsis abbreviation";
        }

        return null;
    }

    private static bool HasParenthesizedClause(string value)
    {
        var pairs = new[] { ('(', ')'), ('\uFF08', '\uFF09') }; // ASCII + full-width
        foreach (var (open, close) in pairs)
        {
            var start = value.IndexOf(open);
            if (start >= 0 && value.IndexOf(close, start + 1) > start + 1)
            {
                return true;
            }
        }

        return false;
    }
}
