using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using WpfDesignGuard.Analyzers;
using Xunit;

namespace WpfDesignGuard.Analyzers.Tests;

internal sealed class InMemoryText : AdditionalText
{
    private readonly SourceText _text;

    public InMemoryText(string path, string content)
    {
        Path = path;
        _text = SourceText.From(content, Encoding.UTF8);
    }

    public override string Path { get; }

    public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
}

/// <summary>Shared host: drives the real generator against in-memory additional files.</summary>
internal static class GeneratorTestHost
{
    public static (ImmutableArray<Diagnostic> Diagnostics, XamlDesignGenerator Generator, Compilation Compilation) Run(
        params (string Path, string Content)[] additionalFiles)
    {
        var generator = new XamlDesignGenerator();
        var reference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var compilation = CSharpCompilation.Create(
            "gen_tests",
            new[] { SyntaxFactory.ParseSyntaxTree("class C { }") },
            new[] { reference });

        var driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            additionalFiles.Select(a => (AdditionalText)new InMemoryText(a.Path, a.Content)),
            new CSharpParseOptions());

        var drivenDriver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        var result = drivenDriver.GetRunResult();
        var allDiagnostics = result.Diagnostics
            .AddRange(((CSharpCompilation)updated).GetDiagnostics());
        return (allDiagnostics, generator, (CSharpCompilation)updated);
    }

    public static ImmutableArray<Diagnostic> RuleDiagnostics(params (string Path, string Content)[] additionalFiles)
    {
        var (diagnostics, _, _) = Run(additionalFiles);
        if (System.Environment.GetEnvironmentVariable("DG_TEST_DEBUG") == "1")
        {
            foreach (var d in diagnostics)
            {
                System.Console.WriteLine($"[diag] {d.Id} {d.Severity}: {d.GetMessage()}");
            }
        }

        return diagnostics.Where(d => d.Id.StartsWith("DG", StringComparison.Ordinal)).ToImmutableArray();
    }
}

public class PathUtilitiesTests
{
    [Fact]
    public void MakeRelative_RelativizesAbsolutePathsUnderConfigDir()
    {
        var rel = PathUtilities.MakeRelative(
            "/abs/proj",
            "/abs/proj/Ui/Spike2.xaml");
        Assert.Equal("Ui/Spike2.xaml", rel);
    }

    [Fact]
    public void MakeRelative_FallsBackToFileName_WhenOutside()
    {
        var rel = PathUtilities.MakeRelative("/other/base", "/abs/proj/Ui/S.xaml");
        Assert.Equal("S.xaml", rel);
    }
}

public class GeneratorPipelineTests
{
    private const string Config = """
        {
          "version": 1,
          "baseline": {
            "date": "2026-10-15",
            "counts": { "DG0002": 1, "DG0007": 1 },
            "files": { "Ui/Legacy.xaml": { "DG0002": 1, "DG0007": 1 } }
          }
        }
        """;

    private const string ViolatingXaml = """
        <Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <Grid Margin="8,6">
            <TextBlock TextWrapping="Wrap" />
          </Grid>
        </Page>
        """;

