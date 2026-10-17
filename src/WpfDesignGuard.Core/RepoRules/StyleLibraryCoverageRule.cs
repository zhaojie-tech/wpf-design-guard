using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Globbing;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Core.RepoRules;

/// <summary>
/// DG0017 (CLI-only): coverage gate. Every clickable type that occurs in the project
/// (and is listed in tokens.clickableTypes) must have an implicit Style at the style
/// library definition site whose MinHeight or MinWidth setter is at least the hit-target
/// floor. A new type that the library never extended fails immediately.
/// Blind spots (no styleLibrary configured, no matching file) surface as unchecked.
/// </summary>
public sealed class StyleLibraryCoverageRule : RepoRule
{
    public StyleLibraryCoverageRule()
        : base(RuleCatalog.TryGet("DG0017")!)
    {
    }

    public override IReadOnlyList<RuleFinding> Evaluate(RepoContext context)
    {
        var findings = new List<RuleFinding>();
        var library = context.Config.Tokens.StyleLibrary;
        if (string.IsNullOrWhiteSpace(library))
        {
            context.Unchecked.Add(
                "DG0017: tokens.styleLibrary is not configured — clickable-type style coverage is unchecked.");
            return findings;
        }

        var matcher = new GlobMatcher(library!);
        var libraryFiles = context.Files.Keys.Where(p => matcher.IsMatch(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();
        if (libraryFiles.Count == 0)
        {
            context.Unchecked.Add(
                $"DG0017: no file matches tokens.styleLibrary '{library}' — coverage is unchecked.");
            return findings;
        }

        var clickable = new HashSet<string>(context.Config.Tokens.ClickableTypes, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in context.Files.Keys)
        {
            var index = context.TryParse(file);
            if (index == null)
            {
                continue;
            }

            foreach (var element in index.Document.Descendants())
            {
                if (clickable.Contains(element.Name.LocalName))
                {
                    used.Add(element.Name.LocalName);
                }
            }
        }

        var floor = context.Config.Tokens.HitTargetFloor;
        foreach (var type in used.OrderBy(t => t, StringComparer.Ordinal))
        {
            var covered = false;
            string? reportedFile = null;
            var reportedLine = 1;
            var reportedColumn = 1;

            foreach (var file in libraryFiles)
            {
                var index = context.TryParse(file);
                if (index == null)
                {
                    continue;
                }

                foreach (var style in index.Document.Descendants().Where(e => e.Name.LocalName == "Style"))
                {
                    if (HasKey(style) || !Targets(style, type))
                    {
                        continue;
                    }

                    if (MeetsFloor(style, floor))
                    {
                        covered = true;
                        break;
                    }

                    reportedFile ??= file;
                    var (line, column) = index.PositionOf(style) ?? (1, 1);
                    reportedLine = line;
                    reportedColumn = column;
                }

                if (covered)
                {
                    break;
                }
            }

            if (covered)
            {
                continue;
            }

            findings.Add(new RuleFinding(
                Id, reportedFile ?? libraryFiles[0], reportedLine, reportedColumn,
                type.Length,
                string.Format(CultureInfo.InvariantCulture, Descriptor.MessageFormat, type, FormatNumber(floor)),
                type));
        }

        return findings;
    }

    private static bool Targets(XElement style, string type)
    {
        var target = style.Attribute("TargetType")?.Value;
        if (target == null)
        {
            return false;
        }

        var stripped = Xaml.SetterSpecCollector.StripMarkupExtension(target.Trim());
        var name = stripped.Substring(stripped.LastIndexOf(':') + 1);
        return string.Equals(name, type, StringComparison.Ordinal);
    }

    private static bool MeetsFloor(XElement style, double floor)
    {
        foreach (var setter in style.Descendants().Where(e => e.Name.LocalName == "Setter"))
        {
            var property = setter.Attribute("Property")?.Value?.Trim();
            if (property == null)
            {
                continue;
            }

            var name = property.Substring(property.LastIndexOf('.') + 1);
            if (!string.Equals(name, "MinHeight", StringComparison.Ordinal) &&
                !string.Equals(name, "MinWidth", StringComparison.Ordinal))
            {
                continue;
            }

            var value = setter.Attribute("Value")?.Value;
            if (value == null || Xaml.XamlValue.IsMarkupExtension(value) || !Xaml.XamlValue.IsNumber(value, out var number))
            {
                continue;
            }

            if (number >= floor)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasKey(XElement style) =>
        style.Attributes().Any(a =>
            string.Equals(a.Name.LocalName, "Key", StringComparison.Ordinal) &&
            a.Name.NamespaceName.Contains("/winfx/2006/xaml", StringComparison.Ordinal));

    private static string FormatNumber(double v) =>
        v == Math.Floor(v) ? ((int)v).ToString(CultureInfo.InvariantCulture) : v.ToString(CultureInfo.InvariantCulture);
}
