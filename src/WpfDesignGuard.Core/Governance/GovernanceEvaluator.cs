using System;
using System.Collections.Generic;
using System.Linq;
using WpfDesignGuard.Core.Configuration;

namespace WpfDesignGuard.Core.Governance;

/// <summary>Result of the dual-layer gate evaluation .</summary>
public sealed class VerificationOutcome
{
    public List<DispositionedFinding> Findings { get; } = new();

    public List<StaleExemption> StaleExemptions { get; } = new();

    public List<RatchetViolation> RatchetViolations { get; } = new();

    /// <summary>File-level breaches against baseline.files snapshots (ADR-0007).</summary>
    public List<PerFileRatchetViolation> PerFileViolations { get; } = new();

    /// <summary>Unexempted per-rule global counters (hard-zero hits excluded,  closure).</summary>
    public Dictionary<string, int> GlobalCounts { get; } = new(StringComparer.Ordinal);

    /// <summary>Findings outside the rule scope / skipped rules: visible, never silent .</summary>
    public List<RuleFinding> UncheckedSurface { get; } = new();

    /// <summary>
    /// True when no governance layer is red. Unexempted hits within the ratchet baseline are
    /// tolerated (that is what the baseline is for); hard-zero hits, ratchet breaches and
    /// stale exemptions are red.
    /// </summary>
    public bool IsGreen =>
        Findings.All(f => f.Disposition != FindingDisposition.HardZero) &&
        StaleExemptions.Count == 0 &&
        RatchetViolations.Count == 0 &&
        PerFileViolations.Count == 0;

    /// <summary>Per-file unexempted counts — the cold-start downgrade data source for the generator.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> PerFileCounts()
    {
        var map = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var f in Findings)
        {
            if (f.Disposition != FindingDisposition.Unexempted)
            {
                continue;
            }

            if (!map.TryGetValue(f.Finding.File, out var perRule))
            {
                perRule = new Dictionary<string, int>(StringComparer.Ordinal);
                map[f.Finding.File] = perRule;
            }

            perRule[f.Finding.RuleId] = (perRule.TryGetValue(f.Finding.RuleId, out var n) ? n : 0) + 1;
        }

        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<string, int>)kv.Value, StringComparer.Ordinal);
    }
}

/// <summary>
/// The single evaluation implementation shared verbatim by the CLI and (via the generator)
/// the compiler surface — the "zero drift" guarantee.
/// </summary>
public static class GovernanceEvaluator
{
    /// <summary>
    /// Dispositions findings and evaluates both governance layers.
    /// <paramref name="findings"/> must already be scope-root-relative per file.
    /// </summary>
    public static VerificationOutcome Evaluate(
        DesignGuardConfig config,
        IEnumerable<KeyValuePair<string, IReadOnlyList<RuleFinding>>> perFileFindings)
    {
        var outcome = new VerificationOutcome();
        var ledger = new ExemptionLedger(config.Exemptions);
        var hardZero = new HardZeroResolver(config.HardZero);

        foreach (var kv in perFileFindings)
        {
            var file = kv.Key;
            foreach (var finding in kv.Value)
            {
                if (!config.IsEnabled(finding.RuleId))
                {
                    // Disabled rules produce no findings upstream; a finding here means the
                    // caller fed engine output from a different config — treat as unchecked.
                    outcome.UncheckedSurface.Add(finding);
                    outcome.Findings.Add(new DispositionedFinding(finding, FindingDisposition.OutOfScope, null));
                    continue;
                }

                FindingDisposition disposition;
                ExemptionEntry? exemption = null;

                if (hardZero.IsHardZero(file, finding.RuleId))
                {
                    disposition = FindingDisposition.HardZero;
                }
                else
                {
                    exemption = ledger.Match(finding.RuleId, file, finding.Type);
                    disposition = exemption != null
                        ? FindingDisposition.Exempted
                        : FindingDisposition.Unexempted;

                    if (exemption == null)
                    {
                        outcome.GlobalCounts[finding.RuleId] =
                            (outcome.GlobalCounts.TryGetValue(finding.RuleId, out var n) ? n : 0) + 1;
                    }
                }

                outcome.Findings.Add(new DispositionedFinding(finding, disposition, exemption));
            }
        }

        outcome.StaleExemptions.AddRange(ledger.CollectStale());

        // Missing baseline section == zero baseline (strict). Cold start goes through
        // `designguard init`, which writes the baseline with per-file snapshots (ADR-0004).
        var counts = config.Baseline?.Counts ?? (IReadOnlyDictionary<string, int>)new Dictionary<string, int>();
        foreach (var kv in outcome.GlobalCounts)
        {
            var allowed = counts.TryGetValue(kv.Key, out var a) ? a : 0;
            if (kv.Value > allowed)
            {
                outcome.RatchetViolations.Add(new RatchetViolation(kv.Key, kv.Value, allowed));
            }
        }

        // File-level ratchet (ADR-0007): the same snapshot judgement the generator uses.
        // A violation moved from an old file to a new one keeps the global count intact
        // but breaks the per-file snapshot, so it is caught here.
        var perFile = outcome.PerFileCounts();
        foreach (var file in perFile.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            IReadOnlyDictionary<string, int>? snapshot = null;
            config.Baseline?.Files.TryGetValue(file.Key, out snapshot);
            foreach (var rule in file.Value.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var allowed = snapshot != null && snapshot.TryGetValue(rule.Key, out var a) ? a : 0;
                if (rule.Value > allowed)
                {
                    outcome.PerFileViolations.Add(new PerFileRatchetViolation(file.Key, rule.Key, rule.Value, allowed));
                }
            }
        }

        return outcome;
    }
}
