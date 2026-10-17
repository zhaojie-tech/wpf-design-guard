using System;
using System.Collections.Generic;
using System.Globalization;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Core.Xaml.Rules;

/// <summary>
/// DG0008: a clickable control with an EXPLICIT instance dimension below the hit-target
/// floor. Style-level floors are the coverage gate's job (DG0017) — this rule only judges
/// values written on the instance, which is the documented blind spot boundary.
/// Dimensions: MinHeight, MinWidth, Height, Width (MaxWidth is a cap, not a floor).
/// </summary>
public sealed class HitTargetFloorRule : XamlRule
{
    private static readonly string[] Dimensions = { "MinHeight", "MinWidth", "Height", "Width" };

    public HitTargetFloorRule()
        : base(RuleCatalog.TryGet("DG0008")!)
    {
    }

    public override IReadOnlyList<RuleFinding> Evaluate(XamlIndex index, DesignGuardConfig config, string file)
    {
        var findings = new List<RuleFinding>();
        var clickable = new HashSet<string>(config.Tokens.ClickableTypes, StringComparer.Ordinal);
        var floor = config.Tokens.HitTargetFloor;

        foreach (var element in index.Document.Descendants())
        {
            if (!clickable.Contains(element.Name.LocalName))
            {
                continue;
            }

            foreach (var dimension in Dimensions)
            {
                var raw = index.EffectiveAttributeValue(element, dimension);
                if (raw == null || XamlValue.IsMarkupExtension(raw) || !XamlValue.IsNumber(raw, out var value))
                {
                    continue;
                }

                if (value >= floor)
                {
                    continue;
                }

                var pos = index.PositionOf(element) ?? (1, 1);
                var (line, column) = pos;
                var attr = element.Attribute(dimension);
                if (attr != null)
                {
                    var span = index.ValueSpanOf(attr);
                    if (span != null)
                    {
                        (line, column) = index.PositionOf(span.Value.Offset);
                    }
                }

                findings.Add(new RuleFinding(
                    Id, file, line, column, Math.Max(1, raw.Length),
                    string.Format(CultureInfo.InvariantCulture, Descriptor.MessageFormat,
                        dimension, FormatNumber(value), element.Name.LocalName, FormatNumber(floor))));
            }
        }

        return findings;
    }

    private static string FormatNumber(double v) =>
        v == Math.Floor(v) ? ((int)v).ToString(CultureInfo.InvariantCulture) : v.ToString(CultureInfo.InvariantCulture);
}
