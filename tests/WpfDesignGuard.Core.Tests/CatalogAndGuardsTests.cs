using System;
using System.Collections.Generic;
using System.Linq;
using WpfDesignGuard.Core;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;
using Xunit;

namespace WpfDesignGuard.Core.Tests;

public class RuleCatalogTests
{
    [Fact]
    public void Catalog_ContainsAllSeventeenAllocatedIds()
    {
        var ids = RuleCatalog.All.Select(r => r.Id).OrderBy(x => x).ToArray();
        var expected = Enumerable.Range(1, 17).Select(i => $"DG{i:0000}").OrderBy(x => x).ToArray();
        Assert.Equal(expected, ids);
    }

    [Fact]
    public void TryGet_KnownAndUnknown_AndCaseSensitivity()
    {
        Assert.NotNull(RuleCatalog.TryGet("DG0002"));
        Assert.Null(RuleCatalog.TryGet("DG9999"));
        Assert.Null(RuleCatalog.TryGet("dg0002")); // IDs are case-sensitive public API
    }

    [Fact]
    public void M1ShippedRules_AreFlagged()
    {
        // M1 pure-text rules: DG0002 (spacing ladder), DG0007 (wrapped TextBlock line height).
        Assert.Equal("0.1.0", RuleCatalog.TryGet("DG0002")!.FirstShippedIn);
        Assert.Equal("0.1.0", RuleCatalog.TryGet("DG0007")!.FirstShippedIn);
        Assert.Equal(RuleSurface.Generator, RuleCatalog.TryGet("DG0002")!.Surfaces);
        Assert.Equal(RuleSurface.Cli, RuleCatalog.TryGet("DG0014")!.Surfaces);
    }

    [Theory]
    [InlineData("0.1.0", "0.1.0", true)]
    [InlineData("0.1.0", "0.1.1", true)]
    [InlineData("0.2.0", "0.1.0", false)]
    [InlineData("0.2.0", "0.1.99", false)]
    [InlineData("1.0.0", "0.9.9", false)]
    [InlineData("0.10.0", "0.9.0", false)] // numeric, not lexicographic comparison
    public void IsImplementedAt_ComparesSemVerNumerically(string shipped, string current, bool expected)
    {
        var rule = new RuleDescriptor("DG9001", "t", "m", RuleSeverity.Error,
            RuleDeterminism.Deterministic, RuleSurface.Generator, RuleFixSupport.None, shipped);
        Assert.Equal(expected, RuleCatalog.IsImplementedAt(rule, current));
    }

    [Fact]
    public void DescriptorProperties_AreExposed()
    {
        var d = RuleCatalog.TryGet("DG0004")!;
        Assert.Equal(RuleDeterminism.Heuristic, d.Determinism);
        Assert.Equal(RuleSeverity.Warning, d.DefaultSeverity);
        Assert.Equal(RuleFixSupport.None, d.FixSupport);
    }
}

public class ConstructorNullGuardsTests
{
    [Fact]
    public void TokenTable_NullArguments_Throw()
    {
        var ok = new[] { 0d };
        var map = new Dictionary<string, double>();
        var strs = Array.Empty<string>();
        Assert.Throws<ArgumentNullException>(() => new TokenTable(null!, map, 48, strs, map, strs, strs, null));
        Assert.Throws<ArgumentNullException>(() => new TokenTable(ok, null!, 48, strs, map, strs, strs, null));
        Assert.Throws<ArgumentNullException>(() => new TokenTable(ok, map, 48, null!, map, strs, strs, null));
        Assert.Throws<ArgumentNullException>(() => new TokenTable(ok, map, 48, strs, null!, strs, strs, null));
        Assert.Throws<ArgumentNullException>(() => new TokenTable(ok, map, 48, strs, map, null!, strs, null));
        Assert.Throws<ArgumentNullException>(() => new TokenTable(ok, map, 48, strs, map, strs, null!, null));
    }

    [Fact]
    public void ScopeConfig_NullArguments_Throw()
    {
        var strs = Array.Empty<string>();
        var map = new Dictionary<string, RuleScopeOverride>();
        Assert.Throws<ArgumentNullException>(() => new ScopeConfig(null!, strs, strs, map));
        Assert.Throws<ArgumentNullException>(() => new ScopeConfig(".", null!, strs, map));
        Assert.Throws<ArgumentNullException>(() => new ScopeConfig(".", strs, null!, map));
        Assert.Throws<ArgumentNullException>(() => new ScopeConfig(".", strs, strs, null!));
    }

    [Fact]
    public void ExemptionEntry_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new ExemptionEntry(null!, "f", 1, "r", null));
        Assert.Throws<ArgumentNullException>(() => new ExemptionEntry("DG0002", null!, 1, "r", null));
        Assert.Throws<ArgumentNullException>(() => new ExemptionEntry("DG0002", "f", 1, null!, null));
    }

    [Fact]
    public void BaselineSnapshot_NullArguments_Throw()
    {
        var map = new Dictionary<string, int>();
        Assert.Throws<ArgumentNullException>(() => new BaselineSnapshot(null, null!, new Dictionary<string, IReadOnlyDictionary<string, int>>()));
        Assert.Throws<ArgumentNullException>(() => new BaselineSnapshot(null, map, null!));
    }

    [Fact]
    public void DesignGuardConfig_NullArguments_Throw()
    {
        var scope = ScopeConfig.Default;
        var rules = new Dictionary<string, RuleConfig>();
        var tokens = TokenTable.Default;
        var hz = new Dictionary<string, IReadOnlyList<string>>();
        var ex = Array.Empty<ExemptionEntry>();
        Assert.Throws<ArgumentNullException>(() => new DesignGuardConfig(null, null!, rules, tokens, hz, ex, null));
        Assert.Throws<ArgumentNullException>(() => new DesignGuardConfig(null, scope, null!, tokens, hz, ex, null));
        Assert.Throws<ArgumentNullException>(() => new DesignGuardConfig(null, scope, rules, null!, hz, ex, null));
        Assert.Throws<ArgumentNullException>(() => new DesignGuardConfig(null, scope, rules, tokens, null!, ex, null));
        Assert.Throws<ArgumentNullException>(() => new DesignGuardConfig(null, scope, rules, tokens, hz, null!, null));
    }

    [Fact]
    public void GovernanceModels_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new RuleFinding(null!, "f", 1, 1, 1, "m"));
        Assert.Throws<ArgumentNullException>(() => new RuleFinding("DG0002", null!, 1, 1, 1, "m"));
        Assert.Throws<ArgumentNullException>(() => new RuleFinding("DG0002", "f", 1, 1, 1, null!));
        var finding = new RuleFinding("DG0002", "f", 1, 1, 1, "m");
        Assert.Throws<ArgumentNullException>(() => new DispositionedFinding(null!, FindingDisposition.Unexempted, null));
        Assert.Throws<ArgumentNullException>(() => new StaleExemption(null!, 0));
        Assert.Throws<ArgumentNullException>(() => new RatchetViolation(null!, 0, 0));
    }

    [Fact]
    public void ConfigError_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new ConfigError(null!, "m"));
        Assert.Throws<ArgumentNullException>(() => new ConfigError("CODE", null!));
    }

    [Fact]
    public void ConfigError_ToString_ContainsCodeAndMessage()
    {
        var e = new ConfigError("DGCFG_X", "boom");
        Assert.Equal("DGCFG_X: boom", e.ToString());
    }

    [Fact]
    public void ConfigLoadResult_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new ConfigLoadResult(null, null!));
    }
}
