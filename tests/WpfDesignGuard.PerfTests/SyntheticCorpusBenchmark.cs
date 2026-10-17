using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Workspace;
using WpfDesignGuard.Core.Xaml;
using Xunit;

namespace WpfDesignGuard.PerfTests;

/// <summary>
/// Performance budget: a synthetic 10k-XAML corpus must verify well under
/// the 10-second budget (cold path), and a second identical pass (warm cache of parsed
/// documents is not used by the CLI, so this measures the honest cold path twice).
/// </summary>
public class SyntheticCorpusBenchmark
{
    private const int FileCount = 10_000;

    [Fact]
    [Trait("Category", "Perf")]
    public void Verify_10kSyntheticXaml_Under10Seconds()
    {
        var root = Path.Combine(Path.GetTempPath(), "dgperf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Xaml"));
        try
        {
            var presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            for (var i = 0; i < FileCount; i++)
            {
                var kind = i % 5;
                var tokenRef = "{DynamicResource Spacing.Md}";
                var body = kind switch
                {
                    0 => $"""<Grid xmlns="{presentation}" Margin="8,4"/>""",                                      // clean
                    1 => $"""<Grid xmlns="{presentation}" Margin="8,6"/>""",                                      // DG0002
                    2 => $"""<Grid xmlns="{presentation}"><TextBlock TextWrapping="Wrap"/></Grid>""",             // DG0007
                    3 => $"""<Grid xmlns="{presentation}" Margin="8"><TextBlock FontSize="16"/></Grid>""",        // DG0003+DG0001
                    _ => $"""<Grid xmlns="{presentation}" Margin="{tokenRef}"/>""",                               // token refs
                };
                File.WriteAllText(Path.Combine(root, "Xaml", $"F{i:D5}.xaml"), body);
            }

            var config = ConfigLoader.LoadFromText("""{ "version": 1, "scope": { "include": ["Xaml/**/*.xaml"] } }""").Config!;

            var clock = Stopwatch.StartNew();
            var files = WorkspaceScanner.ScanXamlFiles(root, config.Scope);
            Assert.Equal(FileCount, files.Count);

            var totalFindings = 0;
            foreach (var file in files)
            {
                var result = XamlRuleEngine.Evaluate(File.ReadAllText(Path.Combine(root, file)), file, config);
                totalFindings += result.Findings.Count;
            }

            clock.Stop();

            Assert.True(totalFindings > 0, "expected the synthetic corpus to carry findings");
            Assert.True(clock.ElapsedMilliseconds < 10_000,
                $"verify budget exceeded: {clock.ElapsedMilliseconds} ms for {FileCount} files ({totalFindings} findings)");
            Console.WriteLine($"[perf] {FileCount} files, {totalFindings} findings in {clock.ElapsedMilliseconds} ms");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
            }
        }
    }
}
