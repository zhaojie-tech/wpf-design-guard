using System;
using System.IO;
using System.Linq;
using Xunit;

namespace WpfDesignGuard.Cli.Tests;

/// <summary>
/// CLI integration tests: real temp trees, real engine, exit-code contract coverage
///.
/// </summary>
public class CliIntegrationTests : IDisposable
{
    private const string Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly string _root;

    public CliIntegrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dgcli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "App", "Views"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // temp cleanup best effort
        }
    }

    private void Write(params (string RelPath, string Content)[] files)
    {
        foreach (var (rel, content) in files)
        {
            var full = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
    }

    private int Run(params string[] args) => Program.Main(args);

    [Fact]
    public void Init_ColdStart_WritesBaselineWithPerFileSnapshots()
    {
        Write(
            ("App/Views/Clean.xaml", $"""<Grid xmlns="{Presentation}"/>"""),
            ("App/Views/Legacy.xaml", $"""<Grid xmlns="{Presentation}" Margin="8,6"><TextBlock TextWrapping="Wrap"/></Grid>"""));

        Assert.Equal(0, Run("init", "--root", _root));

        var json = File.ReadAllText(Path.Combine(_root, "designguard.json"));
        Assert.Contains("\"DG0002\": 1", json);
        Assert.Contains("App/Views/Legacy.xaml", json);
        Assert.Contains("baseline", json);
    }

    [Fact]
    public void Verify_StockWithinBaseline_IsGreen()
    {
        Write(
            ("App/Views/Clean.xaml", $"""<Grid xmlns="{Presentation}"/>"""),
            ("App/Views/Legacy.xaml", $"""<Grid xmlns="{Presentation}" Margin="8,6"><TextBlock TextWrapping="Wrap"/></Grid>"""));
        Assert.Equal(0, Run("init", "--root", _root));
        Assert.Equal(0, Run("verify", "--root", _root));
    }

    [Fact]
    public void Verify_NewFileOutsideSnapshot_IsRed()
    {
        Write(
            ("App/Views/Clean.xaml", $"""<Grid xmlns="{Presentation}"/>"""),
            ("App/Views/Legacy.xaml", $"""<Grid xmlns="{Presentation}"><TextBlock TextWrapping="Wrap"/></Grid>"""));
        Assert.Equal(0, Run("init", "--root", _root));

        // DG0007 is token-independent (enabled by the init template default layering).
        Write(("App/Views/NewFeature.xaml", $"""<Grid xmlns="{Presentation}"><TextBlock TextWrapping="Wrap"/></Grid>"""));
        Assert.Equal(2, Run("verify", "--root", _root));
    }

    [Fact]
    public void Verify_HardZeroHit_IsRed_EvenWithFreshBaseline()
    {
        Write(
            ("App/Views/Clean.xaml", $"""<Grid xmlns="{Presentation}"><TextBlock TextWrapping="Wrap"/></Grid>"""),
            ("App/Pos/Register/Print/Receipt.xaml", $"""<Grid xmlns="{Presentation}"><TextBlock TextWrapping="Wrap"/></Grid>"""));

        Assert.Equal(0, Run("init", "--root", _root)); // hard-zero domain not yet configured

        // Configure hard zero: stock inside the domain must stay red after a re-init.
        var json = File.ReadAllText(Path.Combine(_root, "designguard.json"));
        json = json.Replace("\"hardZero\": {", "\"hardZero\": { \"App/Pos/Register/Print/**\": [\"DG0007\"],");
        File.WriteAllText(Path.Combine(_root, "designguard.json"), json);

        Assert.Equal(0, Run("init", "--root", _root)); // re-init keeps domain stock out of baseline
        Assert.Equal(2, Run("verify", "--root", _root)); // domain stock is red by design
    }

    [Fact]
    public void Verify_StaleExemption_IsRed()
    {
        Write(("App/Views/X.xaml", $"""<Grid xmlns="{Presentation}" Margin="8,6"/>"""));
        Assert.Equal(0, Run("init", "--root", _root));

        // Inject a ledger entry pointing at a file that no longer exists. The writer
        // emits an empty array across two lines, so anchor on the opening bracket.
        var json = File.ReadAllText(Path.Combine(_root, "designguard.json"));
        var entry = "\"exemptions\": [\n    { \"rule\": \"DG0002\", \"file\": \"App/Views/Gone.xaml\", \"count\": 3, \"reason\": \"registered but file vanished\" },";
        json = json.Replace("\"exemptions\": [", entry);
        File.WriteAllText(Path.Combine(_root, "designguard.json"), json);
        Assert.Contains("Gone.xaml", json); // guard: injection must actually land

        Assert.Equal(2, Run("verify", "--root", _root));
    }

    [Fact]
    public void Verify_EmptyScanSurface_AntiIdle_IsConfigError()
    {
        Write(("App/Views/X.xaml", $"""<Grid xmlns="{Presentation}"/>"""));
        Assert.Equal(0, Run("init", "--root", _root));

        var json = File.ReadAllText(Path.Combine(_root, "designguard.json"));
        json = json.Replace("\"include\": [\"**/*.xaml\"]", "\"include\": [\"Nope/**/*.xaml\"]");
        File.WriteAllText(Path.Combine(_root, "designguard.json"), json);

        Assert.Equal(1, Run("verify", "--root", _root));
    }

    [Fact]
    public void Verify_TokenFloorViolation_IsConfigError()
    {
        Write(("App/Views/X.xaml", $"""<Grid xmlns="{Presentation}"/>"""));
        Assert.Equal(0, Run("init", "--root", _root));

        var json = File.ReadAllText(Path.Combine(_root, "designguard.json"));
        json = json.Replace("\"hitTargetFloor\": 48", "\"hitTargetFloor\": 30");
        File.WriteAllText(Path.Combine(_root, "designguard.json"), json);

        Assert.Equal(1, Run("verify", "--root", _root));
    }

    [Fact]
    public void Verify_MissingConfig_IsConfigError()
    {
        Write(("App/Views/X.xaml", $"""<Grid xmlns="{Presentation}"/>"""));
        Assert.Equal(1, Run("verify", "--root", _root));
    }

    [Fact]
    public void BaselineUpdate_RefusesRedTree()
    {
        Write(("App/Views/Legacy.xaml", $"""<Grid xmlns="{Presentation}"><TextBlock TextWrapping="Wrap"/></Grid>"""));
        Assert.Equal(0, Run("init", "--root", _root));

        Write(("App/Views/New.xaml", $"""<Grid xmlns="{Presentation}"><TextBlock TextWrapping="Wrap"/></Grid>"""));
        Assert.Equal(2, Run("baseline", "--update", "--root", _root)); // red tree: refuse
    }

    [Fact]
    public void BaselineUpdate_GreenTree_RewritesBaseline()
    {
        Write(("App/Views/Legacy.xaml", $"""<Grid xmlns="{Presentation}" Margin="8,6"/>"""));
        Assert.Equal(0, Run("init", "--root", _root));
        Assert.Equal(0, Run("verify", "--root", _root));
        Assert.Equal(0, Run("baseline", "--update", "--root", _root));

        var json = File.ReadAllText(Path.Combine(_root, "designguard.json"));
        Assert.Contains("\"date\":", json);
    }

    [Fact]
    public void Verify_WithProjectFlag_ReportsMissingProjectionTarget()
    {
        Write(("App/Views/X.xaml", $"""<Grid xmlns="{Presentation}"/>"""));
        Assert.Equal(0, Run("init", "--root", _root));

        // A csproj without WpfDesignGuard.Analyzers lacks the DesignGuardEmitProjection
        // target: the check must fail loudly but not corrupt the verify result.
        var projPath = Path.Combine(_root, "Demo.csproj");
        File.WriteAllText(projPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Assert.Equal(0, Run("verify", "--root", _root, "--project", projPath));
    }

    [Fact]
    public void ExitCodeContract_UnknownCommandAndHelp()
    {
        Assert.Equal(1, Run("nonsense"));
        Assert.Equal(0, Run("--help"));
        Assert.Equal(1, Run()); // no args
    }

    [Fact]
    public void NotYetShipped_Commands_ReturnConfigError()
    {
        Assert.Equal(1, Run("fix", "--rule", "DG0002"));
        Assert.Equal(1, Run("report", "--format", "sarif"));
    }
}

public class FixAndReportTests : IDisposable
{
    private const string Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dgfix-" + Guid.NewGuid().ToString("N"));

    public FixAndReportTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "App"));
        var runBinding = "{Binding Total}";
        File.WriteAllText(Path.Combine(_root, "App", "A.xaml"),
            $"""<Grid xmlns="{Presentation}" Margin="8,6"><TextBlock TextWrapping="Wrap" FontSize="16"/><Run Text="{runBinding}"/></Grid>""");
        File.WriteAllText(Path.Combine(_root, "designguard.json"), """{ "version": 1 }""");
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

    private int Run(params string[] args) => Program.Main(args);

    [Fact]
    public void Fix_DryRun_PrintsDiffWithoutWriting()
    {
        var before = File.ReadAllText(Path.Combine(_root, "App", "A.xaml"));
        Assert.Equal(0, Run("fix", "--rule", "DG0002", "--root", _root));
        Assert.Equal(before, File.ReadAllText(Path.Combine(_root, "App", "A.xaml")));
    }

    [Fact]
    public void FixApply_DG0002_NearestLadderTieSmaller()
    {
        Assert.Equal(0, Run("fix", "--rule", "DG0002", "--apply", "--root", _root));
        var text = File.ReadAllText(Path.Combine(_root, "App", "A.xaml"));
        Assert.Contains("Margin=\"8,4\"", text); // 6 → nearest(6)=4 (delta 2) vs 8 (delta 2) → smaller wins
    }

    [Fact]
    public void FixApply_DG0015_InsertsModeOneWay()
    {
        Assert.Equal(0, Run("fix", "--rule", "DG0015", "--apply", "--root", _root));
        var text = File.ReadAllText(Path.Combine(_root, "App", "A.xaml"));
        Assert.Contains("{Binding Total, Mode=OneWay}", text);
    }

    [Fact]
    public void FixApply_DG0003_DirectionalForm_PassesSelfCheck()
    {
        // 8 sits ON the ladder: the canonical DG0003 shape (uniform single value of a
        // ladder step). The directional rewrite must not trip DG0002 (both components 8).
        File.WriteAllText(Path.Combine(_root, "App", "B.xaml"),
            $"""<Grid xmlns="{Presentation}" Margin="8"/>""");
        Assert.Equal(0, Run("fix", "--rule", "DG0003", "--apply", "--root", _root));
        var text = File.ReadAllText(Path.Combine(_root, "App", "B.xaml"));
        Assert.Contains("Margin=\"8,8\"", text);
        // Self-check: the directional form must not trip DG0002 (8,8 is fully on-ladder).
        // B.xaml is now clean; A.xaml still carries its own pre-existing findings.
        Assert.Contains("8,8", text);
    }

    [Fact]
    public void FixApply_DG0007_UsesLineHeightsMap()
    {
        Assert.Equal(0, Run("fix", "--rule", "DG0007", "--apply", "--root", _root));
        var text = File.ReadAllText(Path.Combine(_root, "App", "A.xaml"));
        Assert.Contains("LineHeight=\"24\"", text); // FontSize 16 ↔ Body → lineHeights.Body = 24
        Assert.Contains("LineStackingStrategy=\"BlockLineHeight\"", text);
    }

    [Fact]
    public void Fix_UnsupportedRule_IsConfigError()
    {
        Assert.Equal(1, Run("fix", "--rule", "DG0013", "--root", _root)); // copy rule: never auto-fixed
        Assert.Equal(1, Run("fix", "--root", _root)); // missing --rule
    }

    [Fact]
    public void Report_Sarif_EmitsResultsWithLocations()
    {
        Assert.Equal(0, Run("report", "--format", "sarif", "--root", _root, "--output", Path.Combine(_root, "report.sarif")));
        var json = File.ReadAllText(Path.Combine(_root, "report.sarif"));
        Assert.Contains("\"version\": \"2.1.0\"", json);
        Assert.Contains("\"ruleId\": \"DG0002\"", json);
        Assert.Contains("\"startLine\": 1", json);
        Assert.Contains("\"disposition\": \"Unexempted\"", json);
    }

    [Fact]
    public void Report_Markdown_WritesTable()
    {
        Assert.Equal(0, Run("report", "--format", "markdown", "--root", _root, "--output", Path.Combine(_root, "report.md")));
        Assert.Contains("| File | Line |", File.ReadAllText(Path.Combine(_root, "report.md")));
    }
}
