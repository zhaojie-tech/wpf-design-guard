using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Core.Xaml.Rules;

/// <summary>
/// DG0010: an inline &lt;X.Style&gt; property element whose &lt;Style&gt; lacks BasedOn
/// silently replaces the library style (compile green, UI broken). Only the inline
/// property-element form counts — style-library definitions are data, not call sites.
/// </summary>
public sealed class InlineStyleBasedOnRule : XamlRule
{
    public InlineStyleBasedOnRule()
        : base(RuleCatalog.TryGet("DG0010")!)
    {
    }

    public override IReadOnlyList<RuleFinding> Evaluate(XamlIndex index, DesignGuardConfig config, string file)
    {
        var findings = new List<RuleFinding>();

        foreach (var element in index.Document.Descendants())
        {
            var local = element.Name.LocalName;
            if (!local.EndsWith(".Style", StringComparison.Ordinal) || local == "Style")
            {
                continue;
            }

            foreach (var style in element.Elements().Where(e => e.Name.LocalName == "Style"))
            {
                if (style.Attribute("BasedOn") == null)
                {
                    var (line, column) = index.PositionOf(style) ?? (1, 1);
                    var ownerType = local.Substring(0, local.Length - ".Style".Length);
                    findings.Add(new RuleFinding(
                        Id, file, line, column, style.Name.LocalName.Length,
                        string.Format(CultureInfo.InvariantCulture, Descriptor.MessageFormat, ownerType)));
                }
            }
        }

        return findings;
    }
}
