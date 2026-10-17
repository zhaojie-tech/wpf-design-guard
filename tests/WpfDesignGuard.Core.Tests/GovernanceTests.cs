using System;
using System.Collections.Generic;
using System.Linq;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;
using Xunit;

namespace WpfDesignGuard.Core.Tests;

public class ExemptionLedgerTests
{
    private static ExemptionEntry Entry(string rule = "DG0009", string file = "Views/X.xaml", int count = 2, string? type = null) =>
        new(rule, file, count, "test reason", type);

    [Fact]
    public void Match_ConsumesInRegistrationOrder()
    {
        var ledger = new ExemptionLedger(new[] { Entry(count: 2) });
        Assert.NotNull(ledger.Match("DG0009", "Views/X.xaml", null));
        Assert.NotNull(ledger.Match("DG0009", "Views/X.xaml", null));
        Assert.Null(ledger.Match("DG0009", "Views/X.xaml", null)); // exhausted: unregistered again
    }

    [Fact]
    public void Match_IsScopedByFile()
    {
        var ledger = new ExemptionLedger(new[] { Entry(file: "Views/X.xaml") });
        Assert.Null(ledger.Match("DG0009", "Views/Y.xaml", null));
    }

    [Fact]
    public void Match_IsScopedByRule()
    {
        var ledger = new ExemptionLedger(new[] { Entry(rule: "DG0009") });
        Assert.Null(ledger.Match("DG0002", "Views/X.xaml", null));
    }

    [Fact]
    public void TypedHit_PrefersTypedEntry_ThenFallsBackToUntyped()
    {
        var ledger = new ExemptionLedger(new[]
        {
            Entry(rule: "DG0017", count: 1, type: "RepeatButton"),
            Entry(rule: "DG0017", count: 1, type: null),
        });

        var typed = ledger.Match("DG0017", "Views/X.xaml", "RepeatButton");
        Assert.Equal("RepeatButton", typed!.Type);

        var second = ledger.Match("DG0017", "Views/X.xaml", "RepeatButton");
        Assert.Null(second!.Type); // typed exhausted, untyped consumed

        Assert.Null(ledger.Match("DG0017", "Views/X.xaml", "RepeatButton")); // all consumed
    }

    [Fact]
    public void TypedEntry_IsNotConsumedByUntypedHit_WhileUntypedExists()
    {
        var ledger = new ExemptionLedger(new[]
        {
            Entry(rule: "DG0017", count: 1, type: "RepeatButton"),
            Entry(rule: "DG0017", count: 1, type: null),
        });

        var hit = ledger.Match("DG0017", "Views/X.xaml", null);
        Assert.Null(hit!.Type); // untyped hit consumed the untyped slot
        // Typed slot was never consumed (0 of 1) and therefore shows up as stale.
        var staleTyped = Assert.Single(ledger.CollectStale(), s2 => s2.Entry.Type == "RepeatButton");
        Assert.Equal(0, staleTyped.ActualCount);
    }

    [Fact]
    public void CollectStale_ReportsNeverConsumedEntries()
    {
        var ledger = new ExemptionLedger(new[]
        {
            Entry(count: 3, file: "Views/Changed.xaml"),
        });
        ledger.Match("DG0009", "Views/Changed.xaml", null);
        ledger.Match("DG0009", "Views/Changed.xaml", null);

        var stale = ledger.CollectStale();
        var entry = Assert.Single(stale);
        Assert.Equal(2, entry.ActualCount);
        Assert.Equal(3, entry.Entry.Count);
    }

    [Fact]
    public void CollectStale_EmptyWhenCountsMatchExactly()
    {
        var ledger = new ExemptionLedger(new[] { Entry(count: 1) });
        ledger.Match("DG0009", "Views/X.xaml", null);
        Assert.Empty(ledger.CollectStale());
    }

    [Fact]
    public void NullEntries_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ExemptionLedger(null!));
    }
}

