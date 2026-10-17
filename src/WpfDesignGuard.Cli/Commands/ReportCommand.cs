using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using WpfDesignGuard.Core;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;
using WpfDesignGuard.Core.Workspace;

namespace WpfDesignGuard.Cli.Commands;

/// <summary>
/// `designguard report --format sarif`: full evaluation output as
/// SARIF 2.1.0 for GitHub Code Scanning, or a readable Markdown report.
/// Reporting is not a gate — exit codes stay 0 unless the configuration is invalid.
/// </summary>
internal static class ReportCommand
{
    public static int Run(string[] args)
    {
        var format = ArgumentParser.ValueOf(args, "--format") ?? "sarif";
        var root = ArgumentParser.ValueOf(args, "--root") ?? Directory.GetCurrentDirectory();
        var output = ArgumentParser.ValueOf(args, "--output");

        if (format is not ("sarif" or "markdown"))
        {
            Console.Error.WriteLine("designguard report: --format must be sarif or markdown.");
            return ExitCodes.ConfigError;
        }

        var configPath = Path.Combine(root, "designguard.json");
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"designguard report: no designguard.json at '{root}' — run `designguard init` first.");
            return ExitCodes.ConfigError;
        }

        var load = ConfigLoader.LoadFromText(File.ReadAllText(configPath));
        if (load.HasErrors || load.Config == null)
        {
            Console.Error.WriteLine("designguard report: designguard.json is invalid.");
            return ExitCodes.ConfigError;
        }

        var config = load.Config;
        var scanRoot = Path.GetFullPath(Path.Combine(root, config.Scope.Root));
        var files = WorkspaceScanner.ScanXamlFiles(scanRoot, config.Scope);
        var evaluation = InitCommand.EvaluateAll(scanRoot, files, config);
        var findingsByFile = evaluation.FindingsByFile;
        var outcome = GovernanceEvaluator.Evaluate(config, findingsByFile);

        var text = format == "sarif"
            ? WriteSarif(config, outcome)
            : WriteMarkdown(config, files.Count, outcome);

        if (output != null)
        {
            File.WriteAllText(output, text, new UTF8Encoding(false));
            Console.WriteLine($"designguard report: written to {output}");
        }
        else
        {
            Console.WriteLine(text);
        }

        return ExitCodes.Green;
    }

    internal static string WriteSarif(DesignGuardConfig config, VerificationOutcome outcome)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("$schema", "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/master/Schemata/sarif-schema-2.1.0.json");
            writer.WriteString("version", "2.1.0");
            writer.WriteStartArray("runs");
            writer.WriteStartObject();
            writer.WriteStartObject("tool");
            writer.WriteStartObject("driver");
            writer.WriteString("name", "WpfDesignGuard");
            writer.WriteString("informationUri", "https://zhaojie-tech.github.io/wpf-design-guard");
            writer.WriteString("semanticVersion", GuardVersion.Current);
            writer.WriteStartArray("rules");
            foreach (var rule in RuleCatalog.All)
            {
                writer.WriteStartObject();
                writer.WriteString("id", rule.Id);
                writer.WriteStartObject("properties");
                writer.WriteString("determinism", rule.Determinism.ToString());
                writer.WriteString("firstShippedIn", rule.FirstShippedIn);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();

            writer.WriteStartArray("results");
            foreach (var f in outcome.Findings)
            {
                writer.WriteStartObject();
                writer.WriteString("ruleId", f.Finding.RuleId);
                writer.WriteString("level", Level(config.SeverityOf(f.Finding.RuleId)));
                writer.WriteStartObject("message");
                writer.WriteString("text", f.Finding.Message);
                writer.WriteEndObject();
                writer.WriteStartArray("locations");
                writer.WriteStartObject();
                writer.WriteStartObject("physicalLocation");
                writer.WriteStartObject("artifactLocation");
                writer.WriteString("uri", f.Finding.File);
                writer.WriteEndObject();
                writer.WriteStartObject("region");
                writer.WriteNumber("startLine", f.Finding.Line);
                writer.WriteNumber("startColumn", f.Finding.Column);
                writer.WriteEndObject();
                writer.WriteEndObject(); // physicalLocation
                writer.WriteEndObject(); // location
                writer.WriteEndArray();  // locations
                writer.WriteStartObject("properties");
                writer.WriteString("disposition", f.Disposition.ToString());
                if (f.Exemption != null)
                {
                    writer.WriteString("exemptionReason", f.Exemption.Reason);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            foreach (var stale in outcome.StaleExemptions)
            {
                writer.WriteStartObject();
                writer.WriteString("ruleId", stale.Entry.Rule);
                writer.WriteString("level", "error");
                writer.WriteStartObject("message");
                writer.WriteString("text", $"Stale exemption on '{stale.Entry.File}': registered {stale.Entry.Count}, current hits {stale.ActualCount}");
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            foreach (var violation in outcome.RatchetViolations)
            {
                writer.WriteStartObject();
                writer.WriteString("ruleId", violation.RuleId);
                writer.WriteString("level", "error");
                writer.WriteStartObject("message");
                writer.WriteString("text", $"Ratchet breach: {violation.Actual} unexempted > baseline {violation.Allowed}");
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            foreach (var violation in outcome.PerFileViolations)
            {
                writer.WriteStartObject();
                writer.WriteString("ruleId", violation.RuleId);
                writer.WriteString("level", "error");
                writer.WriteStartObject("message");
                writer.WriteString("text", $"Per-file ratchet breach on '{violation.File}': {violation.Actual} unexempted > snapshot {violation.Allowed}");
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Level(RuleSeverity severity) => severity switch
    {
        RuleSeverity.Error => "error",
        RuleSeverity.Warning => "warning",
        _ => "note",
    };

    private static string WriteMarkdown(DesignGuardConfig config, int scannedFiles, VerificationOutcome outcome)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# WpfDesignGuard report");
        sb.AppendLine();
        sb.AppendLine($"- Files scanned: {scannedFiles}");
        sb.AppendLine($"- Findings: {outcome.Findings.Count} ({outcome.Findings.Count(f => f.Disposition == FindingDisposition.Exempted)} exempted, {outcome.Findings.Count(f => f.Disposition == FindingDisposition.HardZero)} hard-zero)");
        sb.AppendLine($"- Stale exemptions: {outcome.StaleExemptions.Count}; ratchet breaches: {outcome.RatchetViolations.Count}; per-file ratchet breaches: {outcome.PerFileViolations.Count}");
        sb.AppendLine();
        sb.AppendLine("| File | Line | Col | Rule | Disposition | Message |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var f in outcome.Findings.OrderBy(f => f.Finding.File, StringComparer.Ordinal).ThenBy(f => f.Finding.Line))
        {
            sb.AppendLine($"| {f.Finding.File} | {f.Finding.Line} | {f.Finding.Column} | {f.Finding.RuleId} | {f.Disposition} | {f.Finding.Message.Replace("|", "\\|")} |");
        }

        return sb.ToString();
    }
}
