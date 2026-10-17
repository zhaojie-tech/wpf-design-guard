using System.Collections.Generic;
using System.Globalization;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Core.Xaml.Rules;

/// <summary>
/// DG0007: a TextBlock with TextWrapping="Wrap" must set both LineHeight and
/// LineStackingStrategy. Wrapping mode "WrapWithOverflow" is intentionally out of scope
/// (the original judgement was on "Wrap" only); this is recorded on the rule page.
/// </summary>
public sealed class WrappedTextLineHeightRule : XamlRule
{
    public WrappedTextLineHeightRule()
        : base(RuleCatalog.TryGet("DG0007")!)
    {
    }

    public override IReadOnlyList<RuleFinding> Evaluate(XamlIndex index, DesignGuardConfig config, string file)
    {
        var findings = new List<RuleFinding>();

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

            var missingLineHeight = lineHeight == null || XamlValue.IsMarkupExtension(lineHeight);
            var missingStrategy = strategy == null || XamlValue.IsMarkupExtension(strategy);
            if (!missingLineHeight && !missingStrategy)
            {
                continue;
            }

            var (line, column) = index.PositionOf(element) ?? (1, 1);
            findings.Add(new RuleFinding(
                Id,
                file,
                line,
                column,
                element.Name.LocalName.Length,
                string.Format(
                    CultureInfo.InvariantCulture,
                    Descriptor.MessageFormat,
                    wrapping)));
        }

        return findings;
    }
}
