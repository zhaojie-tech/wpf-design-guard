using System.Collections.Generic;
using System.Linq;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using Xunit;

namespace WpfDesignGuard.Core.Tests;

public class ColdStartPolicyTests
{
    private static DesignGuardConfig ConfigWithSnapshot() =>
        ConfigLoader.LoadFromText("""
        {
          "version": 1,
          "baseline": {
            "date": "2026-10-15",
            "counts": { "DG0002": 1, "DG0007": 1 },
            "files": { "Ui/Legacy.xaml": { "DG0002": 1, "DG0007": 1 } }
          }
        }
        """).Config!;

    private static RuleFinding F(string rule) => new(rule, "Ui/Legacy.xaml", 3, 10, 3, "m");

    [Fact]
    public void WithinSnapshot_Downgrades()
    {
        var config = ConfigWithSnapshot();
        var (downgraded, escalated) = ColdStartPolicy.Split(
            config, new HardZeroResolver(config.HardZero), "Ui/Legacy.xaml",
            new[] { F("DG0002"), F("DG0007") });

        Assert.Equal(2, downgraded.Count);
        Assert.Empty(escalated);
    }

    [Fact]
    public void OverSnapshot_EscalatesTheSurplus()
    {
        var config = ConfigWithSnapshot();
        var (downgraded, escalated) = ColdStartPolicy.Split(
            config, new HardZeroResolver(config.HardZero), "Ui/Legacy.xaml",
            new[] { F("DG0002"), F("DG0002"), F("DG0002") });

        Assert.Single(downgraded);
        Assert.Equal(2, escalated.Count);
    }

    [Fact]
    public void FileOutsideSnapshot_EverythingEscalates()
    {
        var config = ConfigWithSnapshot();
        var (downgraded, escalated) = ColdStartPolicy.Split(
            config, new HardZeroResolver(config.HardZero), "Ui/New.xaml",
            new[] { F("DG0002") });

        Assert.Empty(downgraded);
        Assert.Single(escalated);
    }

    [Fact]
    public void HardZeroHits_NeverDowngrade()
    {
        var config = ConfigLoader.LoadFromText("""
        {
          "version": 1,
          "hardZero": { "Ui/**": ["DG0002"] },
          "baseline": { "files": { "Ui/Legacy.xaml": { "DG0002": 1 } } }
        }
        """).Config!;
        var (downgraded, escalated) = ColdStartPolicy.Split(
            config, new HardZeroResolver(config.HardZero), "Ui/Legacy.xaml",
            new[] { F("DG0002") });

        Assert.Empty(downgraded);
        Assert.Single(escalated);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var config = ConfigWithSnapshot();
        var hz = new HardZeroResolver(config.HardZero);
        Assert.Throws<System.ArgumentNullException>(() => ColdStartPolicy.Split(null!, hz, "f", System.Array.Empty<RuleFinding>()));
        Assert.Throws<System.ArgumentNullException>(() => ColdStartPolicy.Split(config, null!, "f", System.Array.Empty<RuleFinding>()));
    }
}
