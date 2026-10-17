using System;
using System.Collections.Generic;
using System.Linq;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;

namespace WpfDesignGuard.Core.Governance;

/// <summary>
/// Cold-start downgrade policy: a file whose per-rule hit count
/// is within its baseline per-file snapshot compiles with Warnings (存量共存), while
/// files outside the snapshot (allowed = 0) and files exceeding the snapshot go red
/// immediately. Hard-zero hits are never downgraded (domain stock is red by design);
/// exempted hits never reach this policy.
/// </summary>
public static class ColdStartPolicy
{
    /// <summary>
    /// Splits the findings of one file into (downgraded, escalated) pairs against the
    /// baseline snapshot. Preserves evaluation order; consumes each rule's snapshot
    /// allowance in order (the first N hits within the snapshot are downgraded).
    /// </summary>
    public static (IReadOnlyList<RuleFinding> Downgraded, IReadOnlyList<RuleFinding> Escalated) Split(
        DesignGuardConfig config,
        HardZeroResolver hardZero,
        string file,
        IEnumerable<RuleFinding> unexemptedFindings)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (hardZero is null)
        {
            throw new ArgumentNullException(nameof(hardZero));
        }

        var downgraded = new List<RuleFinding>();
        var escalated = new List<RuleFinding>();

        var perFile = config.Baseline?.Files;
        Dictionary<string, int>? allowances = null;
        if (perFile != null && perFile.TryGetValue(file, out var snapshot))
        {
            allowances = snapshot.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        }

        var consumed = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var finding in unexemptedFindings)
        {
            if (hardZero.IsHardZero(file, finding.RuleId))
            {
                escalated.Add(finding);
                continue;
            }

            var used = consumed.TryGetValue(finding.RuleId, out var u) ? u : 0;
            var allowed = allowances != null && allowances.TryGetValue(finding.RuleId, out var a) ? a : 0;
            consumed[finding.RuleId] = used + 1;

            if (used < allowed)
            {
                downgraded.Add(finding);
            }
            else
            {
                escalated.Add(finding);
            }
        }

        return (downgraded, escalated);
    }
}
