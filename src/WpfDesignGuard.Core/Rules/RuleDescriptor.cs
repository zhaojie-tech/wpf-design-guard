using System;
namespace WpfDesignGuard.Core.Rules;

public enum RuleSeverity
{
    Info,
    Warning,
    Error,
}

public enum RuleDeterminism
{
    Deterministic,
    Heuristic,
}

[Flags]
public enum RuleSurface
{
    None = 0,
    /// <summary>Runs in the XAML incremental generator (single-file view).</summary>
    Generator = 1,
    /// <summary>Runs as a classic C# Roslyn analyzer.</summary>
    CSharpAnalyzer = 2,
    /// <summary>Runs in the CLI only (whole-repo view).</summary>
    Cli = 4,
}

[Flags]
public enum RuleFixSupport
{
    None = 0,
    /// <summary>C# code fix provider shipped in the Analyzers package.</summary>
    CSharpCodeFix = 1,
    /// <summary>Span-level fix via `designguard fix`.</summary>
    CliFix = 2,
}

/// <summary>
/// Static metadata for one diagnostic rule. IDs are allocated once and never reused
/// Message formats are public API: changing text requires a CHANGELOG entry.
/// </summary>
public sealed class RuleDescriptor
{
    public RuleDescriptor(
        string id,
        string title,
        string messageFormat,
        RuleSeverity defaultSeverity,
        RuleDeterminism determinism,
        RuleSurface surfaces,
        RuleFixSupport fixSupport,
        string firstShippedIn,
        bool tokenDependent = false)
    {
        Id = id;
        Title = title;
        MessageFormat = messageFormat;
        DefaultSeverity = defaultSeverity;
        Determinism = determinism;
        Surfaces = surfaces;
        FixSupport = fixSupport;
        FirstShippedIn = firstShippedIn;
        TokenDependent = tokenDependent;
    }

    public string Id { get; }

    public string Title { get; }

    public string MessageFormat { get; }

    public RuleSeverity DefaultSeverity { get; }

    public RuleDeterminism Determinism { get; }

    public RuleSurface Surfaces { get; }

    public RuleFixSupport FixSupport { get; }

    /// <summary>Semantic version the rule first shipped in ("planned" for future rules).</summary>
    public string FirstShippedIn { get; }

    /// <summary>
    /// True when the rule's judgement requires a meaningful token table: without tokens
    /// configured it produces noise rather than signal. The init template turns these off
    /// until the user adopts tokens (ADR-0005); the engine's "unlisted = enabled" contract
    /// is untouched.
    /// </summary>
    public bool TokenDependent { get; }
}
