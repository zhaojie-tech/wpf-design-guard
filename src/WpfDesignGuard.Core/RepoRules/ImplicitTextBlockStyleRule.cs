using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;
using WpfDesignGuard.Core.Xaml;

namespace WpfDesignGuard.Core.RepoRules;

/// <summary>
/// DG0014 (CLI-only): the implicit TextBlock style must set a base FontSize; otherwise
/// every TextBlock falls back to the WPF 12px default. Cross-file judgement:
/// App.xaml MergedDictionaries/pack URIs are resolved in load order, the last implicit
/// TextBlock style wins (WPF override semantics), and BasedOn chains are followed for the
/// FontSize setter. Unresolvable sources surface as unchecked, never as a violation.
/// </summary>
public sealed class ImplicitTextBlockStyleRule : RepoRule
{
    private const int MaxDepth = 10;

    private sealed class Definition
    {
        public Definition(string file, XElement style)
        {
            File = file;
            Style = style;
        }

        public string File { get; }

        public XElement Style { get; }
    }

    public ImplicitTextBlockStyleRule()
        : base(RuleCatalog.TryGet("DG0014")!)
    {
    }

    public override IReadOnlyList<RuleFinding> Evaluate(RepoContext context)
    {
        var findings = new List<RuleFinding>();
        var indexes = new Dictionary<string, XamlIndex>(StringComparer.Ordinal);
        foreach (var file in context.Files.Keys)
        {
            var index = context.TryParse(file);
            if (index != null)
            {
                indexes[file] = index;
            }
        }

        var appFile = indexes.Keys.FirstOrDefault(p =>
            string.Equals(p.Substring(p.LastIndexOf('/') + 1), "App.xaml", StringComparison.OrdinalIgnoreCase));
        if (appFile == null)
        {
            appFile = indexes.Keys.FirstOrDefault(p =>
                indexes[p].Document.Root?.Name.LocalName == "Application");
        }

        var ordered = new List<Definition>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var unresolved = context.Unchecked;

        if (appFile != null)
        {
            CollectFromFile(appFile, indexes, ordered, visited, unresolved, depth: 0, context);
        }
        else
        {
            // No application definition: every dictionary's implicit style applies in its
            // own scope, so each lacking definition is reported.
            foreach (var file in indexes.Keys.OrderBy(p => p, StringComparer.Ordinal))
            {
                CollectFromFile(file, indexes, ordered, visited, unresolved, depth: 0, context);
            }
        }

        var winners = appFile != null
            ? (ordered.Count > 0 ? new[] { ordered[ordered.Count - 1] } : Array.Empty<Definition>())
            : ordered.ToArray();

        foreach (var definition in winners)
        {
            if (ResolvesFontSize(definition, indexes, depth: 0))
            {
                continue;
            }

            var index = indexes[definition.File];
            var (line, column) = index.PositionOf(definition.Style) ?? (1, 1);
            findings.Add(new RuleFinding(
                Id, definition.File, line, column, definition.Style.Name.LocalName.Length,
                string.Format(CultureInfo.InvariantCulture, Descriptor.MessageFormat,
                    $"{definition.File}:{line}")));
        }

        return findings;
    }

