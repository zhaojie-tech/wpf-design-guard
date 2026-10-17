using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using WpfDesignGuard.Core;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.RepoRules;
using WpfDesignGuard.Core.Rules;
using WpfDesignGuard.Core.Workspace;
using WpfDesignGuard.Core.Xaml;

namespace WpfDesignGuard.Cli.Commands;

/// <summary>
/// Cold start: creates designguard.json (defaults when absent,
/// preserves existing sections when present) and writes every stock finding into the
/// baseline — global counts AND per-file snapshots. Hard-zero hits and exempted hits
/// never enter the baseline: domain stock stays red by design, and exemptions are debt
/// tracked separately.
/// </summary>
internal static class InitCommand
{
    public static int Run(string[] args)
    {
        var root = ArgumentParser.ValueOf(args, "--root") ?? Directory.GetCurrentDirectory();
        var configPath = Path.Combine(root, "designguard.json");
        var firstInit = !File.Exists(configPath);

        DesignGuardConfig config;
        if (!firstInit)
        {
            var load = ConfigLoader.LoadFromText(File.ReadAllText(configPath));
            if (load.HasErrors || load.Config == null)
            {
                Console.Error.WriteLine("designguard init: existing designguard.json is invalid:");
                foreach (var e in load.Errors)
                {
                    Console.Error.WriteLine($"  {e}");
                }

                return ExitCodes.ConfigError;
            }

            config = load.Config;
        }
        else
        {
            config = ConfigLoader.LoadFromText("""{ "version": 1 }""").Config!;
            Console.WriteLine($"designguard init: created default configuration at {configPath}");
        }

        var scanRoot = Path.GetFullPath(Path.Combine(root, config.Scope.Root));
        var files = WorkspaceScanner.ScanXamlFiles(scanRoot, config.Scope);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("designguard init: scan surface is empty — scope.include matches no XAML files (anti-idle self-check).");
            return ExitCodes.ConfigError;
        }

        var evaluation = EvaluateAll(scanRoot, files, config);
        var findingsByFile = evaluation.FindingsByFile;
        var ledger = new ExemptionLedger(config.Exemptions);
        var hardZero = new HardZeroResolver(config.HardZero);

