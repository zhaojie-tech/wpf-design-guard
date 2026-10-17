using System.Text;
using WpfDesignGuard.Core;
using WpfDesignGuard.Core.Rules;

var root = args.Length > 0 ? args[0] : ".";
var rulesDir = Path.Combine(root, "docs", "rules");
Directory.CreateDirectory(rulesDir);

foreach (var rule in RuleCatalog.All)
{
    var sb = new StringBuilder();
    void Line(string s) => sb.Append(s).Append('\n');

    Line($"# {rule.Id} — {rule.Title}");
    Line("");
    Line("| | |");
    Line("|---|---|");
    Line($"| Default severity | **{rule.DefaultSeverity}** |");
    Line($"| Determinism | {rule.Determinism} |");
    Line($"| Detection surface | {SurfaceNames(rule.Surfaces)} |");
    Line($"| Fix support | {FixNames(rule.FixSupport)} |");
    Line($"| First shipped | v{rule.FirstShippedIn} |");
    Line("");
    Line("## Message");
    Line("");
    Line("```text");
    Line(rule.MessageFormat);
    Line("```");
    Line("");
    var notes = RuleNotes.For(rule.Id);
    if (notes.Length > 0)
    {
        Line("## Value judgement (取值口径)");
        Line("");
        foreach (var n in notes)
        {
            Line($"- {n}");
        }
        Line("");
    }

    var falsePositives = RuleNotes.FalsePositivesFor(rule.Id);
    if (falsePositives.Length > 0)
    {
        Line("## When a hit may NOT be a bug");
        Line("");
        foreach (var n in falsePositives)
        {
            Line($"- {n}");
        }
        Line("");
    }

    File.WriteAllText(Path.Combine(rulesDir, $"{rule.Id}.md"), sb.ToString());
}

Console.WriteLine($"Generated {RuleCatalog.All.Count} rule pages into {Path.GetFullPath(rulesDir)}");

static string SurfaceNames(RuleSurface s)
{
    var parts = new List<string>();
    if (s.HasFlag(RuleSurface.Generator)) parts.Add("generator (AdditionalFiles XAML pipeline)");
    if (s.HasFlag(RuleSurface.CSharpAnalyzer)) parts.Add("C# analyzer");
    if (s.HasFlag(RuleSurface.Cli)) parts.Add("CLI (whole-repo view)");
    return string.Join(" + ", parts);
}

static string FixNames(RuleFixSupport f)
{
    var parts = new List<string>();
    if (f.HasFlag(RuleFixSupport.CSharpCodeFix)) parts.Add("C# code fix (placeholder token until v0.2)");
    if (f.HasFlag(RuleFixSupport.CliFix)) parts.Add("`designguard fix` (span-level)");
    return parts.Count == 0 ? "none (report only)" : string.Join(" + ", parts);
}

