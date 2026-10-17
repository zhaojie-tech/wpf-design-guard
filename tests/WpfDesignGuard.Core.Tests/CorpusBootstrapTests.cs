using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Xaml;
using Xunit;

namespace WpfDesignGuard.Core.Tests;

/// <summary>
/// Corpus self-bootstrap: the engine's output over samples/ must
/// match expected-findings.txt exactly — every rule's positive example fires, every
/// negative example is silent, and nothing else is reported. Guards the rule engine
/// against self-decay.
/// </summary>
public class CorpusBootstrapTests
{
    public static string FindCorpusRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "DesignGuardCorpus");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent!;
        }

        throw new InvalidOperationException("samples/DesignGuardCorpus not found from " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Corpus_MatchesExpectedFindings_Exactly()
    {
        var corpusRoot = FindCorpusRoot();
        var config = ConfigLoader.LoadFromText(File.ReadAllText(Path.Combine(corpusRoot, "designguard.json"))).Config!;

        var actual = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(corpusRoot, "Xaml"), "*.xaml"))
        {
            var relative = Path.GetRelativePath(corpusRoot, file).Replace('\\', '/');
            var result = XamlRuleEngine.Evaluate(File.ReadAllText(file), relative, config);
            Assert.True(result.ParseSucceeded, $"{relative} failed to parse: {result.ParseError}");
            foreach (var grouping in result.Findings.GroupBy(f => f.RuleId))
            {
                actual[$"{grouping.Key} {relative}"] = grouping.Count();
            }
        }

        var expected = File.ReadAllLines(Path.Combine(corpusRoot, "expected-findings.txt"))
            .Where(l => l.Trim().Length > 0 && !l.StartsWith("#"))
            .Select(l => l.Trim())
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToList();

        var actualLines = actual.Select(kv => $"{kv.Key} {kv.Value}").OrderBy(l => l, StringComparer.Ordinal).ToList();
        Assert.Equal(expected, actualLines);
    }

    [Fact]
    public void Corpus_CoversEveryImplementedGeneratorRule_WithPositiveAndNegative()
    {
        var corpusRoot = FindCorpusRoot();
        var implemented = new[] { "DG0001", "DG0002", "DG0003", "DG0004", "DG0005", "DG0006", "DG0007", "DG0008", "DG0009", "DG0010", "DG0011", "DG0012", "DG0013", "DG0015", "DG0016" };
        foreach (var rule in implemented)
        {
            Assert.True(File.Exists(Path.Combine(corpusRoot, "Xaml", $"{rule}-positive.xaml")), $"{rule} lacks a positive example");
            Assert.True(File.Exists(Path.Combine(corpusRoot, "Xaml", $"{rule}-negative.xaml")), $"{rule} lacks a negative example");
        }
    }
}
