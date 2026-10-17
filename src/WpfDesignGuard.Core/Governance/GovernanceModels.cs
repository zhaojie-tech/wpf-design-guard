using System;
using System.Collections.Generic;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Globbing;

namespace WpfDesignGuard.Core.Governance;

/// <summary>
/// The effective scan surface for one rule: perRule.include replaces the global include;
/// global exclude and perRule.exclude both always apply.
/// </summary>
public sealed class EffectiveScope
{
    private readonly IReadOnlyList<GlobMatcher> _include;
    private readonly IReadOnlyList<GlobMatcher> _exclude;

    private EffectiveScope(IReadOnlyList<GlobMatcher> include, IReadOnlyList<GlobMatcher> exclude)
    {
        _include = include;
        _exclude = exclude;
    }

    public static EffectiveScope Resolve(ScopeConfig scope, string ruleId)
    {
        IReadOnlyList<string> include = scope.Include;
        var exclude = new List<string>(scope.Exclude);

        if (scope.PerRule.TryGetValue(ruleId, out var overrideScope))
        {
            if (overrideScope.Include is { Count: > 0 })
            {
                include = overrideScope.Include;
            }

            exclude.AddRange(overrideScope.Exclude ?? Array.Empty<string>());
        }

        var includeMatchers = new List<GlobMatcher>(include.Count);
        foreach (var p in include)
        {
            includeMatchers.Add(new GlobMatcher(p));
        }

        var excludeMatchers = new List<GlobMatcher>(exclude.Count);
        foreach (var p in exclude)
        {
            excludeMatchers.Add(new GlobMatcher(p));
        }

        return new EffectiveScope(includeMatchers, excludeMatchers);
    }

    /// <summary>True when the scope-root-relative path is inside this rule's surface.</summary>
    public bool Matches(string relativePath) => GlobMatcher.MatchesAny(_include, _exclude, relativePath);
}

/// <summary>One rule hit on one file (engine output, before governance disposition).</summary>
public sealed class RuleFinding
{
    public RuleFinding(string ruleId, string file, int line, int column, int length, string message, string? type = null)
    {
        RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
        File = file ?? throw new ArgumentNullException(nameof(file));
        Line = line;
        Column = column;
        Length = length;
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Type = type;
    }

    public string RuleId { get; }

    /// <summary>Path relative to scope root (normalized to '/').</summary>
    public string File { get; }

    /// <summary>1-based line.</summary>
    public int Line { get; }

    /// <summary>1-based column.</summary>
    public int Column { get; }

    public int Length { get; }

    public string Message { get; }

    /// <summary>Optional type subject (coverage gates) — enables type-scoped exemptions.</summary>
    public string? Type { get; }
}

/// <summary>How a finding was disposed by the governance layer.</summary>
public enum FindingDisposition
{
    /// <summary>Inside a hard-zero domain: always red.</summary>
    HardZero,

    /// <summary>Registered exemption consumed this occurrence.</summary>
    Exempted,

    /// <summary>No exemption registered: counts against the global ratchet (and is red if over baseline).</summary>
    Unexempted,

    /// <summary>Outside the rule's scan surface: not counted in any layer, surfaced as unchecked.</summary>
    OutOfScope,
}

public sealed class DispositionedFinding
{
    public DispositionedFinding(RuleFinding finding, FindingDisposition disposition, ExemptionEntry? exemption)
    {
        Finding = finding ?? throw new ArgumentNullException(nameof(finding));
        Disposition = disposition;
        Exemption = exemption;
    }

    public RuleFinding Finding { get; }

    public FindingDisposition Disposition { get; }

    public ExemptionEntry? Exemption { get; }
}

/// <summary>An exemption whose registered count no longer matches actual hits (stale = red, ).</summary>
public sealed class StaleExemption
{
    public StaleExemption(ExemptionEntry entry, int actualCount)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        ActualCount = actualCount;
    }

    public ExemptionEntry Entry { get; }

    public int ActualCount { get; }
}

/// <summary>
/// File-level ratchet breach: a file's unexempted count exceeds its baseline snapshot
/// (files absent from the snapshot allow zero). Closes the "violation moved to another
/// file, total unchanged" hole (ADR-0007).
/// </summary>
public sealed class PerFileRatchetViolation
{
    public PerFileRatchetViolation(string file, string ruleId, int actual, int allowed)
    {
        File = file ?? throw new ArgumentNullException(nameof(file));
        RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
        Actual = actual;
        Allowed = allowed;
    }

    public string File { get; }

    public string RuleId { get; }

    public int Actual { get; }

    public int Allowed { get; }
}

/// <summary>Global ratchet breach: unexempted count exceeds the baseline.</summary>
public sealed class RatchetViolation
{
    public RatchetViolation(string ruleId, int actual, int allowed)
    {
        RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
        Actual = actual;
        Allowed = allowed;
    }

    public string RuleId { get; }

    public int Actual { get; }

    public int Allowed { get; }
}
