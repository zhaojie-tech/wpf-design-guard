using System;
using System.IO;
using System.Linq;
using Xunit;

namespace WpfDesignGuard.Cli.Tests;

/// <summary>
/// Acceptance gates of ADR-0005: template layering,
/// seeding order, comment survival, re-enabling path.
/// </summary>
public class InitTemplateTests : IDisposable
{
    private const string Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dgtpl-" + Guid.NewGuid().ToString("N"));

    public InitTemplateTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "App", "Views"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    private void WriteStockCorpus()
    {
        File.WriteAllText(Path.Combine(_root, "App", "Views", "Legacy.xaml"),
            $"""<Grid xmlns="{Presentation}" Margin="8,6"><TextBlock FontSize="16"/></Grid>""");
    }

    private string Init() =>
        RunAndRead("init");

    private string RunAndRead(string command, params string[] extra)
    {
        var exit = Program.Main(new[] { command, "--root", _root }.Concat(extra).ToArray());
        Assert.Equal(0, exit);
        return File.ReadAllText(Path.Combine(_root, "designguard.json"));
    }

    [Fact]
    public void FirstInit_TokenRulesDisabled_WithComment()
    {
        WriteStockCorpus();
        var json = Init();

        var expected = new (string Id, string Severity)[]
        {
            ("DG0001", "error"), ("DG0002", "error"), ("DG0003", "error"),
            ("DG0004", "warning"), ("DG0005", "error"), ("DG0006", "error"),
        };
        foreach (var (id, severity) in expected)
        {
            Assert.Contains($"\"{id}\": {{ \"severity\": \"{severity}\", \"enabled\": false }}", json);
        }

        Assert.Contains("Token-dependent rules are OFF", json);
        Assert.Contains("RE-RUN `designguard init`", json);
        // Token-independent rules are NOT listed .
        Assert.DoesNotContain("\"DG0007\": {", json);
        Assert.DoesNotContain("\"DG0012\": {", json);
    }

    [Fact]
    public void FirstInit_TemplateComments_ParseCleanly()
    {
        WriteStockCorpus();
        var json = Init();
        var load = WpfDesignGuard.Core.Configuration.ConfigLoader.LoadFromText(json);
        Assert.False(load.HasErrors, string.Join(" | ", load.Errors.Select(e => e.ToString())));
        Assert.False(load.Config!.IsEnabled("DG0001"));
        Assert.True(load.Config.IsEnabled("DG0007"));
    }

    [Fact]
    public void FirstInit_SeedsBaselineWithTokenRules_SeedingOrderContract()
    {
        // ADR-0005 layering: seeding uses the fully-enabled config even though the written
        // template disables token rules — enabling them later must not go red.
        WriteStockCorpus(); // DG0002 (6 off-ladder) + DG0001 (bare 16)
        var json = Init();

        Assert.Contains("\"DG0002\": 1", json); // global counts include the token rules
        Assert.Contains("\"DG0001\": 1", json);
        Assert.Contains("App/Views/Legacy.xaml", json); // per-file snapshot
    }

    [Fact]
    public void EnableAfterInit_VerifyIsGreen()
    {
        WriteStockCorpus();
        var json = Init();

        json = json.Replace(
            "\"DG0001\": { \"severity\": \"error\", \"enabled\": false }",
            "\"DG0001\": { \"severity\": \"error\", \"enabled\": true }");
        File.WriteAllText(Path.Combine(_root, "designguard.json"), json);

        Assert.Equal(0, Program.Main(new[] { "verify", "--root", _root })); // baseline covers it
    }

    [Fact]
    public void SecondInit_DoesNotFlipUserSwitches()
    {
        WriteStockCorpus();
        Init();

        var json = File.ReadAllText(Path.Combine(_root, "designguard.json"));
        json = json.Replace(
            "\"DG0002\": { \"severity\": \"error\", \"enabled\": false }",
            "\"DG0002\": { \"severity\": \"warning\", \"enabled\": true }");
        File.WriteAllText(Path.Combine(_root, "designguard.json"), json);

        var after = RunAndRead("init");
        Assert.Contains("\"DG0002\": { \"severity\": \"warning\", \"enabled\": true }", after);
    }

    [Fact]
    public void BaselineUpdate_PreservesComments_AndKeepsDisabledRuleCounts()
    {
        WriteStockCorpus();
        Init();

        Assert.Equal(0, Program.Main(new[] { "baseline", "--update", "--root", _root }));
        var after = File.ReadAllText(Path.Combine(_root, "designguard.json"));

        Assert.Contains("Token-dependent rules are OFF", after); // comments survive re-baseline
        Assert.Contains("\"DG0001\": 1", after);                  // disabled rule keeps its seeded count
        Assert.Contains("\"enabled\": false", after);
    }
}

/// <summary>Review round 2 F1: a plain re-init must not silently erase the baseline of
/// rules that are disabled in the written config (the "enable later" path depends on it).</summary>
public class ReInitBaselineTests : IDisposable
{
    private const string Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dgreinit-" + Guid.NewGuid().ToString("N"));

    public ReInitBaselineTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "App", "Views"));
        File.WriteAllText(Path.Combine(_root, "App", "Views", "Legacy.xaml"),
            $"""<Grid xmlns="{Presentation}" Margin="8,6"><TextBlock FontSize="16"/></Grid>""");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    [Fact]
    public void AsIsReInit_KeepsDisabledRuleCounts()
    {
        Assert.Equal(0, Program.Main(new[] { "init", "--root", _root }));
        Assert.Equal(0, Program.Main(new[] { "init", "--root", _root })); // as-is re-run

        var json = File.ReadAllText(Path.Combine(_root, "designguard.json"));
        Assert.Contains("\"DG0001\": 1", json); // disabled rule: seeded count survives re-init
        Assert.Contains("\"DG0002\": 1", json);
    }

    [Fact]
    public void AsIsReInit_KeepsDisabledRulePerFileSnapshots()
    {
        Assert.Equal(0, Program.Main(new[] { "init", "--root", _root }));
        Assert.Equal(0, Program.Main(new[] { "init", "--root", _root }));

        var json = File.ReadAllText(Path.Combine(_root, "designguard.json"));
        Assert.Contains("\"App/Views/Legacy.xaml\": { \"DG0001\": 1", json);
    }

    [Fact]
    public void ReInit_StillKeepsUserSwitches_AndReseedsEnabledRules()
    {
        Assert.Equal(0, Program.Main(new[] { "init", "--root", _root }));
        // User adds a NEW violation file between inits (enabled-rule debt gets absorbed —
        // documented re-init semantics, ADR-0005 decision 6).
        File.WriteAllText(Path.Combine(_root, "App", "Views", "New.xaml"),
            $"""<Grid xmlns="{Presentation}" Margin="8,4"><TextBlock TextWrapping="Wrap"/></Grid>""");
        Assert.Equal(0, Program.Main(new[] { "init", "--root", _root }));

        var json = File.ReadAllText(Path.Combine(_root, "designguard.json"));
        Assert.Contains("\"DG0007\": 1", json); // enabled rule: re-seeded from current state
    }
}