        var globalCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var perFile = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal);
        var exemptedTotal = 0;

        foreach (var kv in findingsByFile)
        {
            var unexempted = new List<RuleFinding>();
            foreach (var finding in kv.Value)
            {
                if (ledger.Match(finding.RuleId, finding.File, hitType: null) != null)
                {
                    exemptedTotal++;
                    continue; // exempted stock never enters the baseline
                }

                unexempted.Add(finding);
            }

            var (downgradedIgnored, escalated) = ColdStartPolicy.Split(config, hardZero, kv.Key, unexempted);
            var fileCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var finding in escalated)
            {
                // Hard-zero stock intentionally stays OUT of the baseline (never relaxed);
                // only genuine unexempted, non-hard-zero hits seed the ratchet.
                if (hardZero.IsHardZero(kv.Key, finding.RuleId))
                {
                    continue;
                }

                fileCounts[finding.RuleId] = (fileCounts.TryGetValue(finding.RuleId, out var n) ? n : 0) + 1;
                globalCounts[finding.RuleId] = (globalCounts.TryGetValue(finding.RuleId, out var g) ? g : 0) + 1;
            }

            if (fileCounts.Count > 0)
            {
                perFile[kv.Key] = fileCounts;
            }
        }

        // ADR-0005: rules disabled in the WRITTEN config produce no
        // findings, so this fresh evaluation cannot see them — carry their previous
        // counts and per-file snapshots forward, otherwise re-enabling later goes red
        // and a plain re-init silently erases accepted debt.
        var previousBaseline = config.Baseline;
        foreach (var rule in RuleCatalog.All)
        {
            if (!config.IsEnabled(rule.Id) && previousBaseline != null &&
                previousBaseline.Counts.TryGetValue(rule.Id, out var prevCount))
            {
                globalCounts[rule.Id] = prevCount;
            }
        }

        if (previousBaseline != null)
        {
            foreach (var kv in previousBaseline.Files)
            {
                var carried = new Dictionary<string, int>(StringComparer.Ordinal);
                if (perFile.TryGetValue(kv.Key, out var current))
                {
                    foreach (var entry in current)
                    {
                        carried[entry.Key] = entry.Value;
                    }
                }

                foreach (var entry in kv.Value)
                {
                    if (!config.IsEnabled(entry.Key))
                    {
                        carried[entry.Key] = entry.Value; // disabled rule: keep the old snapshot
                    }
                }

                if (carried.Count > 0)
                {
                    perFile[kv.Key] = carried;
                }
            }
        }

        var baseline = new BaselineSnapshot(DateTime.UtcNow.ToString("yyyy-MM-dd"), globalCounts, perFile);
        var updated = new DesignGuardConfig(
            config.Schema, config.Scope, config.Rules, config.Tokens,
            config.HardZero, config.Exemptions, baseline);

        // ADR-0005 layering: evaluation/seeding above ran with the fully-enabled config;
        // the template only shapes the written text (token rules off + how-to comments).
        File.WriteAllText(configPath, Templating.ConfigTemplate.WriteWithTemplate(updated, firstInit), new UTF8Encoding(false));

        Console.WriteLine($"designguard init: scanned {files.Count} XAML files.");
        foreach (var rule in globalCounts.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {rule}: {globalCounts[rule]} stock finding(s) entered the baseline");
        }

        if (globalCounts.Count == 0)
        {
            Console.WriteLine("  no stock findings — clean baseline.");
        }

        if (exemptedTotal > 0)
        {
            Console.WriteLine($"  {exemptedTotal} exempted finding(s) excluded from the baseline (tracked via the exemption ledger).");
        }

        foreach (var note in evaluation.Unchecked)
        {
            Console.WriteLine($"  unchecked: {note}");
        }

        Console.WriteLine("Cold start complete: stock findings compile as warnings; new files and over-snapshot hits go red immediately.");
        return ExitCodes.Green;
    }

    internal sealed class WorkspaceEvaluation
    {
        public Dictionary<string, IReadOnlyList<RuleFinding>> FindingsByFile { get; } =
            new(StringComparer.Ordinal);

        /// <summary>Whole-repo blind spots (repo rules that could not judge), never silent.</summary>
        public List<string> Unchecked { get; } = new();
    }

    internal static WorkspaceEvaluation EvaluateAll(
        string scanRoot, IReadOnlyList<string> files, DesignGuardConfig config)
    {
        var evaluation = new WorkspaceEvaluation();
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var full = Path.Combine(scanRoot, file);
            string text;
            try
            {
                text = File.ReadAllText(full);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"designguard: unreadable file '{file}': {ex.Message}");
                continue;
            }

            var result = XamlRuleEngine.Evaluate(text, file, config);
            if (!result.ParseSucceeded)
            {
                Console.Error.WriteLine($"designguard: XAML parse failed for '{file}' (skipped, unchecked): {result.ParseError}");
                continue;
            }

            texts[file] = text;
            evaluation.FindingsByFile[file] = result.Findings;
        }

        // Whole-repo rules (DG0014 implicit-style chain, DG0017 coverage gate).
        var repo = RepoRuleEngine.Evaluate(new RepoContext(texts, config));
        foreach (var finding in repo.Findings)
        {
            if (evaluation.FindingsByFile.TryGetValue(finding.File, out var existing))
            {
                evaluation.FindingsByFile[finding.File] = existing.Concat(new[] { finding }).ToList();
            }
            else
            {
                evaluation.FindingsByFile[finding.File] = new[] { finding };
            }
        }

        evaluation.Unchecked.AddRange(repo.Unchecked);
        return evaluation;
    }
}