public class HardZeroResolverTests
{
    private static HardZeroResolver Create() => new(new Dictionary<string, IReadOnlyList<string>>
    {
        ["App/Pos/Register/Print/**"] = new[] { "DG0002", "DG0007" },
    });

    [Fact]
    public void FileInDomain_RuleListed_IsHardZero()
    {
        Assert.True(Create().IsHardZero("App/Pos/Register/Print/Receipt.xaml", "DG0002"));
    }

    [Fact]
    public void FileInDomain_RuleNotListed_IsNotHardZero()
    {
        Assert.False(Create().IsHardZero("App/Pos/Register/Print/Receipt.xaml", "DG0009"));
    }

    [Fact]
    public void FileOutsideDomain_IsNotHardZero()
    {
        Assert.False(Create().IsHardZero("App/Pos/Register/Other.xaml", "DG0002"));
    }

    [Fact]
    public void DomainsFor_ListsCoveringPatterns()
    {
        var domains = Create().DomainsFor("App/Pos/Register/Print/Receipt.xaml");
        var domain = Assert.Single(domains);
        Assert.Equal("App/Pos/Register/Print/**", domain);
        Assert.Empty(Create().DomainsFor("App/Other.xaml"));
    }
}

public class GovernanceEvaluatorTests
{
    private static DesignGuardConfig Config(
        IReadOnlyDictionary<string, int>? baselineCounts = null,
        IReadOnlyList<ExemptionEntry>? exemptions = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? hardZero = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>? baselineFiles = null)
    {
        return new DesignGuardConfig(
            null,
            ScopeConfig.Default,
            new Dictionary<string, RuleConfig>(),
            TokenTable.Default,
            hardZero ?? new Dictionary<string, IReadOnlyList<string>>(),
            exemptions ?? Array.Empty<ExemptionEntry>(),
            baselineCounts != null
                ? new BaselineSnapshot("2026-11-01", baselineCounts,
                    baselineFiles ?? new Dictionary<string, IReadOnlyDictionary<string, int>>())
                : null);
    }

    private static RuleFinding Finding(string rule = "DG0002", string file = "Views/X.xaml", int line = 1) =>
        new(rule, file, line, 5, 3, "msg");

    [Fact]
    public void HardZeroHit_IsAlwaysRed_AndExcludedFromGlobalCounts()
    {
        var config = Config(
            baselineCounts: new Dictionary<string, int> { ["DG0002"] = 5 },
            hardZero: new Dictionary<string, IReadOnlyList<string>>
            {
                ["Views/**"] = new[] { "DG0002" },
            });

        var outcome = GovernanceEvaluator.Evaluate(config,
            new Dictionary<string, IReadOnlyList<RuleFinding>>
            {
                ["Views/X.xaml"] = new[] { Finding() },
            });

        Assert.Equal(FindingDisposition.HardZero, outcome.Findings.Single().Disposition);
        Assert.False(outcome.GlobalCounts.ContainsKey("DG0002"));
        Assert.False(outcome.IsGreen);
    }

    [Fact]
    public void ExemptedHit_DoesNotCountAgainstBaseline()
    {
        var config = Config(
            exemptions: new[] { new ExemptionEntry("DG0002", "Views/X.xaml", 1, "reviewed", null) },
            baselineCounts: new Dictionary<string, int> { ["DG0002"] = 0 });

        var outcome = GovernanceEvaluator.Evaluate(config,
            new Dictionary<string, IReadOnlyList<RuleFinding>>
            {
                ["Views/X.xaml"] = new[] { Finding() },
            });

        Assert.Equal(FindingDisposition.Exempted, outcome.Findings.Single().Disposition);
        Assert.Empty(outcome.RatchetViolations);
        Assert.True(outcome.IsGreen);
    }

