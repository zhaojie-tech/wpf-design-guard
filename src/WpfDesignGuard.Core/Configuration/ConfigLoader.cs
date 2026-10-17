using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using WpfDesignGuard.Core.Globbing;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Core.Configuration;

public sealed class ConfigError
{
    public ConfigError(string code, string message)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public string Code { get; }

    public string Message { get; }

    public override string ToString() => $"{Code}: {Message}";
}

public sealed class ConfigLoadResult
{
    public ConfigLoadResult(DesignGuardConfig? config, IReadOnlyList<ConfigError> errors)
    {
        Config = config;
        Errors = errors ?? throw new ArgumentNullException(nameof(errors));
    }

    public DesignGuardConfig? Config { get; }

    public IReadOnlyList<ConfigError> Errors { get; }

    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// Parses and validates designguard.json. Validation errors are collected (not fail-fast)
/// and reported together. All configuration closures are enforced here:
/// token-table floors, exemption validity (reason required, in-scope, outside hard-zero),
/// unknown rule IDs, baseline non-negative counters.
/// </summary>
public static class ConfigLoader
{
    private static readonly HashSet<string> KnownSeverities = new(StringComparer.OrdinalIgnoreCase)
    {
        "error", "warning", "info",
    };

    public static ConfigLoadResult LoadFromText(string jsoncText)
    {
        var errors = new List<ConfigError>();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(Jsonc.Strip(jsoncText));
        }
        catch (JsonException ex)
        {
            errors.Add(new ConfigError("DGCFG_PARSE", $"designguard.json is not valid JSON/JSONC: {ex.Message}"));
            return new ConfigLoadResult(null, errors);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new ConfigError("DGCFG_SHAPE", "designguard.json root must be an object."));
                return new ConfigLoadResult(null, errors);
            }

            var version = 0;
            if (root.TryGetProperty("version", out var versionEl))
            {
                if (versionEl.ValueKind == JsonValueKind.Number && versionEl.TryGetInt32(out var v))
                {
                    version = v;
                    if (v != 1)
                    {
                        errors.Add(new ConfigError("DGCFG_VERSION", $"Unsupported config version {v}; expected 1."));
                    }
                }
                else
                {
                    errors.Add(new ConfigError("DGCFG_VERSION", "'version' must be an integer."));
                }
            }
            else
            {
                errors.Add(new ConfigError("DGCFG_VERSION", "'version' is required (expected 1)."));
            }

            var schema = root.TryGetProperty("$schema", out var schemaEl) && schemaEl.ValueKind == JsonValueKind.String
                ? schemaEl.GetString()
                : null;

            var scope = ReadScope(root, errors);
            var rules = ReadRules(root, errors);
            var tokens = ReadTokens(root, errors);
            var hardZero = ReadHardZero(root, errors);
            var baseline = ReadBaseline(root, errors);
            var exemptions = ReadExemptions(root, errors);

            // Cross-section closures: exemption file must be inside the rule's
            // effective scope and must not point into a hard-zero domain.
            if (scope != null && hardZero != null && exemptions != null)
            {
                ValidateExemptions(exemptions, scope, hardZero, errors);
            }

            if (errors.Count > 0)
            {
                return new ConfigLoadResult(null, errors);
            }

