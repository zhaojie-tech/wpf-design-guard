using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using WpfDesignGuard.Core;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;
using WpfDesignGuard.Core.Workspace;

namespace WpfDesignGuard.Cli.Commands;

/// <summary>
/// Full evaluation per the shared engine: scope scan → per-rule evaluation → governance
/// layers (hard-zero ∧ exemption bidirectional equality ∧ global ratchet). Reports the
/// unchecked surface (unimplemented rules, parse failures, third-party blind spots) and,
/// with --project, the explicit generator-vs-CLI file-set delta.
/// </summary>
internal static class VerifyCommand
{
    public static int Run(string[] args)
    {
        var root = ArgumentParser.ValueOf(args, "--root") ?? Directory.GetCurrentDirectory();
        var projectArg = ArgumentParser.ValueOf(args, "--project");
        var configPath = Path.Combine(root, "designguard.json");

        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"designguard verify: no designguard.json at '{root}' — run `designguard init` first (cold start).");
            return ExitCodes.ConfigError;
        }

        var load = ConfigLoader.LoadFromText(File.ReadAllText(configPath));
        if (load.HasErrors || load.Config == null)
        {
            Console.Error.WriteLine("designguard verify: designguard.json is invalid (configuration error):");
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
            Console.Error.WriteLine("designguard verify: scan surface is empty — scope.include matches no XAML files (anti-idle self-check, configuration error).");
            return ExitCodes.ConfigError;
        }

        var evaluation = InitCommand.EvaluateAll(scanRoot, files, config);
        var findingsByFile = evaluation.FindingsByFile;
        var outcome = GovernanceEvaluator.Evaluate(config, findingsByFile);

        foreach (var note in evaluation.Unchecked)
        {
            Console.WriteLine($"  unchecked: {note}");
        }

        var exit = Report(scanRoot, config, files.Count, outcome);

        if (projectArg != null)
        {
            ReportProjectionDelta(root, projectArg, config);
        }

        return exit;
    }

    private static int Report(string scanRoot, DesignGuardConfig config, int scannedFiles, VerificationOutcome outcome)
    {
        Console.WriteLine($"designguard verify: {scannedFiles} file(s) scanned, {outcome.Findings.Count} finding(s).");

        var ratchetRules = outcome.RatchetViolations.Select(v => v.RuleId).ToHashSet(StringComparer.Ordinal);
        foreach (var f in outcome.Findings.Where(f => f.Disposition != FindingDisposition.Exempted)
                     .OrderBy(f => f.Finding.File, StringComparer.Ordinal)
                     .ThenBy(f => f.Finding.Line).ThenBy(f => f.Finding.Column))
        {
            var tag = f.Disposition switch
            {
                FindingDisposition.HardZero => "HARD-ZERO",
                FindingDisposition.Unexempted when ratchetRules.Contains(f.Finding.RuleId) => "OVER-BASELINE",
                FindingDisposition.Unexempted => "IN-BASELINE",
                _ => "OUT-OF-SCOPE",
            };
            Console.WriteLine($"  {f.Finding.File}({f.Finding.Line},{f.Finding.Column}): {tag} {f.Finding.RuleId}: {f.Finding.Message}");
        }

        foreach (var stale in outcome.StaleExemptions)
        {
            Console.WriteLine(
                $"  STALE EXEMPTION {stale.Entry.Rule} '{stale.Entry.File}': registered {stale.Entry.Count}, current hits {stale.ActualCount} — clean up the ledger (exemptions are debt).");
        }

        foreach (var v in outcome.RatchetViolations.OrderBy(v => v.RuleId, StringComparer.Ordinal))
        {
            var allowed = config.Baseline?.Counts.TryGetValue(v.RuleId, out var a) == true ? a : 0;
            Console.WriteLine($"  RATCHET {v.RuleId}: {v.Actual} unexempted hit(s) > baseline {allowed}.");
        }

        foreach (var v in outcome.PerFileViolations.OrderBy(v => v.File, StringComparer.Ordinal).ThenBy(v => v.RuleId, StringComparer.Ordinal))
        {
            Console.WriteLine($"  PER-FILE RATCHET {v.RuleId} '{v.File}': {v.Actual} unexempted hit(s) > snapshot {v.Allowed}.");
        }

        var hardZeroHits = outcome.Findings.Count(f => f.Disposition == FindingDisposition.HardZero);
        var unexemptedTotal = outcome.Findings.Count(f => f.Disposition == FindingDisposition.Unexempted);

        // Unchecked surface, never silent.
        var notYetImplemented = RuleCatalog.All
            .Where(r => RuleCatalog.IsImplementedAt(r, GuardVersion.Current) == false)
            .Select(r => r.Id).ToList();
        if (notYetImplemented.Count > 0)
        {
            Console.WriteLine($"  unchecked surface: rules not yet implemented at v{GuardVersion.Current}: {string.Join(", ", notYetImplemented)}.");
        }

        var baseline = config.Baseline;
        if (baseline == null)
        {
            Console.WriteLine("  no baseline section — the gate is strict (any unexempted hit is red). Run `designguard init` for a cold start.");
        }

        var isGreen = outcome.IsGreen && hardZeroHits == 0;
        Console.WriteLine(isGreen
            ? $"designguard verify: GREEN ({unexemptedTotal} unexempted within baseline, {outcome.Findings.Count - unexemptedTotal - hardZeroHits} exempted, 0 hard-zero hits)."
            : $"designguard verify: RED (hard-zero hits: {hardZeroHits}, over-baseline rules: {outcome.RatchetViolations.Count}, over-snapshot files: {outcome.PerFileViolations.Count}, stale exemptions: {outcome.StaleExemptions.Count}).");

        return isGreen ? ExitCodes.Green : ExitCodes.Violations;
    }

    /// <summary>
    /// File-set disclosure: runs the package's DesignGuardEmitProjection target and
    /// diffs the generator-facing file list against the CLI scope surface. Differences
    /// are visible, never assumed equal.
    /// </summary>
    private static void ReportProjectionDelta(string root, string projectPath, DesignGuardConfig config)
    {
        var projectionFile = Path.Combine(Path.GetTempPath(), $"designguard-projection-{Guid.NewGuid():N}.txt");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"msbuild \"{Path.GetFullPath(projectPath)}\" -t:DesignGuardEmitProjection -p:DesignGuardProjectionFile=\"{projectionFile}\" -nologo -v:q",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
            {
                Console.Error.WriteLine("  projection check: failed to start dotnet msbuild.");
                return;
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                Console.Error.WriteLine($"  projection check: msbuild exited {process.ExitCode} (target DesignGuardEmitProjection must exist — install WpfDesignGuard.Analyzers).");
                return;
            }

            if (!File.Exists(projectionFile))
            {
                Console.WriteLine("  projection check: generator-facing file list is empty (no Page/ApplicationDefinition items).");
                return;
            }

            var scanRoot = Path.GetFullPath(Path.Combine(root, config.Scope.Root));
            var projected = File.ReadAllLines(projectionFile)
                .Where(l => l.Trim().Length > 0)
                .Select(l => ToRelative(l, scanRoot))
                .ToHashSet(StringComparer.Ordinal);

            var cliFiles = WorkspaceScanner.ScanXamlFiles(scanRoot, config.Scope).ToHashSet(StringComparer.Ordinal);
            var generatorOnly = projected.Except(cliFiles, StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();
            var cliOnly = cliFiles.Except(projected, StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();

            Console.WriteLine($"  projection check: generator-facing {projected.Count} file(s) vs CLI surface {cliFiles.Count} file(s).");
            foreach (var f in generatorOnly)
            {
                Console.WriteLine($"    generator-only (compiled but outside scope): {f}");
            }

            foreach (var f in cliOnly)
            {
                Console.WriteLine($"    cli-only (in scope but not generator-facing): {f}");
            }

            if (generatorOnly.Count == 0 && cliOnly.Count == 0)
            {
                Console.WriteLine("    file sets match.");
            }
        }
        finally
        {
            try
            {
                if (File.Exists(projectionFile))
                {
                    File.Delete(projectionFile);
                }
            }
            catch
            {
                // best-effort temp cleanup
            }
        }
    }

    private static string ToRelative(string fullPath, string root)
    {
        var norm = fullPath.Replace('\\', '/').TrimEnd('/');
        var rootNorm = root.Replace('\\', '/').TrimEnd('/');
        return norm.StartsWith(rootNorm, StringComparison.Ordinal)
            ? norm.Substring(rootNorm.Length + 1)
            : norm;
    }
}