    [Fact]
    public void UnexemptedHit_WithinBaselineAndSnapshot_IsGreen()
    {
        // ADR-0007: both layers must hold — global counts AND the per-file snapshot.
        var config = Config(
            baselineCounts: new Dictionary<string, int> { ["DG0002"] = 3 },
            baselineFiles: new Dictionary<string, IReadOnlyDictionary<string, int>>
            {
                ["Views/X.xaml"] = new Dictionary<string, int> { ["DG0002"] = 3 },
            });
        var outcome = GovernanceEvaluator.Evaluate(config,
            new Dictionary<string, IReadOnlyList<RuleFinding>>
            {
                ["Views/X.xaml"] = new[] { Finding(), Finding(line: 2), Finding(line: 3) },
            });

        Assert.Equal(3, outcome.GlobalCounts["DG0002"]);
        Assert.Empty(outcome.RatchetViolations);
        Assert.Empty(outcome.PerFileViolations);
        Assert.True(outcome.IsGreen);
    }

    [Fact]
    public void GlobalWithinBaseline_ButNoPerFileSnapshot_IsRed()
    {
        // The pre-ADR-0007 gap, now closed: a global baseline alone cannot bless a file.
        var config = Config(baselineCounts: new Dictionary<string, int> { ["DG0002"] = 3 });
        var outcome = GovernanceEvaluator.Evaluate(config,
            new Dictionary<string, IReadOnlyList<RuleFinding>>
            {
                ["Views/X.xaml"] = new[] { Finding() },
            });

        Assert.Empty(outcome.RatchetViolations);
        Assert.Single(outcome.PerFileViolations);
        Assert.False(outcome.IsGreen);
    }

    [Fact]
    public void UnexemptedHit_OverBaseline_IsRatchetViolation()
    {
        var config = Config(baselineCounts: new Dictionary<string, int> { ["DG0002"] = 1 });
        var outcome = GovernanceEvaluator.Evaluate(config,
            new Dictionary<string, IReadOnlyList<RuleFinding>>
            {
                ["Views/X.xaml"] = new[] { Finding(), Finding(line: 2) },
            });

        var violation = Assert.Single(outcome.RatchetViolations);
        Assert.Equal("DG0002", violation.RuleId);
        Assert.Equal(2, violation.Actual);
        Assert.Equal(1, violation.Allowed);
        Assert.False(outcome.IsGreen);
    }

    [Fact]
    public void MissingBaselineEntry_TreatedAsZero()
    {
        var config = Config(baselineCounts: new Dictionary<string, int>());
        var outcome = GovernanceEvaluator.Evaluate(config,
            new Dictionary<string, IReadOnlyList<RuleFinding>>
            {
                ["Views/X.xaml"] = new[] { Finding() },
            });

        var violation = Assert.Single(outcome.RatchetViolations);
        Assert.Equal(0, violation.Allowed);
    }

    [Fact]
    public void MissingBaselineSection_TreatedAsZeroBaseline_StrictGate()
    {
        // Without init, verify is strict: any unexempted hit is red. Cold start goes through
        // `designguard init` (ADR-0004); this keeps "no baseline" from silently disabling the gate.
        var config = Config();
        var outcome = GovernanceEvaluator.Evaluate(config,
            new Dictionary<string, IReadOnlyList<RuleFinding>>
            {
                ["Views/X.xaml"] = new[] { Finding() },
            });

        Assert.Single(outcome.RatchetViolations);
    }

    [Fact]
    public void StaleExemption_IsReported_AndBlocksGreen()
    {
        var config = Config(exemptions: new[]
        {
            new ExemptionEntry("DG0009", "Views/Gone.xaml", 2, "moved away", null),
        });
        var outcome = GovernanceEvaluator.Evaluate(config,
            new Dictionary<string, IReadOnlyList<RuleFinding>>
            {
                ["Views/X.xaml"] = Array.Empty<RuleFinding>(),
            });

        var stale = Assert.Single(outcome.StaleExemptions);
        Assert.Equal(0, stale.ActualCount);
        Assert.Equal(2, stale.Entry.Count);
        Assert.False(outcome.IsGreen);
    }

