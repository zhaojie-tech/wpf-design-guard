using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using WpfDesignGuard.Core;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;
using WpfDesignGuard.Core.Xaml;

namespace WpfDesignGuard.Analyzers;

/// <summary>
/// Parse-call counter: the cache-behaviour tests assert that re-running the generator
/// over an unchanged additional text performs zero new parses, and that changing one
/// file re-parses exactly that one file.
/// </summary>
internal sealed class ParseTelemetry
{
    private int _xamlParseCount;

    internal void Reset() => Interlocked.Exchange(ref _xamlParseCount, 0);

    internal void Increment() => Interlocked.Increment(ref _xamlParseCount);

    internal int Current => Volatile.Read(ref _xamlParseCount);
}

/// <summary>
/// The XAML guard as an incremental generator: consumes the @(Page)/@(ApplicationDefinition)
/// projection injected by the package targets as AdditionalFiles plus the injected
/// designguard.json, evaluates the shared Core rule engine per file, and reports
/// diagnostics on the XAML additional texts (Error List / Problems panel + build errors).
/// Cold start: files within their baseline per-file snapshot downgrade to Warnings
/// config missing/invalid self-disables the generator with a build warning.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class XamlDesignGenerator : IIncrementalGenerator
{
    public const string ConfigFileName = "designguard.json";

    private readonly ParseTelemetry _telemetry = new();
    private readonly SymbolQueryTelemetry _symbolTelemetry = new();
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Compilation, Dictionary<string, string>> _clrNamespaceMap =
        new();

    internal ParseTelemetry Telemetry => _telemetry;

    internal SymbolQueryTelemetry SymbolTelemetry => _symbolTelemetry;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Config: exactly the injected designguard.json (single node, so any config text
        // change invalidates the whole pipeline — correct, tokens/ledger are global).
        var configProvider = context.AdditionalTextsProvider
            .Where(static at => IsConfigFile(at.Path))
            .Select(static (at, ct) => (Path: at.Path, Text: at.GetText(ct)?.ToString()))
            .Collect()
            .Select(static (list, ct) => list.Length > 0 ? list[0] : ((string Path, string? Text)?)null);

        // One operational warning per config state (missing/invalid self-disable).
        context.RegisterSourceOutput(configProvider, static (spc, config) => ReportConfigStatus(spc, config));

        // Per-file pipeline: tuple equality on (path, SourceText-reference) is the cache key
        // (cache key = additional-text identity) — unchanged files re-emit with zero re-parse.
        var xamlProvider = context.AdditionalTextsProvider
            .Where(static at => at.Path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Select(static (at, ct) => (Path: at.Path, Text: at.GetText(ct)));

        context.RegisterSourceOutput(
            xamlProvider.Combine(configProvider),
            (spc, data) => EvaluateFile(spc, data.Left.Path, data.Left.Text, data.Right));

        // DG0012 two-stage pipeline: this stage is the cheap text stage — specs are
        // cached with the file text. The stage below runs against the Compilation.
        var setterSpecsProvider = xamlProvider
            .Select(static (data, ct) => CollectSetterSpecs(data));

        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(setterSpecsProvider.Collect().Combine(configProvider)),
            (spc, data) => CheckSetters(spc, data.Left, data.Right.Left, data.Right.Right));
    }

    private static (string Path, ImmutableArray<SpecWithSpan> Specs, ImmutableKeyValuePair<string, string>[] ClrNamespaces) CollectSetterSpecs((string Path, SourceText? Text) input)
    {
        if (input.Text == null)
        {
            return (input.Path, ImmutableArray<SpecWithSpan>.Empty, Array.Empty<ImmutableKeyValuePair<string, string>>());
        }

        try
        {
            var index = XamlIndex.Parse(input.Text.ToString());
            var builder = ImmutableArray.CreateBuilder<SpecWithSpan>();
            foreach (var spec in SetterSpecCollector.Collect(index))
            {
                var offset = index.OffsetOf(spec.Line, spec.Column);
                var end = Math.Min(offset + Math.Max(1, spec.Length), index.Text.Length);
                builder.Add(new SpecWithSpan(
                    spec, input.Path,
                    TextSpan.FromBounds(offset, end),
                    new LinePositionSpan(ToLinePosition(index.PositionOf(offset)), ToLinePosition(index.PositionOf(end)))));
            }

            var ns = SetterExistenceChecker.ParseClrNamespaceDeclarations(input.Text.ToString());
            var pairs = ns.Select(kv => new ImmutableKeyValuePair<string, string>(kv.Key, kv.Value)).ToArray();
            return (input.Path, builder.ToImmutable(), pairs);
        }
        catch (System.Xml.XmlException)
        {
            return (input.Path, ImmutableArray<SpecWithSpan>.Empty, Array.Empty<ImmutableKeyValuePair<string, string>>());
        }
    }

    /// <summary>A pending Setter plus the source span needed for a clickable diagnostic.</summary>
    internal readonly record struct SpecWithSpan(
        Core.Xaml.SetterSpec Spec,
        string Path,
        TextSpan Span,
        LinePositionSpan LineSpan);

    /// <summary>netstandard2.0 has no KeyValuePair-friendly tuple equality in the pipeline; a tiny pair keeps the node comparable.</summary>
    internal readonly struct ImmutableKeyValuePair<TKey, TValue>
    {
        public ImmutableKeyValuePair(TKey key, TValue value)
        {
            Key = key;
            Value = value;
        }

        public TKey Key { get; }

        public TValue Value { get; }
    }

    private void CheckSetters(
        SourceProductionContext spc,
        Compilation compilation,
        ImmutableArray<(string Path, ImmutableArray<SpecWithSpan> Specs, ImmutableKeyValuePair<string, string>[] ClrNamespaces)> specGroups,
        (string Path, string? Text)? config)
    {
        if (config == null || config.Value.Text == null)
        {
            return;
        }

        var loadResult = ConfigLoader.LoadFromText(config.Value.Text);
        if (loadResult.HasErrors || loadResult.Config == null)
        {
            return; // config invalid: generator self-disabled (warning already reported)
        }

        var configData = loadResult.Config;
        if (!configData.IsEnabled("DG0012"))
        {
            return;
        }

        var checker = ObtainChecker(compilation, specGroups.Select(g => g.ClrNamespaces));


        foreach (var group in specGroups)
        {
            foreach (var entry in group.Specs)
            {
                var result = checker.Check(entry.Spec);
                if (result != SetterCheckResult.Missing)
                {
                    continue; // Exists or Unchecked (blind spot, never a false positive)
                }

                var descriptor = RuleCatalog.TryGet("DG0012")!;
                var spec = entry.Spec;
                var displayed = spec.IsAttached
                    ? spec.PropertyPrefix + "." + spec.PropertyName
                    : spec.PropertyName;
                spc.ReportDiagnostic(DiagnosticFactory.Create(
                    descriptor,
                    DiagnosticSeverity.Error,
                    string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        descriptor.MessageFormat,
                        displayed,
                        spec.TypePrefix.Length > 0 ? spec.TypePrefix + ":" + spec.TypeName : spec.TypeName),
                    Location.Create(entry.Path, entry.Span, entry.LineSpan)));
            }
        }
    }

    /// <summary>
    /// The memo lives per compilation : the same compilation reuses type-resolutions
    /// across files, so the symbol-query counter only moves on genuinely new names.
    /// clr-namespace declarations accumulate across the solution's XAML files.
    /// </summary>
    private SetterExistenceChecker ObtainChecker(
        Compilation compilation, IEnumerable<ImmutableKeyValuePair<string, string>[]> declarations)
    {
        lock (_clrNamespaceMap)
        {
            if (!_clrNamespaceMap.TryGetValue(compilation, out var map))
            {
                map = new Dictionary<string, string>(StringComparer.Ordinal);
                _clrNamespaceMap.Add(compilation, map);
            }

            foreach (var group in declarations)
            {
                foreach (var pair in group)
                {
                    map[pair.Key] = pair.Value;
                }
            }

            return new SetterExistenceChecker(compilation, map, _symbolTelemetry);
        }
    }

    internal static bool IsConfigFile(string path)
    {
        var name = path;
        var slash = name.LastIndexOf('/');
        if (slash >= 0)
        {
            name = name.Substring(slash + 1);
        }

        var backslash = name.LastIndexOf('\\');
        if (backslash >= 0)
        {
            name = name.Substring(backslash + 1);
        }

        return name.Equals(ConfigFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static void ReportConfigStatus(
        SourceProductionContext spc,
        (string Path, string? Text)? config)
    {
        if (config == null || config.Value.Text == null)
        {
            spc.ReportDiagnostic(DiagnosticFactory.GeneratorWarning(
                GeneratorDiagnosticIds.ConfigMissing,
                $"No {ConfigFileName} found in the injected additional files — WpfDesignGuard XAML analysis is disabled for this project " +
                "(run `designguard init` to create a configuration; the CLI verifies independently)."));
            return;
        }

        var result = ConfigLoader.LoadFromText(config.Value.Text);
        if (result.HasErrors)
        {
            spc.ReportDiagnostic(DiagnosticFactory.GeneratorWarning(
                GeneratorDiagnosticIds.ConfigInvalid,
                $"{ConfigFileName} is invalid — WpfDesignGuard XAML analysis is disabled for this project. Errors: " +
                string.Join(" | ", result.Errors.Select(e => e.ToString()))));
        }
    }

    private void EvaluateFile(
        SourceProductionContext spc,
        string xamlPath,
        SourceText? text,
        (string Path, string? Text)? config)
    {
        if (text == null || config == null || config.Value.Text == null)
        {
            return; // config missing/invalid: generator self-disabled (warning already reported)
        }

        var loadResult = ConfigLoader.LoadFromText(config.Value.Text);
        if (loadResult.HasErrors || loadResult.Config == null)
        {
            return;
        }

        var relativePath = ComputeRelativePath(config.Value.Path, xamlPath);

        _telemetry.Increment();

        var evaluation = XamlRuleEngine.Evaluate(text.ToString(), relativePath, loadResult.Config);

        if (!evaluation.ParseSucceeded)
        {
            spc.ReportDiagnostic(DiagnosticFactory.GeneratorWarning(
                GeneratorDiagnosticIds.XamlParseError,
                $"XAML could not be parsed as XML; WpfDesignGuard skipped this file: {evaluation.ParseError}",
                Location.Create(xamlPath, TextSpan.FromBounds(0, 0), new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 0)))));
            return;
        }

        var configData = loadResult.Config;
        var ledger = new ExemptionLedger(configData.Exemptions);
        var hardZero = new HardZeroResolver(configData.HardZero);
        var index = evaluation.Index!;

        // Exemption matching first (shared engine, same order as the CLI): exempted hits
        // are never reported here and never enter the baseline .
        var unexempted = new List<RuleFinding>();
        foreach (var finding in evaluation.Findings)
        {
            var exemption = ledger.Match(finding.RuleId, finding.File, finding.Type);
            if (exemption == null)
            {
                unexempted.Add(finding);
            }
        }

        var (downgraded, escalated) = ColdStartPolicy.Split(configData, hardZero, relativePath, unexempted);


        foreach (var finding in downgraded)
        {
            spc.ReportDiagnostic(CreateRuleDiagnostic(RuleCatalog.TryGet(finding.RuleId)!, xamlPath, index, finding, DiagnosticSeverity.Warning));
        }

        foreach (var finding in escalated)
        {
            var descriptor = RuleCatalog.TryGet(finding.RuleId)!;
            var severity = configData.SeverityOf(finding.RuleId) == RuleSeverity.Warning
                ? DiagnosticSeverity.Warning
                : DiagnosticSeverity.Error;
            spc.ReportDiagnostic(CreateRuleDiagnostic(descriptor, xamlPath, index, finding, severity));
        }
    }

    /// <summary>
    /// The scope-root-relative path of a XAML additional file. MSBuild injects items with
    /// their original (project-relative) specs, so both inputs are project-relative in the
    /// common case and pass through unchanged; when both are absolute we relativize against
    /// the config's directory (designguard.json sits at the project root).
    /// </summary>
    private static string ComputeRelativePath(string configPath, string xamlPath)
    {
        var configAbsolute = System.IO.Path.IsPathRooted(configPath);
        var xamlAbsolute = System.IO.Path.IsPathRooted(xamlPath);
        var xamlNorm = PathUtilities.Normalize(xamlPath);

        if (configAbsolute && xamlAbsolute)
        {
            var configDir = PathUtilities.Normalize(configPath);
            configDir = configDir.Substring(0, configDir.LastIndexOf('/') + 1).TrimEnd('/');
            return PathUtilities.MakeRelative(configDir, xamlNorm);
        }

        if (!xamlAbsolute)
        {
            return xamlNorm; // project-relative — the same form the config and CLI use
        }

        // Config project-relative but XAML absolute: fall back to the file name (deterministic).
        return xamlNorm.Substring(xamlNorm.LastIndexOf('/') + 1);
    }

    private static Diagnostic CreateRuleDiagnostic(
        RuleDescriptor descriptor,
        string xamlPath,
        XamlIndex index,
        RuleFinding finding,
        DiagnosticSeverity severity)
    {
        var offset = index.OffsetOf(finding.Line, finding.Column);
        var end = Math.Min(offset + finding.Length, index.Text.Length);
        var start = ToLinePosition(index.PositionOf(offset));
        var endPos = ToLinePosition(index.PositionOf(end));

        // The message was formatted by Core from the same descriptor (no drift);
        // the diagnostic carries the final text so both severities share one contract.
        return DiagnosticFactory.Create(descriptor, severity, finding.Message,
            Location.Create(xamlPath, TextSpan.FromBounds(offset, end), new LinePositionSpan(start, endPos)));
    }

    private static LinePosition ToLinePosition((int Line, int Column) pos) =>
        new(pos.Line - 1, pos.Column - 1);
}
