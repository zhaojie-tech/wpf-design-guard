using System.Collections.Generic;
using System.Globalization;
using System.Text;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Core.Xaml.Rules;

/// <summary>
/// DG0002: a multi-component Margin/Padding literal with at least one component off the
/// spacing ladder. Single-component literals belong to DG0003; markup extensions are
/// skipped. Reports one finding per offending component, span on that component.
/// </summary>
public sealed class SpacingLadderRule : XamlRule
{
    public SpacingLadderRule()
        : base(RuleCatalog.TryGet("DG0002")!)
    {
    }

    public override IReadOnlyList<RuleFinding> Evaluate(XamlIndex index, DesignGuardConfig config, string file)
    {
        var ladder = new HashSet<double>(config.Tokens.SpacingLadder);
        var findings = new List<RuleFinding>();

        foreach (var occ in index.CollectPropertyOccurrences("Margin", "Padding"))
        {
            if (XamlValue.IsMarkupExtension(occ.Value))
            {
                continue;
            }

            var components = XamlValue.TryParseThickness(occ.Value);
            if (components == null || components.Length == 1)
            {
                continue; // single-value form is DG0003's surface
            }

            // Map each comma/space component back to its span inside the raw value.
            var componentSpans = SplitComponentSpans(occ.Value);

            for (var i = 0; i < components.Length; i++)
            {
                if (!ladder.Contains(components[i]))
                {
                    var (cLine, cCol) = (occ.Line, occ.Column);
                    var (offset, length) = componentSpans[i];
                    var col = cCol + offset;
                    findings.Add(new RuleFinding(
                        Id,
                        file,
                        cLine,
                        col,
                        length,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            Descriptor.MessageFormat,
                            FormatNumber(components[i]),
                            occ.PropertyName,
                            occ.Value.Trim(),
                            FormatLadder(config.Tokens.SpacingLadder))));
                }
            }
        }

        return findings;
    }

    internal static string FormatLadder(IEnumerable<double> ladder)
    {
        var sb = new StringBuilder("[");
        var first = true;
        foreach (var v in ladder)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            sb.Append(FormatNumber(v));
            first = false;
        }

        return sb.Append(']').ToString();
    }

    internal static string FormatNumber(double v) =>
        v == System.Math.Floor(v) && !double.IsInfinity(v)
            ? ((int)v).ToString(CultureInfo.InvariantCulture)
            : v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Returns (relativeOffset, length) of each comma/space component inside the raw value.</summary>
    private static List<(int Offset, int Length)> SplitComponentSpans(string value)
    {
        var result = new List<(int, int)>();
        var i = 0;
        while (i < value.Length)
        {
            while (i < value.Length && (value[i] == ',' || value[i] == ' ' || value[i] == '\t'))
            {
                i++;
            }

            if (i >= value.Length)
            {
                break;
            }

            var start = i;
            while (i < value.Length && value[i] != ',' && value[i] != ' ' && value[i] != '\t')
            {
                i++;
            }

            result.Add((start, i - start));
        }

        return result;
    }
}
