using System;
using System.IO;
using System.Linq;
using Xunit;

namespace WpfDesignGuard.Cli.Tests;

/// <summary>
/// CLI wiring of the whole-repo rules: DG0014 (implicit TextBlock style chain) and
/// DG0017 (clickable-type coverage gate), including type-scoped exemptions.
/// </summary>
public class RepoRuleCliTests : IDisposable
{
    private const string P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "dgcli-repo-" + Guid.NewGuid().ToString("N"));

    public RepoRuleCliTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Views"));
        Directory.CreateDirectory(Path.Combine(_root, "Themes"));
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

    private void Write(string rel, string content)
    {
        var full = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private int Run(params string[] args) => Program.Main(args);

    private void WriteConfig(string json) => Write("designguard.json", json);

    private static (int Exit, string Output) RunCaptured(params string[] args)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var exit = Program.Main(args);
            return (exit, writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public void Dg0014_NewWinnerIsRed_PerFileRatchet()
    {
        Write("designguard.json", """{ "version": 1 }""");
        Write("App.xaml", $"""
        <Application xmlns="{P}"><Application.Resources><ResourceDictionary>
          <ResourceDictionary.MergedDictionaries><ResourceDictionary Source="Themes/Text.xaml"/></ResourceDictionary.MergedDictionaries>
        </ResourceDictionary></Application.Resources></Application>
        """);
        Write("Themes/Text.xaml", $"""<ResourceDictionary xmlns="{P}"><Style TargetType="TextBlock"/></ResourceDictionary>""");
        Write("Views/Main.xaml", $"""<Page xmlns="{P}"><TextBlock Text="hi"/></Page>""");

        Assert.Equal(0, Run("init", "--root", _root));
        Assert.Contains("DG0014", File.ReadAllText(Path.Combine(_root, "designguard.json")));
        Assert.Equal(0, Run("verify", "--root", _root));

        // A later merged dictionary becomes the winner and lacks FontSize.
        Write("App.xaml", $"""
        <Application xmlns="{P}"><Application.Resources><ResourceDictionary>
          <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="Themes/Text.xaml"/>
            <ResourceDictionary Source="Themes/Text2.xaml"/>
          </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary></Application.Resources></Application>
        """);
        Write("Themes/Text2.xaml", $"""<ResourceDictionary xmlns="{P}"><Style TargetType="TextBlock"/></ResourceDictionary>""");

        // The winner moved from Text.xaml to Text2.xaml: the global count is unchanged,
        // but the per-file snapshot turns it red (ADR-0007) — no more "migration" hole.
        var (exit, output) = RunCaptured("verify", "--root", _root);
        Assert.Equal(2, exit);
        Assert.Contains("Themes/Text2.xaml", output);
        Assert.Contains("PER-FILE RATCHET", output);
    }

    [Fact]
    public void Dg0017_UncoveredTypeAfterInitIsRed()
    {
        WriteConfig("""{ "version": 1, "tokens": { "styleLibrary": "Themes/Controls.xaml" } }""");
        Write("Views/Main.xaml", $"""<Page xmlns="{P}"><Button/></Page>""");
        Write("Themes/Controls.xaml", $"""<ResourceDictionary xmlns="{P}"><Style TargetType="Button"><Setter Property="MinHeight" Value="40"/></Style></ResourceDictionary>""");

        Assert.Equal(0, Run("init", "--root", _root));
        Assert.Equal(0, Run("verify", "--root", _root));

        // New clickable type, library never extended: coverage gate fails.
        Write("Views/New.xaml", $"""<Page xmlns="{P}"><ToggleButton/></Page>""");
        Assert.Equal(2, Run("verify", "--root", _root));
    }

    [Fact]
    public void Dg0017_TypeScopedExemption_IsGreen()
    {
        WriteConfig("""
        {
          "version": 1,
          "tokens": { "styleLibrary": "Themes/Controls.xaml" },
          "exemptions": [
            { "rule": "DG0017", "file": "Themes/Controls.xaml", "count": 1, "reason": "template internal", "type": "RepeatButton" }
          ]
        }
        """);
        Write("Views/Main.xaml", $"""<Page xmlns="{P}"><RepeatButton/></Page>""");
        Write("Themes/Controls.xaml", $"""<ResourceDictionary xmlns="{P}"/>""");

        Assert.Equal(0, Run("init", "--root", _root));
        Assert.Equal(0, Run("verify", "--root", _root)); // typed exemption consumes the finding
    }

    [Fact]
    public void Dg0017_UnconfiguredLibrary_SurfacesUnchecked()
    {
        WriteConfig("""{ "version": 1 }""");
        Write("Views/Main.xaml", $"""<Page xmlns="{P}"><Button/></Page>""");

        Assert.Equal(0, Run("init", "--root", _root));
        var json = File.ReadAllText(Path.Combine(_root, "designguard.json"));
        Assert.DoesNotContain("\"DG0017\"", json); // no false finding without a library
    }
}

public class PerFileRatchetCliTests : IDisposable
{
    private const string P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dgcli-pf-" + Guid.NewGuid().ToString("N"));

    public PerFileRatchetCliTests()
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

    private void Write(string rel, string content)
    {
        var full = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private int Run(params string[] args) => Program.Main(args);

    [Fact]
    public void GlobalWithinBaseline_ButFileOverSnapshot_IsRed_AndUpdateRefuses()
    {
        // Two wrapped TextBlocks: global count 2 == baseline count 2, but the file
        // snapshot allows 1 — the pre-ADR-0007 hole would have been green.
        Write("App/Views/A.xaml",
            $"""<Page xmlns="{P}"><TextBlock TextWrapping="Wrap"/><TextBlock TextWrapping="Wrap"/></Page>""");
        Write("designguard.json", """
        {
          "version": 1,
          "baseline": {
            "date": "2026-10-17",
            "counts": { "DG0007": 2 },
            "files": { "App/Views/A.xaml": { "DG0007": 1 } }
          }
        }
        """);

        Assert.Equal(2, Run("verify", "--root", _root));
        Assert.Equal(2, Run("baseline", "--update", "--root", _root));
    }

    [Fact]
    public void FileAtSnapshot_IsGreen()
    {
        Write("App/Views/A.xaml",
            $"""<Page xmlns="{P}"><TextBlock TextWrapping="Wrap"/></Page>""");
        Write("designguard.json", """
        {
          "version": 1,
          "baseline": {
            "date": "2026-10-17",
            "counts": { "DG0007": 1 },
            "files": { "App/Views/A.xaml": { "DG0007": 1 } }
          }
        }
        """);

        Assert.Equal(0, Run("verify", "--root", _root));
    }
}
