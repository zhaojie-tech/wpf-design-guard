using System;
using System.Collections.Generic;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Core.Configuration;

/// <summary>
/// Token table. Values come from designguard.json only — v0.1 has no
/// Tokens runtime package. Defaults are the production-calibrated ladder; the whole
/// table may be replaced. Load-time floor validation errors are configuration errors
/// (CLI exit 1), never diagnostics.
/// </summary>
public sealed class TokenTable
{
    public const int MinBodyFontSize = 16;
    public const int MinHitTargetFloor = 44;

    public TokenTable(
        IReadOnlyList<double> spacingLadder,
        IReadOnlyDictionary<string, double> fontSizes,
        double hitTargetFloor,
        IReadOnlyList<string> uniformSpacingKeys,
        IReadOnlyDictionary<string, double> lineHeights,
        IReadOnlyList<string> clickableTypes,
        IReadOnlyList<string> inlineInputTypes,
        string? styleLibrary)
    {
        SpacingLadder = spacingLadder ?? throw new ArgumentNullException(nameof(spacingLadder));
        FontSizes = fontSizes ?? throw new ArgumentNullException(nameof(fontSizes));
        HitTargetFloor = hitTargetFloor;
        UniformSpacingKeys = uniformSpacingKeys ?? throw new ArgumentNullException(nameof(uniformSpacingKeys));
        LineHeights = lineHeights ?? throw new ArgumentNullException(nameof(lineHeights));
        ClickableTypes = clickableTypes ?? throw new ArgumentNullException(nameof(clickableTypes));
        InlineInputTypes = inlineInputTypes ?? throw new ArgumentNullException(nameof(inlineInputTypes));
        StyleLibrary = styleLibrary;
    }

    public static TokenTable Default { get; } = new(
        new[] { 0d, 2d, 4d, 8d, 12d, 16d, 24d, 32d },
        new Dictionary<string, double>
        {
            ["Caption"] = 14,
            ["Body"] = 16,
            ["Subtitle"] = 18,
            ["Title"] = 20,
            ["Display"] = 28,
        },
        48,
        new[] { "Spacing.*" },
        new Dictionary<string, double>
        {
            ["Caption"] = 20,
            ["Body"] = 24,
            ["Subtitle"] = 24,
            ["Title"] = 28,
        },
        new[]
        {
            "Button", "ToggleButton", "TextBox", "ComboBox", "ComboBoxItem", "CheckBox",
            "RadioButton", "TabItem", "ListBoxItem", "TreeViewItem", "MenuItem",
            "DataGridRow", "RepeatButton",
        },
        new[] { "TextBox", "ComboBox", "Button", "DatePicker" },
        null);

    public IReadOnlyList<double> SpacingLadder { get; }

    public IReadOnlyDictionary<string, double> FontSizes { get; }

    public double HitTargetFloor { get; }

    /// <summary>Glob key families treated as "uniform spacing tokens" (DG0004 judgement data).</summary>
    public IReadOnlyList<string> UniformSpacingKeys { get; }

    /// <summary>Font-size key to line-height mapping (DG0007 fix data source).</summary>
    public IReadOnlyDictionary<string, double> LineHeights { get; }

    /// <summary>Clickable type list generalized from the 13 production types (DG0008/0009/0017).</summary>
    public IReadOnlyList<string> ClickableTypes { get; }

    /// <summary>Types treated as "inline input" elements (DG0004/0016).</summary>
    public IReadOnlyList<string> InlineInputTypes { get; }

    /// <summary>Style library definition file (glob, DG0017).</summary>
    public string? StyleLibrary { get; }
}

/// <summary>Per-rule override: severity and/or enabled flag. Unlisted rules = enabled with catalog default.</summary>
public sealed class RuleConfig
{
    public RuleConfig(RuleSeverity? severity, bool enabled)
    {
        Severity = severity;
        Enabled = enabled;
    }

    public RuleSeverity? Severity { get; }

    public bool Enabled { get; }
}

/// <summary>Per-rule scan-surface override.</summary>
public sealed class RuleScopeOverride
{
    public RuleScopeOverride(IReadOnlyList<string>? include, IReadOnlyList<string>? exclude)
    {
        Include = include;
        Exclude = exclude;
    }

    public IReadOnlyList<string>? Include { get; }

    public IReadOnlyList<string>? Exclude { get; }
}

/// <summary>Scan scope: the first-class citizen of the configuration.</summary>
public sealed class ScopeConfig
{
    public const string DefaultRoot = ".";

    public ScopeConfig(
        string root,
        IReadOnlyList<string> include,
        IReadOnlyList<string> exclude,
        IReadOnlyDictionary<string, RuleScopeOverride> perRule)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        Include = include ?? throw new ArgumentNullException(nameof(include));
        Exclude = exclude ?? throw new ArgumentNullException(nameof(exclude));
        PerRule = perRule ?? throw new ArgumentNullException(nameof(perRule));
    }

    public string Root { get; }

    public IReadOnlyList<string> Include { get; }

    public IReadOnlyList<string> Exclude { get; }

    public IReadOnlyDictionary<string, RuleScopeOverride> PerRule { get; }

    public static ScopeConfig Default { get; } = new(
        DefaultRoot,
        new[] { "**/*.xaml" },
        Array.Empty<string>(),
        new Dictionary<string, RuleScopeOverride>());
}

/// <summary>
/// One exemption entry. Exemptions are per-occurrence (file + count + reason); directory-level
/// exemptions are forbidden by design. Optional <see cref="Type"/> enables type-scoped exemption
/// for coverage gates like DG0017.
/// </summary>
public sealed class ExemptionEntry
{
    public ExemptionEntry(string rule, string file, int count, string reason, string? type)
    {
        Rule = rule ?? throw new ArgumentNullException(nameof(rule));
        File = file ?? throw new ArgumentNullException(nameof(file));
        Count = count;
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        Type = type;
    }

    public string Rule { get; }

    /// <summary>File path relative to <see cref="ScopeConfig.Root"/>.</summary>
    public string File { get; }

    public int Count { get; }

    public string Reason { get; }

    public string? Type { get; }
}

/// <summary>Global ratchet baseline. Only the global layer; hard-zero domains are never relaxed via baseline.</summary>
public sealed class BaselineSnapshot
{
    public BaselineSnapshot(
        string? date,
        IReadOnlyDictionary<string, int> counts,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> files)
    {
        Date = date;
        Counts = counts ?? throw new ArgumentNullException(nameof(counts));
        Files = files ?? throw new ArgumentNullException(nameof(files));
    }

    public string? Date { get; }

    /// <summary>Per-rule global counters (rules absent = 0).</summary>
    public IReadOnlyDictionary<string, int> Counts { get; }

    /// <summary>Per-file snapshots (written by init) — data source of the cold-start downgrade rule.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Files { get; }
}