    [Fact]
    public void DisabledRuleFinding_TreatedAsOutOfScope()
    {
        var config = new DesignGuardConfig(
            null,
            ScopeConfig.Default,
            new Dictionary<string, RuleConfig> { ["DG0002"] = new RuleConfig(null, enabled: false) },
            TokenTable.Default,
            new Dictionary<string, IReadOnlyList<string>>(),
            Array.Empty<ExemptionEntry>(),
            null);

        var outcome = GovernanceEvaluator.Evaluate(config,
            new Dictionary<string, IReadOnlyList<RuleFinding>>
            {
                ["Views/X.xaml"] = new[] { Finding() },
            });

        Assert.Equal(FindingDisposition.OutOfScope, outcome.Findings.Single().Disposition);
        Assert.Single(outcome.UncheckedSurface);
        Assert.True(outcome.IsGreen);
    }

    [Fact]
    public void PerFileCounts_AggregatesUnexemptedOnly()
    {
        var config = Config(
            exemptions: new[] { new ExemptionEntry("DG0002", "Views/X.xaml", 1, "one free", null) },
            baselineCounts: new Dictionary<string, int> { ["DG0002"] = 10 });

        var outcome = GovernanceEvaluator.Evaluate(config,
            new Dictionary<string, IReadOnlyList<RuleFinding>>
            {
                ["Views/X.xaml"] = new[] { Finding(), Finding(line: 2), Finding(line: 3) },
                ["Views/Y.xaml"] = new[] { Finding(rule: "DG0007", file: "Views/Y.xaml") },
            });

        var perFile = outcome.PerFileCounts();
        Assert.Equal(2, perFile["Views/X.xaml"]["DG0002"]); // 3 hits - 1 exempted
        Assert.Equal(1, perFile["Views/Y.xaml"]["DG0007"]);
        Assert.False(perFile.ContainsKey("Views/Gone.xaml"));
    }

    [Fact]
    public void MixedDispositions_OnSameFile()
    {
        var config = Config(
            exemptions: new[] { new ExemptionEntry("DG0002", "Views/X.xaml", 1, "ok", null) },
            baselineCounts: new Dictionary<string, int>(),
            hardZero: new Dictionary<string, IReadOnlyList<string>> { ["Views/X.xaml"] = new[] { "DG0007" } });

        var outcome = GovernanceEvaluator.Evaluate(config,
            new Dictionary<string, IReadOnlyList<RuleFinding>>
            {
                ["Views/X.xaml"] = new[]
                {
                    Finding(),                                   // exempted
                    Finding(rule: "DG0007", line: 10),           // hard zero
                    Finding(rule: "DG0007", line: 20),           // hard zero (second)
                },
            });

        Assert.Collection(
            outcome.Findings,
            f => Assert.Equal(FindingDisposition.Exempted, f.Disposition),
            f => Assert.Equal(FindingDisposition.HardZero, f.Disposition),
            f => Assert.Equal(FindingDisposition.HardZero, f.Disposition));
        Assert.Empty(outcome.RatchetViolations); // hard-zero hits never enter the global layer
    }
}

/// <summary>ADR-0007: per-file ratchet closes the "violation moved to another file" hole.</summary>
public class PerFileRatchetTests
{
    private static DesignGuardConfig Config(IReadOnlyDictionary<string, int> counts,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> files) =>
        new(null, ScopeConfig.Default, new Dictionary<string, RuleConfig>(), TokenTable.Default,
            new Dictionary<string, IReadOnlyList<string>>(), Array.Empty<ExemptionEntry>(),
            new BaselineSnapshot("2026-10-17", counts, files));

    private static RuleFinding F(string file) => new("DG0007", file, 1, 1, 1, "m");

