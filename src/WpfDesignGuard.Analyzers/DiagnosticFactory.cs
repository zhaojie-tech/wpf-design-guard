using System;
using Microsoft.CodeAnalysis;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Analyzers;

/// <summary>
/// Diagnostic creation shared by the XAML generator and the C# analyzers.
/// Diagnostic IDs are the rule IDs (DGxxxx) — they are public API: never reused, text
/// changes are breaking-ish (recorded in CHANGELOG).
/// </summary>
internal static class DiagnosticFactory
{
    public const string Category = "WpfDesignGuard";

    /// <summary>
    /// Builds a rule diagnostic. <paramref name="formattedMessage"/> was formatted by Core
    /// from the same <see cref="RuleDescriptor.MessageFormat"/> (single source — no drift);
    /// the explicit severity implements the cold-start downgrade.
    /// </summary>
    public static Diagnostic Create(RuleDescriptor rule, DiagnosticSeverity severity, string formattedMessage, Location location)
    {
        if (rule is null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        if (formattedMessage is null)
        {
            throw new ArgumentNullException(nameof(formattedMessage));
        }

        return Diagnostic.Create(
            id: rule.Id,
            category: Category,
            message: formattedMessage,
            severity: severity,
            defaultSeverity: severity,
            isEnabledByDefault: true,
            warningLevel: severity == DiagnosticSeverity.Error ? 0 : 1,
            title: rule.Title,
            location: location);
    }

    /// <summary>Generator-level operational warning (config missing/invalid, unparseable XAML).</summary>
    public static Diagnostic GeneratorWarning(string id, string message, Location? location = null)
    {
        return Diagnostic.Create(
            id: id,
            category: Category,
            message: message,
            severity: DiagnosticSeverity.Warning,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            warningLevel: 1,
            location: location ?? Location.None);
    }
}

/// <summary>Generator diagnostic IDs (not rule diagnostics).</summary>
internal static class GeneratorDiagnosticIds
{
    /// <summary>designguard.json missing — generator self-disabled, build continues.</summary>
    public const string ConfigMissing = "DGCFG001";

    /// <summary>designguard.json invalid — generator self-disabled, CLI verify exits 1.</summary>
    public const string ConfigInvalid = "DGCFG002";

    /// <summary>XAML file could not be parsed as XML.</summary>
    public const string XamlParseError = "DGCFG003";
}
