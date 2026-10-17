using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WpfDesignGuard.Core.Configuration;

/// <summary>
/// Serializes a configuration back to JSONC text (used by `designguard init` and
/// `baseline --update`). Output is deterministic and review-friendly: stable key
/// order, '/'-normalized paths, sorted per-file snapshots.
/// </summary>
public static class ConfigWriter
{
    public const string DefaultSchemaUrl = "https://zhaojie-tech.github.io/wpf-design-guard/schema/v1/designguard.schema.json";

    public static string Write(DesignGuardConfig config, string? schemaUrl = null)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.AppendLine($"  \"$schema\": \"{JsonEscape(config.Schema ?? schemaUrl ?? DefaultSchemaUrl)}\",");
        sb.AppendLine("  \"version\": 1,");
        sb.AppendLine("  \"scope\": {");
        sb.AppendLine($"    \"root\": \"{JsonEscape(config.Scope.Root)}\",");
        sb.AppendLine($"    \"include\": {Array(config.Scope.Include)},");
        sb.AppendLine($"    \"exclude\": {Array(config.Scope.Exclude)},");
        if (config.Scope.PerRule.Count > 0)
        {
            sb.AppendLine("    \"perRule\": {");
            var perRule = config.Scope.PerRule.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
            for (var i = 0; i < perRule.Count; i++)
            {
                var kv = perRule[i];
                var parts = new List<string>();
                if (kv.Value.Include != null)
                {
                    parts.Add($"\"include\": {Array(kv.Value.Include)}");
                }

                if (kv.Value.Exclude != null)
                {
                    parts.Add($"\"exclude\": {Array(kv.Value.Exclude)}");
                }

                var comma = i < perRule.Count - 1 ? "," : string.Empty;
                sb.AppendLine($"      \"{JsonEscape(kv.Key)}\": {{ {string.Join(", ", parts)} }}{comma}");
            }

            sb.AppendLine("    }");
        }
        else
        {
            sb.Append("    \"perRule\": {}\n");
        }

        sb.Append("  },\n");

        // rules: only non-default entries matter; persist what was configured.
        sb.AppendLine("  \"rules\": {");
        var rules = config.Rules.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        for (var i = 0; i < rules.Count; i++)
        {
            var kv = rules[i];
            var parts = new List<string>();
            if (kv.Value.Severity.HasValue)
            {
                parts.Add($"\"severity\": \"{kv.Value.Severity.Value.ToString().ToLowerInvariant()}\"");
            }

            parts.Add($"\"enabled\": {(kv.Value.Enabled ? "true" : "false")}");
            var comma = i < rules.Count - 1 ? "," : string.Empty;
            sb.AppendLine($"    \"{JsonEscape(kv.Key)}\": {{ {string.Join(", ", parts)} }}{comma}");
        }

        sb.Append("  },\n");

        sb.AppendLine("  \"tokens\": {");
        sb.AppendLine($"    \"spacingLadder\": [{string.Join(", ", config.Tokens.SpacingLadder.Select(v => FormatNumber(v)))}],");
        var fontSizes = config.Tokens.FontSizes.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        sb.AppendLine($"    \"fontSizes\": {{ {string.Join(", ", fontSizes.Select(kv => $"\"{JsonEscape(kv.Key)}\": {FormatNumber(kv.Value)}"))} }},");
        sb.AppendLine($"    \"hitTargetFloor\": {FormatNumber(config.Tokens.HitTargetFloor)},");
        sb.AppendLine($"    \"uniformSpacingKeys\": {Array(config.Tokens.UniformSpacingKeys)},");
        var lineHeights = config.Tokens.LineHeights.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        sb.AppendLine($"    \"lineHeights\": {{ {string.Join(", ", lineHeights.Select(kv => $"\"{JsonEscape(kv.Key)}\": {FormatNumber(kv.Value)}"))} }},");
        sb.AppendLine($"    \"clickableTypes\": {Array(config.Tokens.ClickableTypes)},");
        sb.AppendLine($"    \"inlineInputTypes\": {Array(config.Tokens.InlineInputTypes)}");
        if (config.Tokens.StyleLibrary != null)
        {
            sb.AppendLine($"    ,\"styleLibrary\": \"{JsonEscape(config.Tokens.StyleLibrary)}\"");
        }

        sb.Append("  },\n");

        sb.AppendLine("  \"hardZero\": {");
        var hz = config.HardZero.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        for (var i = 0; i < hz.Count; i++)
        {
            var comma = i < hz.Count - 1 ? "," : string.Empty;
            sb.AppendLine($"    \"{JsonEscape(hz[i].Key)}\": {Array(hz[i].Value)}{comma}");
        }

        sb.Append("  },\n");

        sb.AppendLine("  \"exemptions\": [");
        for (var i = 0; i < config.Exemptions.Count; i++)
        {
            var e = config.Exemptions[i];
            var comma = i < config.Exemptions.Count - 1 ? "," : string.Empty;
            var type = e.Type != null ? $", \"type\": \"{JsonEscape(e.Type)}\"" : string.Empty;
            sb.AppendLine($"    {{ \"rule\": \"{JsonEscape(e.Rule)}\", \"file\": \"{JsonEscape(e.File)}\", \"count\": {e.Count}, \"reason\": \"{JsonEscape(e.Reason)}\"{type} }}{comma}");
        }

        sb.Append("  ],\n");

        if (config.Baseline != null)
        {
            sb.AppendLine("  \"baseline\": {");
            sb.AppendLine($"    \"date\": \"{JsonEscape(config.Baseline.Date ?? string.Empty)}\",");
            sb.AppendLine("    \"counts\": {");
            var counts = config.Baseline.Counts.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
            for (var i = 0; i < counts.Count; i++)
            {
                var comma = i < counts.Count - 1 ? "," : string.Empty;
                sb.AppendLine($"      \"{JsonEscape(counts[i].Key)}\": {counts[i].Value}{comma}");
            }

            sb.Append("    },\n");
            sb.AppendLine("    \"files\": {");
            var files = config.Baseline.Files.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
            for (var i = 0; i < files.Count; i++)
            {
                var fileComma = i < files.Count - 1 ? "," : string.Empty;
                var inner = string.Join(", ", files[i].Value.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"\"{JsonEscape(kv.Key)}\": {kv.Value}"));
                sb.AppendLine($"      \"{JsonEscape(files[i].Key)}\": {{ {inner} }}{fileComma}");
            }

            sb.Append("    }\n");
            sb.Append("  }\n");
        }

        sb.Append("}\n");
        return sb.ToString();
    }

    private static string Array(IEnumerable<string> values) =>
        "[" + string.Join(", ", values.Select(v => $"\"{JsonEscape(v)}\"")) + "]";

    private static string FormatNumber(double v) =>
        v == Math.Floor(v) && !double.IsInfinity(v)
            ? ((long)v).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string JsonEscape(string s) =>
        System.Text.Json.JsonEncodedText.Encode(s).ToString();
}
