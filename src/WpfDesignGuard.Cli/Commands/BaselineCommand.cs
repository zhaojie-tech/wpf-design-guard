using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using WpfDesignGuard.Core;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Rules;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Workspace;

namespace WpfDesignGuard.Cli.Commands;

/// <summary>
/// `baseline --update`: rewrites the GLOBAL baseline from current counts
/// (hard-zero hits and exempted hits excluded), prints the per-rule
/// diff and stamps a new date. Hard-zero domains are never touched.
/// </summary>
internal static class BaselineCommand
{
    public static int Run(string[] args)
    {
        if (!ArgumentParser.Has(args, "--update"))
        {
            Console.Error.WriteLine("designguard baseline: pass --update (this command never runs implicitly).");
            return ExitCodes.ConfigError;
        }

        var root = ArgumentParser.ValueOf(args, "--root") ?? Directory.GetCurrentDirectory();
        var configPath = Path.Combine(root, "designguard.json");
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"designguard baseline: no designguard.json at '{root}' — run `designguard init` first.");
            return ExitCodes.ConfigError;
        }

        var load = ConfigLoader.LoadFromText(File.ReadAllText(configPath));
        if (load.HasErrors || load.Config == null)
        {
            Console.Error.WriteLine("designguard baseline: designguard.json is invalid:");
            foreach (var e in load.Errors)
            {
                Console.Error.WriteLine($"  {e}");
            }

            return ExitCodes.ConfigError;
        }

        var config = load.Config;
        var scanRoot = Path.GetFullPath(Path.Combine(root, config.Scope.Root));
        var files = WorkspaceScanner.ScanXamlFiles(scanRoot, config.Scope);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("designguard baseline: scan surface is empty (anti-idle self-check).");
            return ExitCodes.ConfigError;
        }

        var evaluation = InitCommand.EvaluateAll(scanRoot, files, config);
        var findingsByFile = evaluation.FindingsByFile;
        var outcome = GovernanceEvaluator.Evaluate(config, findingsByFile);

        if (outcome.StaleExemptions.Count > 0 || outcome.RatchetViolations.Count > 0 ||
            outcome.PerFileViolations.Count > 0 ||
            outcome.Findings.Any(f => f.Disposition == FindingDisposition.HardZero))
        {
            Console.Error.WriteLine("designguard baseline: refusing to update — the tree is currently RED.");
            Console.Error.WriteLine("  Baseline updates are for honest re-baselining (accepted debt), not for silencing a red gate.");
            Console.Error.WriteLine("  Resolve hard-zero hits and stale exemptions first, then run baseline --update.");
            return ExitCodes.Violations;
        }

        var oldCounts = config.Baseline?.Counts ?? new Dictionary<string, int>();
        Console.WriteLine("designguard baseline --update: global counter diff (old -> new):");
        var allRules = oldCounts.Keys.Union(outcome.GlobalCounts.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
        foreach (var rule in allRules)
        {
            var oldV = oldCounts.TryGetValue(rule, out var o) ? o : 0;
            var newV = outcome.GlobalCounts.TryGetValue(rule, out var n) ? n : 0;
            if (oldV != newV)
            {
                Console.WriteLine($"  {rule}: {oldV} -> {newV}");
            }
            else
            {
                Console.WriteLine($"  {rule}: {oldV} (unchanged)");
            }
        }

        // Disabled rules produce no findings, so outcome.GlobalCounts lacks them; keep
        // their previous baseline counts so re-enabling later does not go red (ADR-0005).
        var counts = new Dictionary<string, int>(outcome.GlobalCounts, StringComparer.Ordinal);
        foreach (var rule in RuleCatalog.All)
        {
            if (!config.IsEnabled(rule.Id) && config.Baseline != null && config.Baseline.Counts.TryGetValue(rule.Id, out var previous))
            {
                counts[rule.Id] = previous;
            }
        }

        var baseline = new BaselineSnapshot(
            DateTime.UtcNow.ToString("yyyy-MM-dd"),
            counts,
            (IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>)outcome.PerFileCounts());

        var updated = new DesignGuardConfig(
            config.Schema, config.Scope, config.Rules, config.Tokens,
            config.HardZero, config.Exemptions, baseline);
        File.WriteAllText(configPath, Templating.ConfigTemplate.WriteWithTemplate(updated, firstInit: false), new UTF8Encoding(false));
        Console.WriteLine($"designguard baseline: rewritten with date {baseline.Date}.");
        return ExitCodes.Green;
    }
}