public static class RuleNotes
{
    public static string[] For(string id) => id switch
    {
        "DG0001" => new[]
        {
            "XAML: scans attribute, property-element and Setter forms; attached-property form (TextBlock.FontSize) included.",
            "C#: numeric literals assigned to a member named FontSize.",
            "Markup extensions ({DynamicResource Font.Body}, {Binding …}, {} escapes) are skipped.",
        },
        "DG0002" => new[]
        {
            "Reports one finding per offending component, span on that component.",
            "Single-component literals belong to DG0003; comments are stripped before evaluation.",
        },
        "DG0003" => new[]
        {
            "The directional rewrite (\"N,N\") produced by the fixer leaves the DG0002 surface clean when N is on the ladder.",
        },
        "DG0004" => new[]
        {
            "\"Inline position\" = parent is a horizontal stack panel.",
            "Heuristic rule: severity defaults to Warning (diagnosis-fatigue guard, R4).",
        },
        "DG0005" => new[]
        {
            "XAML: any attribute value or property-element/Setter.Value text matching #RGB/#RRGGBB/#AARRGGBB hex literals.",
            "C#: Colors.* member references and Color.FromRgb/FromArgb/FromScRgb invocations.",
        },
        "DG0006" => new[]
        {
            "Covers CornerRadius, MinWidth and MaxWidth literals across attribute, property-element and Setter forms; the C# surface covers assignments in code-behind.",
            "No automatic fix: the token table carries no width/radius ladder, so the replacement token is a human decision. Detection only.",
        },
        "DG0007" => new[]
        {
            "TextWrapping=\"Wrap\" only — WrapWithOverflow is intentionally out of scope (production gate judged on \"Wrap\"; recorded as a delta candidate).",
            "Fix infers LineHeight from tokens.lineHeights by token key or font-size value; without a mappable FontSize the fix reports the occurrence as not auto-fixable.",
        },
        "DG0008" => new[]
        {
            "Only instance-explicit values are checked — style-level floors are DG0017's coverage gate (documented blind spot).",
        },
        "DG0010" => new[]
        {
            "Only the inline <X.Style> property-element form is a call site; style-library definitions are data, not violations.",
        },
        "DG0012" => new[]
        {
            "Symbol route: xmlns URI → clr-namespace via XmlnsDefinition attributes of referenced assemblies; clr-namespace: prefixes map directly.",
            "Trigger/DataTrigger/MultiTrigger setters inherit the enclosing Style or ControlTemplate TargetType.",
            "Unknown types and unmapped namespaces are unchecked surface — never false positives (R2).",
        },
        "DG0013" => new[]
        {
            "No automatic fix by design — rewriting user copy is not a mechanical operation.",
        },
        "DG0014" => new[]
        {
            "CLI-only surface: implicit style chains resolve across App.xaml MergedDictionaries/pack URIs; multi-dictionary name clashes resolve by WPF load order (later wins).",
        },
        "DG0015" => new[]
        {
            "Explicit non-OneWay modes are reported too (unsafe default TwoWay against read-only sources).",
            "OneWay detection is case-insensitive; Mode= appears anywhere in the binding payload.",
        },
        "DG0016" => new[]
        {
            "The combination is the anti-pattern; the MaxWidth value form (literal or token) does not matter.",
            "Input types come from tokens.inlineInputTypes (configurable).",
        },
        "DG0017" => new[]
        {
            "Coverage gate: every clickable type seen in the project must have a MinHeight/MinWidth ≥ floor setter at the style library definition site — new types fail until extended.",
            "Type-scoped exemptions (ledger entry with \"type\") exist for template-internal parts.",
        },
        _ => Array.Empty<string>(),
    };

    public static string[] FalsePositivesFor(string id) => id switch
    {
        "DG0010" => new[]
        {
            "Repos without a library style: every inline style hits, but there is no library style to silently replace — still worth BasedOn for future-proofing, yet not a live defect.",
        },
        "DG0012" => new[]
        {
            "Types that cannot be resolved (missing reference, unmapped xmlns) are unchecked, never reported — a hit means the type WAS resolved and the property is genuinely absent.",
        },
        "DG0015" => new[]
        {
            "Explicit Mode=TwoWay is reported, but it only crashes when the source property is read-only — a TwoWay binding against a writable property is legitimate and can be exempted.",
        },
        "DG0016" => new[]
        {
            "Width-limited + left-aligned toolbars and tag clouds are a common intentional layout; the rule flags the combination because the production incident mistook MaxWidth for a width guarantee — exempt deliberate cases.",
        },
        "DG0006" => new[]
        {
            "Covers CornerRadius, MinWidth and MaxWidth literals across attribute, property-element and Setter forms; the C# surface covers assignments in code-behind.",
            "No automatic fix: the token table carries no width/radius ladder, so the replacement token is a human decision. Detection only.",
        },
        "DG0007" => new[]
        {
            "TextWrapping=WrapWithOverflow is intentionally out of scope (production gate judged on Wrap only); hits are a consistency contract, not a crash risk.",
        },
        "DG0001" => new[]
        {
            "Font sizes on non-UI types named FontSize are reported too (syntax-level judgement) — rare, exempt if legitimate.",
        },
        "DG0005" => new[]
        {
            "Test fixtures and color swatch galleries legitimately enumerate raw hex values — scope them out via perRule.",
        },
        _ => Array.Empty<string>(),
    };
}