            var config = new DesignGuardConfig(
                schema,
                scope!,
                rules!,
                tokens!,
                hardZero!,
                exemptions!,
                baseline);
            return new ConfigLoadResult(config, errors);
        }
    }

    // ---------------------------------------------------------------- scope

    private static ScopeConfig? ReadScope(JsonElement root, List<ConfigError> errors)
    {
        if (!root.TryGetProperty("scope", out var scopeEl))
        {
            return ScopeConfig.Default;
        }

        if (scopeEl.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ConfigError("DGCFG_SCOPE", "'scope' must be an object."));
            return null;
        }

        var rootPath = ScopeConfig.DefaultRoot;
        if (scopeEl.TryGetProperty("root", out var rootEl))
        {
            if (rootEl.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(rootEl.GetString()))
            {
                rootPath = GlobMatcher.Normalize(rootEl.GetString()!);
            }
            else
            {
                errors.Add(new ConfigError("DGCFG_SCOPE", "'scope.root' must be a non-empty string."));
            }
        }

        var include = ReadStringArray(scopeEl, "include", errors, required: true);
        var exclude = ReadStringArray(scopeEl, "exclude", errors, required: false) ?? new List<string>();
        if (include != null && include.Count == 0)
        {
            errors.Add(new ConfigError("DGCFG_SCOPE_EMPTY", "'scope.include' must list at least one pattern (empty scan surface is a configuration error)."));
        }

        var perRule = new Dictionary<string, RuleScopeOverride>(StringComparer.Ordinal);
        if (scopeEl.TryGetProperty("perRule", out var perRuleEl))
        {
            if (perRuleEl.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new ConfigError("DGCFG_SCOPE", "'scope.perRule' must be an object keyed by rule ID."));
            }
            else
            {
                foreach (var prop in perRuleEl.EnumerateObject())
                {
                    ValidateRuleId(prop.Name, $"scope.perRule.{prop.Name}", errors);
                    if (prop.Value.ValueKind != JsonValueKind.Object)
                    {
                        errors.Add(new ConfigError("DGCFG_SCOPE", $"scope.perRule.{prop.Name} must be an object with include/exclude arrays."));
                        continue;
                    }

                    var inc = ReadStringArray(prop.Value, "include", errors, required: false);
                    var exc = ReadStringArray(prop.Value, "exclude", errors, required: false);
                    perRule[prop.Name] = new RuleScopeOverride(inc, exc);
                }
            }
        }

        return new ScopeConfig(rootPath, include ?? new List<string>(), exclude, perRule);
    }

    // ---------------------------------------------------------------- rules

    private static Dictionary<string, RuleConfig>? ReadRules(JsonElement root, List<ConfigError> errors)
    {
        var rules = new Dictionary<string, RuleConfig>(StringComparer.Ordinal);
        if (!root.TryGetProperty("rules", out var rulesEl))
        {
            return rules;
        }

        if (rulesEl.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ConfigError("DGCFG_RULES", "'rules' must be an object keyed by rule ID."));
            return null;
        }

        foreach (var prop in rulesEl.EnumerateObject())
        {
            ValidateRuleId(prop.Name, $"rules.{prop.Name}", errors);
            if (prop.Value.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new ConfigError("DGCFG_RULES", $"rules.{prop.Name} must be an object."));
                continue;
            }

            RuleSeverity? severity = null;
            if (prop.Value.TryGetProperty("severity", out var sevEl))
            {
                if (sevEl.ValueKind == JsonValueKind.String && sevEl.GetString() is { } sev && KnownSeverities.Contains(sev))
                {
                    severity = (RuleSeverity)Enum.Parse(typeof(RuleSeverity), sev, ignoreCase: true);
                }
                else
                {
                    errors.Add(new ConfigError("DGCFG_RULES_SEVERITY", $"rules.{prop.Name}.severity must be one of error|warning|info."));
                }
            }

            var enabled = true;
            if (prop.Value.TryGetProperty("enabled", out var enEl))
            {
                if (enEl.ValueKind == JsonValueKind.True || enEl.ValueKind == JsonValueKind.False)
                {
                    enabled = enEl.GetBoolean();
                }
                else
                {
                    errors.Add(new ConfigError("DGCFG_RULES_ENABLED", $"rules.{prop.Name}.enabled must be a boolean."));
                }
            }

            rules[prop.Name] = new RuleConfig(severity, enabled);
        }

        return rules;
    }

    // ---------------------------------------------------------------- tokens

    private static TokenTable? ReadTokens(JsonElement root, List<ConfigError> errors)
    {
        if (!root.TryGetProperty("tokens", out var tokensEl))
        {
            return TokenTable.Default;
        }

        if (tokensEl.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ConfigError("DGCFG_TOKENS", "'tokens' must be an object."));
            return null;
        }

        var ladder = ReadNumberArray(tokensEl, "spacingLadder", errors) ?? TokenTable.Default.SpacingLadder;
        if (ladder.Count == 0)
        {
            errors.Add(new ConfigError("DGCFG_TOKENS_LADDER", "'tokens.spacingLadder' must not be empty."));
        }
        else
        {
            for (var i = 0; i < ladder.Count; i++)
            {
                if (ladder[i] < 0)
                {
                    errors.Add(new ConfigError("DGCFG_TOKENS_LADDER", $"tokens.spacingLadder[{i}]={ladder[i]} is negative."));
                }
                else if (i > 0 && ladder[i] <= ladder[i - 1])
                {
                    errors.Add(new ConfigError("DGCFG_TOKENS_LADDER", "tokens.spacingLadder must be strictly ascending (it is a ladder)."));
                    break;
                }
            }
        }

        var fontSizes = ReadNumberMap(tokensEl, "fontSizes", errors) ?? TokenTable.Default.FontSizes;
        if (fontSizes.TryGetValue("Body", out var body))
        {
            if (body < TokenTable.MinBodyFontSize)
            {
                errors.Add(new ConfigError("DGCFG_TOKENS_FLOOR", $"tokens.fontSizes.Body={body} is below the floor {TokenTable.MinBodyFontSize} (legibility contract)."));
            }
        }
        else
        {
            errors.Add(new ConfigError("DGCFG_TOKENS_FLOOR", "tokens.fontSizes must define a 'Body' entry (floor anchor)."));
        }

        var floor = TokenTable.Default.HitTargetFloor;
        if (tokensEl.TryGetProperty("hitTargetFloor", out var floorEl))
        {
            if (floorEl.ValueKind == JsonValueKind.Number && floorEl.TryGetDouble(out var f))
            {
                floor = f;
                if (f < TokenTable.MinHitTargetFloor)
                {
                    errors.Add(new ConfigError("DGCFG_TOKENS_FLOOR", $"tokens.hitTargetFloor={f} is below the floor {TokenTable.MinHitTargetFloor} (touch accessibility contract)."));
                }
            }
            else
            {
                errors.Add(new ConfigError("DGCFG_TOKENS", "'tokens.hitTargetFloor' must be a number."));
            }
        }

        var uniformKeys = ReadStringArray(tokensEl, "uniformSpacingKeys", errors, required: false) ?? TokenTable.Default.UniformSpacingKeys;
        var lineHeights = ReadNumberMap(tokensEl, "lineHeights", errors) ?? TokenTable.Default.LineHeights;
        var clickable = ReadStringArray(tokensEl, "clickableTypes", errors, required: false) ?? TokenTable.Default.ClickableTypes;
        var inlineInputs = ReadStringArray(tokensEl, "inlineInputTypes", errors, required: false) ?? TokenTable.Default.InlineInputTypes;
        string? styleLibrary = null;
        if (tokensEl.TryGetProperty("styleLibrary", out var libEl))
        {
            if (libEl.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(libEl.GetString()))
            {
                styleLibrary = GlobMatcher.Normalize(libEl.GetString()!);
            }
            else
            {
                errors.Add(new ConfigError("DGCFG_TOKENS", "'tokens.styleLibrary' must be a non-empty path glob."));
            }
        }

        return new TokenTable(ladder, fontSizes, floor, uniformKeys, lineHeights, clickable, inlineInputs, styleLibrary);
    }

    // ---------------------------------------------------------------- hard-zero

    private static Dictionary<string, IReadOnlyList<string>>? ReadHardZero(JsonElement root, List<ConfigError> errors)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (!root.TryGetProperty("hardZero", out var hzEl))
        {
            return result;
        }

        if (hzEl.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ConfigError("DGCFG_HARDZERO", "'hardZero' must be an object mapping file globs to rule ID arrays."));
            return null;
        }

        foreach (var prop in hzEl.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Array)
            {
                errors.Add(new ConfigError("DGCFG_HARDZERO", $"hardZero['{prop.Name}'] must be an array of rule IDs."));
                continue;
            }

            var ids = new List<string>();
            foreach (var item in prop.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } id)
                {
                    ValidateRuleId(id, $"hardZero['{prop.Name}']", errors);
                    ids.Add(id);
                }
                else
                {
                    errors.Add(new ConfigError("DGCFG_HARDZERO", $"hardZero['{prop.Name}'] contains a non-string entry."));
                }
            }

            result[GlobMatcher.Normalize(prop.Name)] = ids;
        }

        return result;
    }

    // ---------------------------------------------------------------- baseline

    private static BaselineSnapshot? ReadBaseline(JsonElement root, List<ConfigError> errors)
    {
        if (!root.TryGetProperty("baseline", out var blEl))
        {
            return null;
        }

        if (blEl.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ConfigError("DGCFG_BASELINE", "'baseline' must be an object."));
            return null;
        }

        string? date = null;
        if (blEl.TryGetProperty("date", out var dateEl))
        {
            if (dateEl.ValueKind == JsonValueKind.String)
            {
                date = dateEl.GetString();
            }
            else
            {
                errors.Add(new ConfigError("DGCFG_BASELINE", "'baseline.date' must be an ISO date string."));
            }
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (blEl.TryGetProperty("counts", out var countsEl))
        {
            if (countsEl.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new ConfigError("DGCFG_BASELINE", "'baseline.counts' must be an object keyed by rule ID."));
            }
            else
            {
                foreach (var prop in countsEl.EnumerateObject())
                {
                    ValidateRuleId(prop.Name, $"baseline.counts.{prop.Name}", errors);
                    if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var n))
                    {
                        if (n < 0)
                        {
                            errors.Add(new ConfigError("DGCFG_BASELINE", $"baseline.counts.{prop.Name}={n} is negative."));
                        }

                        counts[prop.Name] = n;
                    }
                    else
                    {
                        errors.Add(new ConfigError("DGCFG_BASELINE", $"baseline.counts.{prop.Name} must be a non-negative integer."));
                    }
                }
            }
        }

        var files = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal);
        if (blEl.TryGetProperty("files", out var filesEl))
        {
            if (filesEl.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new ConfigError("DGCFG_BASELINE", "'baseline.files' must be an object keyed by file path."));
            }
            else
            {
                foreach (var fileProp in filesEl.EnumerateObject())
                {
                    if (fileProp.Value.ValueKind != JsonValueKind.Object)
                    {
                        errors.Add(new ConfigError("DGCFG_BASELINE", $"baseline.files['{fileProp.Name}'] must be an object keyed by rule ID."));
                        continue;
                    }

                    var perFile = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var ruleProp in fileProp.Value.EnumerateObject())
                    {
                        ValidateRuleId(ruleProp.Name, $"baseline.files['{fileProp.Name}'].{ruleProp.Name}", errors);
                        if (ruleProp.Value.ValueKind == JsonValueKind.Number && ruleProp.Value.TryGetInt32(out var n))
                        {
                            if (n < 0)
                            {
                                errors.Add(new ConfigError("DGCFG_BASELINE", $"baseline.files['{fileProp.Name}'].{ruleProp.Name}={n} is negative."));
                            }

                            perFile[ruleProp.Name] = n;
                        }
                        else
                        {
                            errors.Add(new ConfigError("DGCFG_BASELINE", $"baseline.files['{fileProp.Name}'].{ruleProp.Name} must be a non-negative integer."));
                        }
                    }

                    files[GlobMatcher.Normalize(fileProp.Name)] = perFile;
                }
            }
        }

        return new BaselineSnapshot(date, counts, files);
    }

    // ---------------------------------------------------------------- exemptions

    private static List<ExemptionEntry>? ReadExemptions(JsonElement root, List<ConfigError> errors)
    {
        var result = new List<ExemptionEntry>();
        if (!root.TryGetProperty("exemptions", out var exEl))
        {
            return result;
        }

        if (exEl.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new ConfigError("DGCFG_EXEMPTIONS", "'exemptions' must be an array."));
            return null;
        }

        var index = 0;
        foreach (var item in exEl.EnumerateArray())
        {
            var at = $"exemptions[{index}]";
            index++;

            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new ConfigError("DGCFG_EXEMPTIONS", $"{at} must be an object."));
                continue;
            }

            string? rule = null;
            string? file = null;
            string? reason = null;
            string? type = null;
            var count = -1;

            if (item.TryGetProperty("rule", out var ruleEl) && ruleEl.ValueKind == JsonValueKind.String)
            {
                rule = ruleEl.GetString();
                ValidateRuleId(rule!, $"{at}.rule", errors);
            }
            else
            {
                errors.Add(new ConfigError("DGCFG_EXEMPTIONS", $"{at}.rule is required."));
            }

            if (item.TryGetProperty("file", out var fileEl) && fileEl.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(fileEl.GetString()))
            {
                file = GlobMatcher.Normalize(fileEl.GetString()!);
            }
            else
            {
                errors.Add(new ConfigError("DGCFG_EXEMPTIONS", $"{at}.file is required (per-file registration; directory-level exemptions are forbidden)."));
            }

            if (item.TryGetProperty("count", out var countEl) && countEl.ValueKind == JsonValueKind.Number && countEl.TryGetInt32(out var c))
            {
                count = c;
                if (c < 1)
                {
                    errors.Add(new ConfigError("DGCFG_EXEMPTIONS", $"{at}.count={c} must be >= 1 (exemptions are per-occurrence)."));
                }
            }
            else
            {
                errors.Add(new ConfigError("DGCFG_EXEMPTIONS", $"{at}.count is required and must be a positive integer."));
            }

            if (item.TryGetProperty("reason", out var reasonEl) && reasonEl.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(reasonEl.GetString()))
            {
                reason = reasonEl.GetString();
            }
            else
            {
                errors.Add(new ConfigError("DGCFG_EXEMPTIONS_REASON", $"{at}.reason is required and must be non-empty (empty reason is a configuration error)."));
            }

            if (item.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(typeEl.GetString()))
            {
                type = typeEl.GetString();
            }

            if (rule != null && file != null && reason != null && count >= 1)
            {
                result.Add(new ExemptionEntry(rule, file, count, reason, type));
            }
        }

        return result;
    }

    private static void ValidateExemptions(
        List<ExemptionEntry> exemptions,
        ScopeConfig scope,
        Dictionary<string, IReadOnlyList<string>> hardZero,
        List<ConfigError> errors)
    {
        foreach (var entry in exemptions)
        {
            var effective = EffectiveScope.Resolve(scope, entry.Rule);
            if (!effective.Matches(entry.File))
            {
                errors.Add(new ConfigError(
                    "DGCFG_EXEMPTIONS_SCOPE",
                    $"exemption for {entry.Rule} points at '{entry.File}' which is outside the rule's scan scope — either widen scope or remove the exemption."));
            }

            foreach (var domain in hardZero)
            {
                var domainMatcher = new GlobMatcher(domain.Key);
                if (domainMatcher.IsMatch(entry.File) && domain.Value.Contains(entry.Rule))
                {
                    errors.Add(new ConfigError(
                        "DGCFG_EXEMPTIONS_HARDZERO",
                        $"exemption for {entry.Rule} points at '{entry.File}' inside hard-zero domain '{domain.Key}' — the two mechanisms are mutually exclusive: fix the occurrence or move the file out of the domain."));
                }
            }
        }
    }

    // ---------------------------------------------------------------- helpers

    private static void ValidateRuleId(string id, string at, List<ConfigError> errors)
    {
        if (RuleCatalog.TryGet(id) == null)
        {
            errors.Add(new ConfigError("DGCFG_RULE_UNKNOWN", $"Unknown rule ID '{id}' at '{at}' (IDs are allocated in the rule catalog; check for typos)."));
        }
    }

    private static List<string>? ReadStringArray(JsonElement parent, string name, List<ConfigError> errors, bool required)
    {
        if (!parent.TryGetProperty(name, out var el))
        {
            if (required)
            {
                errors.Add(new ConfigError("DGCFG_SHAPE", $"'{name}' is required."));
            }

            return null;
        }

        if (el.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new ConfigError("DGCFG_SHAPE", $"'{name}' must be an array of strings."));
            return null;
        }

        var list = new List<string>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            {
                list.Add(GlobMatcher.Normalize(item.GetString()!));
            }
            else
            {
                errors.Add(new ConfigError("DGCFG_SHAPE", $"'{name}' contains a non-string or empty entry."));
            }
        }

        return list;
    }

    private static IReadOnlyList<double>? ReadNumberArray(JsonElement parent, string name, List<ConfigError> errors)
    {
        if (!parent.TryGetProperty(name, out var el))
        {
            return null;
        }

        if (el.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new ConfigError("DGCFG_SHAPE", $"'{name}' must be an array of numbers."));
            return null;
        }

        var list = new List<double>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var d))
            {
                list.Add(d);
            }
            else
            {
                errors.Add(new ConfigError("DGCFG_SHAPE", $"'{name}' contains a non-number entry."));
            }
        }

        return list;
    }

    private static IReadOnlyDictionary<string, double>? ReadNumberMap(JsonElement parent, string name, List<ConfigError> errors)
    {
        if (!parent.TryGetProperty(name, out var el))
        {
            return null;
        }

        if (el.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ConfigError("DGCFG_SHAPE", $"'{name}' must be an object mapping keys to numbers."));
            return null;
        }

        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var prop in el.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out var d))
            {
                map[prop.Name] = d;
            }
            else
            {
                errors.Add(new ConfigError("DGCFG_SHAPE", $"'{name}.{prop.Name}' must be a number."));
            }
        }

        return map;
    }
}