    [Fact]
    public void SnapshotFile_HitsDowngradeToWarnings()
    {
        var diagnostics = GeneratorTestHost.RuleDiagnostics(
            ("/abs/proj/designguard.json", Config),
            ("/abs/proj/Ui/Legacy.xaml", ViolatingXaml));

        var dg = diagnostics.Where(d => d.Id == "DG0002" || d.Id == "DG0007").ToList();
        Assert.NotEmpty(dg);
        Assert.All(dg, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
    }

    [Fact]
    public void FileOutsideSnapshot_EscalatesToError()
    {
        var diagnostics = GeneratorTestHost.RuleDiagnostics(
            ("/abs/proj/designguard.json", Config),
            ("/abs/proj/Ui/NewPage.xaml", ViolatingXaml));

        var dg = diagnostics.Where(d => d.Id == "DG0002" || d.Id == "DG0007").ToList();
        Assert.NotEmpty(dg);
        Assert.All(dg, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    private const string SpikeConfigJsonc = """
        {
          // Spike config: default token table, one baseline entry so the corpus stays warning-level
          // (cold-start downgrade demo: 1 hit <= snapshot 1).
          "version": 1,
          "baseline": {
            "date": "2026-10-15",
            "counts": { "DG0002": 1, "DG0007": 1 },
            "files": { "Ui/Spike2.xaml": { "DG0002": 1, "DG0007": 1 } }
          }
        }
        """;

    private const string Spike2Xaml = """
        <Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <!-- In the baseline per-file snapshot: same hits must downgrade to warnings (cold start). -->
          <Grid Margin="8,6">
            <TextBlock TextWrapping="Wrap" Text="legacy" />
          </Grid>
        </Page>
        """;

    [Fact]
    public void SpikeCorpus_ExactJsoncConfig_DowngradesSnapshotFile()
    {
        var diagnostics = GeneratorTestHost.RuleDiagnostics(
            ("designguard.json", SpikeConfigJsonc),
            ("Ui/Spike2.xaml", Spike2Xaml));

        var dg = diagnostics.Where(d => d.Id == "DG0002" || d.Id == "DG0007").ToList();
        Assert.NotEmpty(dg);
        Assert.All(dg, d => Assert.True(d.Severity == DiagnosticSeverity.Warning,
            $"'{d.Id}' expected Warning, got {d.Severity}"));
    }

    [Fact]
    public void SpikeCorpus_AbsolutePaths_BothFiles_DowngradesSnapshotFile()
    {
        var diagnostics = GeneratorTestHost.RuleDiagnostics(
            ("/abs/proj/designguard.json", SpikeConfigJsonc),
            ("/abs/proj/Ui/SamplePage.xaml", ViolatingXaml),
            ("/abs/proj/Ui/Spike2.xaml", Spike2Xaml));

        var sample = diagnostics.Where(d => d.Id.StartsWith("DG00") && d.Location.GetLineSpan().Path.EndsWith("SamplePage.xaml")).ToList();
        var spike2 = diagnostics.Where(d => d.Id.StartsWith("DG00") && d.Location.GetLineSpan().Path.EndsWith("Spike2.xaml")).ToList();

        Assert.All(sample, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity)); // outside snapshot
        Assert.All(spike2, d => Assert.True(d.Severity == DiagnosticSeverity.Warning,
            $"'{d.Id}' expected Warning, got {d.Severity}"));
    }

    [Fact]
    public void RelativeInjectedPaths_SnapshotDowngradeStillApplies()
    {
        // MSBuild injects items with their original (project-relative) specs: the real
        // spike surface is (designguard.json, Ui/Legacy.xaml) — both relative.
        var diagnostics = GeneratorTestHost.RuleDiagnostics(
            ("designguard.json", Config),
            ("Ui/Legacy.xaml", ViolatingXaml));

        var dg = diagnostics.Where(d => d.Id == "DG0002" || d.Id == "DG0007").ToList();
        Assert.NotEmpty(dg);
        Assert.All(dg, d => Assert.True(d.Severity == DiagnosticSeverity.Warning,
            $"'{d.Id}' expected Warning (in-snapshot downgrade), got {d.Severity}"));
    }

    [Fact]
    public void MissingConfig_SelfDisablesWithDGCFG001()
    {
        var diagnostics = GeneratorTestHost.RuleDiagnostics(
            ("/abs/proj/Ui/NewPage.xaml", ViolatingXaml));

        Assert.Contains(diagnostics, d => d.Id == "DGCFG001");
        Assert.DoesNotContain(diagnostics, d => d.Id == "DG0002");
    }

    [Fact]
    public void InvalidConfig_SelfDisablesWithDGCFG002_ListingErrors()
    {
        var diagnostics = GeneratorTestHost.RuleDiagnostics(
            ("/abs/proj/designguard.json", """{ "version": 1, "tokens": { "hitTargetFloor": 10 } }"""),
            ("/abs/proj/Ui/NewPage.xaml", ViolatingXaml));

        var cfg = Assert.Single(diagnostics, d => d.Id == "DGCFG002");
        Assert.Contains("hitTargetFloor", cfg.GetMessage());
        Assert.DoesNotContain(diagnostics, d => d.Id == "DG0002");
    }

    [Fact]
    public void UnparseableXaml_ReportsDGCFG003()
    {
        var diagnostics = GeneratorTestHost.RuleDiagnostics(
            ("/abs/proj/designguard.json", Config),
            ("/abs/proj/Ui/Broken.xaml", "<Page><Unclosed>"));

        Assert.Contains(diagnostics, d => d.Id == "DGCFG003");
    }

    [Fact]
    public void DiagnosticLocations_PointIntoTheXamlFile()
    {
        var diagnostics = GeneratorTestHost.RuleDiagnostics(
            ("/abs/proj/designguard.json", Config),
            ("/abs/proj/Ui/NewPage.xaml", ViolatingXaml));

        var d = diagnostics.First(x => x.Id == "DG0002");
        var span = d.Location.GetLineSpan();
        Assert.Equal("/abs/proj/Ui/NewPage.xaml", span.Path);
        Assert.Equal(2, span.StartLinePosition.Line + 1);      // Grid line
        Assert.True(span.StartLinePosition.Character + 1 > 0); // column present
    }

    [Fact]
    public void CacheBehaviour_UnchangedRun_ParsesNothing()
    {
        // Cache-hit path zero re-computation (performance budget): the second
        // run over identical inputs must perform zero new parses.
        var generator = new XamlDesignGenerator();
        var reference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var compilation = CSharpCompilation.Create("gen_tests",
            new[] { SyntaxFactory.ParseSyntaxTree("class C { }") }, new[] { reference });

        var driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            new AdditionalText[]
            {
                new InMemoryText("/abs/proj/designguard.json", Config),
                new InMemoryText("/abs/proj/Ui/A.xaml", ViolatingXaml),
                new InMemoryText("/abs/proj/Ui/B.xaml", ViolatingXaml),
            },
            new CSharpParseOptions());

        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        Assert.Equal(2, generator.Telemetry.Current);

        generator.Telemetry.Reset();
        driver.RunGenerators(compilation);
        Assert.Equal(0, generator.Telemetry.Current);
    }

    [Fact]
    public void CacheBehaviour_ChangingOneFile_ReparsesExactlyOne()
    {
        // Same driver across runs (as the compiler keeps across keystrokes): changing one
        // file's text re-parses exactly that file — the other stays cached by
        // (path, SourceText-reference) identity.
        var generator = new XamlDesignGenerator();
        var reference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var compilation = CSharpCompilation.Create("gen_tests",
            new[] { SyntaxFactory.ParseSyntaxTree("class C { }") }, new[] { reference });

        var configText = new InMemoryText("/abs/proj/designguard.json", Config);
        var aText = new InMemoryText("/abs/proj/Ui/A.xaml", ViolatingXaml);
        var bText = new InMemoryText("/abs/proj/Ui/B.xaml", ViolatingXaml);

        var driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            new AdditionalText[] { configText, aText, bText },
            new CSharpParseOptions());

        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        Assert.Equal(2, generator.Telemetry.Current);

        generator.Telemetry.Reset();
        driver = (CSharpGeneratorDriver)driver.ReplaceAdditionalText(
            bText, new InMemoryText("/abs/proj/Ui/B.xaml", ViolatingXaml.Replace("8,6", "8,7")));
        driver.RunGenerators(compilation);
        // Exactly one new parse: B only. A stays cached (per-file pipeline nodes).
        Assert.Equal(1, generator.Telemetry.Current);
    }
}
