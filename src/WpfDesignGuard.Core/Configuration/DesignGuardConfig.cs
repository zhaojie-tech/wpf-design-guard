using System;
using System.Collections.Generic;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Core.Configuration;

/// <summary>The whole designguard.json document. Single source of truth.</summary>
public sealed class DesignGuardConfig
{
    public DesignGuardConfig(
        string? schema,
        ScopeConfig scope,
        IReadOnlyDictionary<string, RuleConfig> rules,
        TokenTable tokens,
        IReadOnlyDictionary<string, IReadOnlyList<string>> hardZero,
        IReadOnlyList<ExemptionEntry> exemptions,
        BaselineSnapshot? baseline)
    {
        Schema = schema;
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        HardZero = hardZero ?? throw new ArgumentNullException(nameof(hardZero));
        Exemptions = exemptions ?? throw new ArgumentNullException(nameof(exemptions));
        Baseline = baseline;
    }

    public string? Schema { get; }

    public ScopeConfig Scope { get; }

    public IReadOnlyDictionary<string, RuleConfig> Rules { get; }

    public TokenTable Tokens { get; }

    /// <summary>Hard-zero domains: file glob -> rule IDs. Hits inside are always red.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> HardZero { get; }

    public IReadOnlyList<ExemptionEntry> Exemptions { get; }

    public BaselineSnapshot? Baseline { get; }

    /// <summary>Effective severity for a rule (catalog default when unlisted).</summary>
    public RuleSeverity SeverityOf(string ruleId)
    {
        return Rules.TryGetValue(ruleId, out var rc) && rc.Severity.HasValue
            ? rc.Severity.Value
            : RuleCatalog.TryGet(ruleId)?.DefaultSeverity ?? RuleSeverity.Error;
    }

    public bool IsEnabled(string ruleId)
    {
        return !Rules.TryGetValue(ruleId, out var rc) || rc.Enabled;
    }
}
