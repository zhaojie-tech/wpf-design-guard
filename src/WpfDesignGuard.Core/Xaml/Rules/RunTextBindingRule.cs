using System;
using System.Collections.Generic;
using System.Globalization;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Core.Xaml.Rules;

/// <summary>
/// DG0015: Run.Text bindings must carry an explicit Mode=OneWay — the default TwoWay
/// crashes at load time against read-only sources (23 production incident sites).
/// Bindings with an explicit non-OneWay mode are equally unsafe and are reported;
/// literal text and markup extensions other than Binding are skipped.
/// </summary>
public sealed class RunTextBindingRule : XamlRule
{
    public RunTextBindingRule()
        : base(RuleCatalog.TryGet("DG0015")!)
    {
    }

    public override IReadOnlyList<RuleFinding> Evaluate(XamlIndex index, DesignGuardConfig config, string file)
    {
        var findings = new List<RuleFinding>();

        foreach (var element in index.Document.Descendants())
        {
            if (element.Name.LocalName != "Run")
            {
                continue;
            }

            var text = index.EffectiveAttributeValue(element, "Text");
            if (text == null || !IsBinding(text))
            {
                continue;
            }

            if (HasModeOneWay(text))
            {
                continue;
            }

            var attr = element.Attribute("Text");
            var (line, col, len) = (1, 1, text.Length);
            if (attr != null)
            {
                var span = index.ValueSpanOf(attr);
                var pos = index.PositionOf(attr);
                (line, col, len) = span != null
                    ? (index.PositionOf(span.Value.Offset).Line, index.PositionOf(span.Value.Offset).Column, span.Value.Length)
                    : (pos?.Line ?? 1, pos?.Column ?? 1, attr.Value.Length);
            }

            findings.Add(new RuleFinding(
                Id, file, line, col, len,
                string.Format(CultureInfo.InvariantCulture, Descriptor.MessageFormat, TrimForMessage(text))));
        }

        return findings;
    }

    internal static bool IsBinding(string value) =>
        value.TrimStart().StartsWith("{Binding", StringComparison.Ordinal);

    internal static bool HasModeOneWay(string value)
    {
        // Scan Mode= occurrences; XAML enum values are case-insensitive.
        var hay = value;
        var idx = hay.IndexOf("Mode=", StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            var start = idx + "Mode=".Length;
            var end = start;
            while (end < hay.Length && hay[end] != ',' && hay[end] != '}')
            {
                end++;
            }

            if (string.Equals(hay.Substring(start, end - start).Trim(), "OneWay", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            idx = hay.IndexOf("Mode=", idx + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string TrimForMessage(string v) =>
        v.Length > 40 ? v.Substring(0, 37) + "..." : v;
}
