using System;
using System.Collections.Generic;
using System.Linq;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;
using WpfDesignGuard.Core.Xaml;

namespace WpfDesignGuard.Core.RepoRules;

/// <summary>Whole-workspace input: scope-relative path to raw text, plus the configuration.</summary>
public sealed class RepoContext
{
    public RepoContext(IReadOnlyDictionary<string, string> files, DesignGuardConfig config)
    {
        Files = files ?? throw new ArgumentNullException(nameof(files));
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public IReadOnlyDictionary<string, string> Files { get; }

    public DesignGuardConfig Config { get; }

    /// <summary>Blind spots the rules could not judge; surfaced by the CLI, never silent.</summary>
    public List<string> Unchecked { get; } = new();

    public XamlIndex? TryParse(string path)
    {
        if (!Files.TryGetValue(path, out var text))
        {
            return null;
        }

        try
        {
            return XamlIndex.Parse(text);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}

/// <summary>Base class of the CLI-only whole-repo rules (cross-file judgement).</summary>
public abstract class RepoRule
{
    protected RepoRule(RuleDescriptor descriptor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    public RuleDescriptor Descriptor { get; }

    public string Id => Descriptor.Id;

    public abstract IReadOnlyList<RuleFinding> Evaluate(RepoContext context);
}

public sealed class RepoEvaluation
{
    public List<RuleFinding> Findings { get; } = new();

    /// <summary>Blind spots surface here (never silent, never a false positive).</summary>
    public List<string> Unchecked { get; } = new();
}

/// <summary>
/// The whole-repo rule pass (DG0014 implicit-style chain, DG0017 coverage gate).
/// These rules need cross-file context and therefore run on the CLI surface only;
/// the generator surface never reports them (single-file approximation would drift).
/// </summary>
public static class RepoRuleEngine
{
    public static IReadOnlyList<RepoRule> CreateRules() => new RepoRule[]
    {
        new ImplicitTextBlockStyleRule(),
        new StyleLibraryCoverageRule(),
    };

    public static RepoEvaluation Evaluate(RepoContext context)
    {
        var result = new RepoEvaluation();
        foreach (var rule in CreateRules())
        {
            if (!context.Config.IsEnabled(rule.Id) ||
                !RuleCatalog.IsImplementedAt(rule.Descriptor, GuardVersion.Current))
            {
                continue;
            }

            result.Findings.AddRange(rule.Evaluate(context));
        }

        result.Unchecked.AddRange(context.Unchecked);
        return result;
    }
}