    [Fact]
    public void WithinSnapshot_NoPerFileViolation()
    {
        var config = Config(
            new Dictionary<string, int> { ["DG0007"] = 1 },
            new Dictionary<string, IReadOnlyDictionary<string, int>>
            {
                ["A.xaml"] = new Dictionary<string, int> { ["DG0007"] = 1 },
            });

        var outcome = GovernanceEvaluator.Evaluate(config,
            new Dictionary<string, IReadOnlyList<RuleFinding>> { ["A.xaml"] = new[] { F("A.xaml") } });

        Assert.Empty(outcome.PerFileViolations);
        Assert.True(outcome.IsGreen);
    }

    [Fact]
    public void ViolationMovedToNewFile_GlobalCountUnchanged_IsPerFileViolation()
    {
        // Baseline: one violation in A. Current: the same single violation now lives in B.
        var config = Config(
            new Dictionary<string, int> { ["DG0007"] = 1 },
            new Dictionary<string, IReadOnlyDictionary<string, int>>
            {
                ["A.xaml"] = new Dictionary<string, int> { ["DG0007"] = 1 },
            });

        var outcome = GovernanceEvaluator.Evaluate(config,
            new Dictionary<string, IReadOnlyList<RuleFinding>> { ["B.xaml"] = new[] { F("B.xaml") } });

        Assert.Empty(outcome.RatchetViolations); // global count still 1
        var violation = Assert.Single(outcome.PerFileViolations);
        Assert.Equal("B.xaml", violation.File);
        Assert.Equal(1, violation.Actual);
        Assert.Equal(0, violation.Allowed);
        Assert.False(outcome.IsGreen);
    }

    [Fact]
    public void FileOverItsSnapshot_IsPerFileViolation()
    {
        var config = Config(
            new Dictionary<string, int> { ["DG0007"] = 5 },
            new Dictionary<string, IReadOnlyDictionary<string, int>>
            {
                ["A.xaml"] = new Dictionary<string, int> { ["DG0007"] = 1 },
            });

        var outcome = GovernanceEvaluator.Evaluate(config,
            new Dictionary<string, IReadOnlyList<RuleFinding>>
            {
                ["A.xaml"] = new[] { F("A.xaml"), F("A.xaml") },
            });

        var violation = Assert.Single(outcome.PerFileViolations);
        Assert.Equal(2, violation.Actual);
        Assert.Equal(1, violation.Allowed);
    }

    [Fact]
    public void HardZeroAndExemptedHits_DoNotEnterPerFileCounts()
    {
        var config = new DesignGuardConfig(
            null, ScopeConfig.Default,
            new Dictionary<string, RuleConfig> { ["DG0007"] = new RuleConfig(null, true) { } },
            TokenTable.Default,
            new Dictionary<string, IReadOnlyList<string>> { ["A.xaml"] = new[] { "DG0007" } },
            new[] { new ExemptionEntry("DG0009", "A.xaml", 1, "ok", null) },
            new BaselineSnapshot("2026-10-17", new Dictionary<string, int>(),
                new Dictionary<string, IReadOnlyDictionary<string, int>>()));

        var outcome = GovernanceEvaluator.Evaluate(config,
            new Dictionary<string, IReadOnlyList<RuleFinding>>
            {
                ["A.xaml"] = new[] { F("A.xaml"), new RuleFinding("DG0009", "A.xaml", 1, 1, 1, "x") },
            });

        Assert.Empty(outcome.PerFileViolations); // hard-zero + exempted only
    }

    [Fact]
    public void MissingBaseline_EverythingIsOverSnapshot()
    {
        var config = new DesignGuardConfig(
            null, ScopeConfig.Default, new Dictionary<string, RuleConfig>(), TokenTable.Default,
            new Dictionary<string, IReadOnlyList<string>>(), Array.Empty<ExemptionEntry>(), null);

        var outcome = GovernanceEvaluator.Evaluate(config,
            new Dictionary<string, IReadOnlyList<RuleFinding>> { ["A.xaml"] = new[] { F("A.xaml") } });

        Assert.Single(outcome.PerFileViolations);
        Assert.False(outcome.IsGreen);
    }
}