    private static void CollectFromFile(
        string file,
        IReadOnlyDictionary<string, XamlIndex> indexes,
        List<Definition> ordered,
        HashSet<string> visited,
        List<string> unresolved,
        int depth,
        RepoContext context)
    {
        if (depth > MaxDepth || !visited.Add(file) || !indexes.TryGetValue(file, out var index))
        {
            return;
        }

        // Merged dictionaries load first (declaration order); the file's own implicit
        // styles come after them and therefore override.
        foreach (var merged in index.Document.Descendants().Where(e => e.Name.LocalName == "ResourceDictionary"))
        {
            var source = merged.Attribute("Source")?.Value;
            if (string.IsNullOrWhiteSpace(source) || XamlValue.IsMarkupExtension(source))
            {
                continue;
            }

            var resolved = ResolveSource(file, source!, indexes);
            if (resolved == null)
            {
                unresolved.Add($"{file} → {source}");
                continue;
            }

            CollectFromFile(resolved, indexes, ordered, visited, unresolved, depth + 1, context);
        }

        foreach (var style in index.Document.Descendants().Where(e => e.Name.LocalName == "Style"))
        {
            if (HasKey(style))
            {
                continue;
            }

            var target = style.Attribute("TargetType")?.Value;
            if (target == null)
            {
                continue;
            }

            var stripped = SetterSpecCollector.StripMarkupExtension(target.Trim());
            var typeName = stripped.Substring(stripped.LastIndexOf(':') + 1);
            if (string.Equals(typeName, "TextBlock", StringComparison.Ordinal))
            {
                ordered.Add(new Definition(file, style));
            }
        }
    }

    private static string? ResolveSource(
        string currentFile,
        string source,
        IReadOnlyDictionary<string, XamlIndex> indexes)
    {
        var raw = source.Trim();
        var component = raw.IndexOf("component/", StringComparison.OrdinalIgnoreCase);
        if (component >= 0)
        {
            raw = raw.Substring(component + "component/".Length);
        }

        raw = raw.TrimStart('/');
        var dir = currentFile.Contains('/') ? currentFile.Substring(0, currentFile.LastIndexOf('/') + 1) : string.Empty;
        var candidate = (dir + raw).TrimStart('/');
        if (indexes.ContainsKey(candidate))
        {
            return candidate;
        }

        // Some projects reference app-root paths explicitly.
        return indexes.ContainsKey(raw) ? raw : null;
    }

    private static bool HasKey(XElement style)
    {
        return style.Attributes().Any(a =>
            string.Equals(a.Name.LocalName, "Key", StringComparison.Ordinal) &&
            a.Name.NamespaceName.Contains("/winfx/2006/xaml", StringComparison.Ordinal));
    }

    private static bool ResolvesFontSize(Definition definition, IReadOnlyDictionary<string, XamlIndex> indexes, int depth)
    {
        if (depth > MaxDepth)
        {
            return false;
        }

        if (HasFontSizeSetter(definition.Style))
        {
            return true;
        }

        var basedOn = definition.Style.Attribute("BasedOn")?.Value;
        if (string.IsNullOrWhiteSpace(basedOn))
        {
            return false;
        }

        var key = ExtractResourceKey(basedOn!);
        if (key == null)
        {
            return false;
        }

        foreach (var file in indexes.Keys)
        {
            var index = indexes[file];
            foreach (var style in index.Document.Descendants().Where(e => e.Name.LocalName == "Style"))
            {
                var styleKey = style.Attributes().FirstOrDefault(a =>
                    string.Equals(a.Name.LocalName, "Key", StringComparison.Ordinal) &&
                    a.Name.NamespaceName.Contains("/winfx/2006/xaml", StringComparison.Ordinal))?.Value;
                if (string.Equals(styleKey, key, StringComparison.Ordinal))
                {
                    return ResolvesFontSize(new Definition(file, style), indexes, depth + 1);
                }
            }
        }

        return false;
    }

    private static bool HasFontSizeSetter(XElement style)
    {
        foreach (var setter in style.Descendants().Where(e => e.Name.LocalName == "Setter"))
        {
            var property = setter.Attribute("Property")?.Value?.Trim();
            if (property == null)
            {
                continue;
            }

            var name = property.Substring(property.LastIndexOf('.') + 1);
            if (string.Equals(name, "FontSize", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ExtractResourceKey(string markup)
    {
        var trimmed = markup.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            trimmed = trimmed.Substring(1).TrimEnd('}');
        }

        var tokens = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length >= 2 ? tokens[tokens.Length - 1] : null;
    }
}
