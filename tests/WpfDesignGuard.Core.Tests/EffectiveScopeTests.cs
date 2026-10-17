using System;
using System.Collections.Generic;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using Xunit;

namespace WpfDesignGuard.Core.Tests;

public class EffectiveScopeTests
{
    [Fact]
    public void GlobalInclude_WithoutPerRule()
    {
        var scope = new ScopeConfig(
            ".",
            new[] { "App/**/*.xaml" },
            Array.Empty<string>(),
            new Dictionary<string, RuleScopeOverride>());

        var effective = EffectiveScope.Resolve(scope, "DG0002");
        Assert.True(effective.Matches("App/Views/Shell.xaml"));
        Assert.False(effective.Matches("Lib/Views/Shell.xaml"));
    }

    [Fact]
    public void PerRuleInclude_ReplacesGlobalInclude()
    {
        var scope = new ScopeConfig(
            ".",
            new[] { "App/**/*.xaml" },
            Array.Empty<string>(),
            new Dictionary<string, RuleScopeOverride>
            {
                ["DG0001"] = new RuleScopeOverride(new[] { "App/Ui/**" }, null),
            });

        var effective = EffectiveScope.Resolve(scope, "DG0001");
        Assert.True(effective.Matches("App/Ui/Button.xaml"));
        // Global include no longer applies to this rule.
        Assert.False(effective.Matches("App/Views/Shell.xaml"));
    }

    [Fact]
    public void PerRuleExclude_AddsToGlobalExclude()
    {
        var scope = new ScopeConfig(
            ".",
            new[] { "App/**/*.xaml" },
            new[] { "App/Themes/**" },
            new Dictionary<string, RuleScopeOverride>
            {
                ["DG0002"] = new RuleScopeOverride(null, new[] { "App/Legacy/**" }),
            });

        var effective = EffectiveScope.Resolve(scope, "DG0002");
        Assert.False(effective.Matches("App/Themes/Colors.xaml"));  // global exclude
        Assert.False(effective.Matches("App/Legacy/Old.xaml"));     // per-rule exclude
        Assert.True(effective.Matches("App/Views/Shell.xaml"));

        // Other rules are unaffected by the per-rule exclude.
        var other = EffectiveScope.Resolve(scope, "DG0007");
        Assert.True(other.Matches("App/Legacy/Old.xaml"));
    }

    [Fact]
    public void PerRuleInclude_StillHonoursGlobalExclude()
    {
        var scope = new ScopeConfig(
            ".",
            new[] { "App/**/*.xaml" },
            new[] { "App/Specs/**" },
            new Dictionary<string, RuleScopeOverride>
            {
                ["DG0001"] = new RuleScopeOverride(new[] { "App/**" }, null),
            });

        var effective = EffectiveScope.Resolve(scope, "DG0001");
        Assert.False(effective.Matches("App/Specs/Demo.xaml"));
    }
}
