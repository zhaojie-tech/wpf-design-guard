using System;
using System.Collections.Generic;
using System.Linq;
using WpfDesignGuard.Core.Configuration;

namespace WpfDesignGuard.Core.Xaml.Fixes;

public sealed class FixApplication
{
    public FixApplication(string file, string newText, int appliedCount, IReadOnlyList<string> unfixed)
    {
        File = file;
        NewText = newText;
        AppliedCount = appliedCount;
        Unfixed = unfixed;
    }

    public string File { get; }

    /// <summary>The fixed text (== input text when nothing was applied).</summary>
    public string NewText { get; }

    public int AppliedCount { get; }

    /// <summary>Occurrences the fixer could not handle safely (kept verbatim, surfaced to the user).</summary>
    public IReadOnlyList<string> Unfixed { get; }
}

/// <summary>
/// Span-level text editing for the CLI `fix` command: reuses evaluation
/// spans, never re-serializes XML (no self-closing/namespace/diff noise). Fix directions
/// are fixed by design: DG0002 → nearest ladder step (tie → smaller); DG0003 → directional
/// "N,N" form; DG0007 → LineHeight from the tokens.lineHeights map (+ BlockLineHeight);
/// DG0015 → insert ", Mode=OneWay". After a fix the text is RE-EVALUATED and an edit that
/// would trip an already-shipped rule is rolled back (self-check, forward compatible:
/// rules not yet implemented are skipped by the self-check).
/// </summary>
public static class XamlFixEngine
{
    public static bool Supports(string ruleId) =>
        ruleId is "DG0002" or "DG0003" or "DG0007" or "DG0015";

    public static FixApplication FixFile(string text, string file, string ruleId, DesignGuardConfig config)
    {
        if (!Supports(ruleId))
        {
            return new FixApplication(file, text, 0, new[] { $"rule {ruleId} has no CLI fix (ships in a later batch)" });
        }

        var index = XamlIndex.Parse(text);
        var edits = new List<(int Offset, int Length, string Replacement)>();
        var unfixed = new List<string>();

        switch (ruleId)
        {
            case "DG0002":
                FixSpacingLadder(index, config, edits, unfixed);
                break;
            case "DG0003":
                FixUniformSingleValue(index, edits, unfixed);
                break;
            case "DG0007":
                FixLineHeight(index, config, text, edits, unfixed);
                break;
            case "DG0015":
                FixRunBinding(index, text, edits, unfixed);
                break;
        }

        if (edits.Count == 0)
        {
            return new FixApplication(file, text, 0, unfixed);
        }

        // Apply bottom-up so offsets stay valid.
        var ordered = edits.OrderByDescending(e => e.Offset).ToList();
        var newText = text;
        foreach (var (offset, length, replacement) in ordered)
        {
            newText = newText.Substring(0, offset) + replacement + newText.Substring(offset + length);
        }

        // Self-check: re-evaluate all SHIPPED rules; a fix that creates new findings for the
        // fixed rule (or any other shipped rule) rolls the whole file back for this rule.
        var before = CountFindings(text, file, config);
        var after = CountFindings(newText, file, config);
        var rolledBack = new List<string>(unfixed);
        foreach (var kv in after)
        {
            var delta = kv.Value - (before.TryGetValue(kv.Key, out var b) ? b : 0);
            if (delta > 0)
            {
                rolledBack.Add($"self-check rolled back: fixing {ruleId} would introduce {delta} new {kv.Key} finding(s)");
                return new FixApplication(file, text, 0, rolledBack);
            }
        }

        return new FixApplication(file, newText, ordered.Count, rolledBack);
    }

    private static Dictionary<string, int> CountFindings(string text, string file, DesignGuardConfig config)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = XamlRuleEngine.Evaluate(text, file, config);
        foreach (var grouping in result.Findings.GroupBy(f => f.RuleId))
        {
            map[grouping.Key] = grouping.Count();
        }

        return map;
    }

    private static void FixSpacingLadder(
        XamlIndex index, DesignGuardConfig config,
        List<(int, int, string)> edits, List<string> unfixed)
    {
        var ladder = config.Tokens.SpacingLadder.OrderBy(v => v).ToList();
        foreach (var occ in index.CollectPropertyOccurrences("Margin", "Padding"))
        {
            if (XamlValue.IsMarkupExtension(occ.Value))
            {
                continue;
            }

            var components = XamlValue.TryParseThickness(occ.Value);
            if (components == null || components.Length == 1)
            {
                continue;
            }

            var span = index.ValueSpanOfAttributeOccurrence(occ);
            if (span == null)
            {
                unfixed.Add($"{occ.PropertyName}='{occ.Value.Trim()}': value span not resolved");
                continue;
            }

            // Re-split the raw value with offsets; replace only offending components.
            var raw = occ.Value;
            var pos = span.Value.Offset;
            var i = 0;
            while (i < raw.Length)
            {
                while (i < raw.Length && (raw[i] == ',' || raw[i] == ' ' || raw[i] == '\t'))
                {
                    i++;
                }

                if (i >= raw.Length)
                {
                    break;
                }

                var start = i;
                while (i < raw.Length && raw[i] != ',' && raw[i] != ' ' && raw[i] != '\t')
                {
                    i++;
                }

                var token = raw.Substring(start, i - start);
                if (!XamlValue.IsNumber(token, out var value))
                {
                    continue;
                }

                if (!ladder.Contains(value))
                {
                    var nearest = Nearest(ladder, value);
                    edits.Add((pos + start, i - start, FormatNumber(nearest)));
                }
            }
        }
    }

    private static void FixUniformSingleValue(
        XamlIndex index, List<(int, int, string)> edits, List<string> unfixed)
    {
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

            var span = index.ValueSpanOfAttributeOccurrence(occ);
            if (span == null)
            {
                unfixed.Add($"{occ.PropertyName}='{occ.Value.Trim()}': value span not resolved");
                continue;
            }

            // Directional form: uniform N → "N,N" (leaves DG0002/DG0003 surfaces clean).
            edits.Add((span.Value.Offset, span.Value.Length, $"{FormatNumber(components[0])},{FormatNumber(components[0])}"));
        }
    }

    private static void FixLineHeight(
        XamlIndex index, DesignGuardConfig config, string text,
        List<(int, int, string)> edits, List<string> unfixed)
    {
        foreach (var element in index.Document.Descendants())
        {
            if (element.Name.LocalName != "TextBlock")
            {
                continue;
            }

            var wrapping = index.EffectiveAttributeValue(element, "TextWrapping");
            if (wrapping == null || XamlValue.IsMarkupExtension(wrapping) || wrapping != "Wrap")
            {
                continue;
            }

            var lineHeight = index.EffectiveAttributeValue(element, "LineHeight");
            var strategy = index.EffectiveAttributeValue(element, "LineStackingStrategy");

            var insertAt = index.OffsetOfElementNameEnd(element);
            if (insertAt == null)
            {
                unfixed.Add("TextBlock: element span not resolved");
                continue;
            }

            var insertions = new List<string>();
            if (lineHeight == null || XamlValue.IsMarkupExtension(lineHeight))
            {
                var height = InferLineHeight(index, element, config);
                if (height == null)
                {
                    unfixed.Add("TextBlock (wrapped): cannot infer LineHeight — set tokens.lineHeights or an explicit FontSize");
                    continue;
                }

                insertions.Add($" LineHeight=\"{FormatNumber(height.Value)}\"");
            }

            if (strategy == null || XamlValue.IsMarkupExtension(strategy))
            {
                insertions.Add(" LineStackingStrategy=\"BlockLineHeight\"");
            }

            if (insertions.Count > 0)
            {
                edits.Add((insertAt.Value, 0, string.Concat(insertions)));
            }
        }
    }

    private static double? InferLineHeight(XamlIndex index, System.Xml.Linq.XElement element, DesignGuardConfig config)
    {
        var fontSizeRaw = index.EffectiveAttributeValue(element, "FontSize");
        if (fontSizeRaw == null)
        {
            return null;
        }

        if (XamlValue.IsMarkupExtension(fontSizeRaw))
        {
            // {DynamicResource Font.Body} → token family keys ("Font.*") map by their
            // trailing segment to the lineHeights keys ("Body").
            var token = fontSizeRaw.Trim().Trim('{', '}').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
            var key = token?.Substring(token.LastIndexOf('.') + 1);
            return key != null && config.Tokens.LineHeights.TryGetValue(key, out var byKey) ? byKey : null;
        }

        if (XamlValue.IsNumber(fontSizeRaw, out var size))
        {
            // Map by value: fontSizes key of this size → lineHeights of the same key.
            foreach (var kv in config.Tokens.FontSizes)
            {
                if (System.Math.Abs(kv.Value - size) < 0.01 && config.Tokens.LineHeights.TryGetValue(kv.Key, out var lh))
                {
                    return lh;
                }
            }
        }

        return null;
    }

    private static void FixRunBinding(
        XamlIndex index, string text, List<(int, int, string)> edits, List<string> unfixed)
    {
        foreach (var element in index.Document.Descendants())
        {
            if (element.Name.LocalName != "Run")
            {
                continue;
            }

            var attr = element.Attribute("Text");
            if (attr == null)
            {
                continue;
            }

            var raw = attr.Value;
            if (!raw.TrimStart().StartsWith("{Binding", StringComparison.Ordinal) || RunTextBindingRuleShim.HasModeOneWay(raw))
            {
                continue;
            }

            if (raw.Contains("Mode=", StringComparison.OrdinalIgnoreCase))
            {
                unfixed.Add($"Run Text='{raw}': explicit non-OneWay mode needs a human decision (not auto-fixed)");
                continue;
            }

            var span = index.ValueSpanOf(attr);
            if (span == null)
            {
                unfixed.Add($"Run Text='{raw}': value span not resolved");
                continue;
            }

            var close = text.IndexOf('}', span.Value.Offset, span.Value.Length);
            if (close < 0)
            {
                unfixed.Add($"Run Text='{raw}': closing brace not found");
                continue;
            }

            var insert = raw.TrimEnd().EndsWith(",", StringComparison.Ordinal) ? " Mode=OneWay" : ", Mode=OneWay";
            edits.Add((close, 0, insert));
        }
    }

    /// <summary>Nearest ladder step; ties resolve to the smaller step.</summary>
    public static double Nearest(IReadOnlyList<double> ladder, double value)
    {
        var best = ladder[0];
        var bestDelta = System.Math.Abs(best - value);
        foreach (var candidate in ladder)
        {
            var delta = System.Math.Abs(candidate - value);
            if (delta < bestDelta || (delta == bestDelta && candidate < best))
            {
                best = candidate;
                bestDelta = delta;
            }
        }

        return best;
    }

    private static string FormatNumber(double v) =>
        v == Math.Floor(v) && !double.IsInfinity(v)
            ? ((int)v).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : v.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Shim so the fixer can reuse DG0015's mode detection without rule coupling.</summary>
internal static class RunTextBindingRuleShim
{
    public static bool HasModeOneWay(string value) => Rules.RunTextBindingRule.HasModeOneWay(value);
}
