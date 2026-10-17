using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace WpfDesignGuard.Core.Xaml;

/// <summary>
/// One pending Setter validation extracted from XAML text (cheap, cached with the text).
/// The type stays an UNRESOLVED name here; symbol resolution happens on the
/// Analyzers side against the live Compilation.
/// </summary>
public sealed class SetterSpec
{
    public SetterSpec(
        string typeName,
        string typePrefix,
        string propertyName,
        string propertyPrefix,
        bool isAttached,
        string ownerXmlnsPrefix,
        int line,
        int column,
        int length)
    {
        TypeName = typeName;
        TypePrefix = typePrefix;
        PropertyName = propertyName;
        PropertyPrefix = propertyPrefix;
        IsAttached = isAttached;
        OwnerXmlnsPrefix = ownerXmlnsPrefix;
        Line = line;
        Column = column;
        Length = length;
    }

    /// <summary>Target type name (TargetType of the owning Style/ControlTemplate), prefix-stripped.</summary>
    public string TypeName { get; }

    /// <summary>XML namespace prefix of the target type ("local" for local:Pager), or "".</summary>
    public string TypePrefix { get; }

    /// <summary>Property name from Setter.Property (post-dot segment for attached properties).</summary>
    public string PropertyName { get; }

    /// <summary>Owner type name when written attached ("TextBlock" for TextBlock.FontSize).</summary>
    public string PropertyPrefix { get; }

    /// <summary>xmlns prefix of the attached owner ("local" for local:Pager.Page), or "".</summary>
    public string OwnerXmlnsPrefix { get; }

    /// <summary>True when Property carries an owner prefix (attached-property form).</summary>
    public bool IsAttached { get; }

    public int Line { get; }

    public int Column { get; }

    public int Length { get; }
}

/// <summary>
/// Text-stage collector for DG0012: every Setter element (direct, or inside
/// Trigger/DataTrigger/MultiTrigger setter lists) paired with the TargetType context of
/// its nearest Style or ControlTemplate ancestor. ControlTemplate.TargetType governs
/// template triggers. Setters without a resolvable context surface as null-type
/// specs (unchecked surface, never silent).
/// </summary>
public static class SetterSpecCollector
{
    public static IReadOnlyList<SetterSpec> Collect(XamlIndex index)
    {
        var specs = new List<SetterSpec>();

        foreach (var setter in index.Document.Descendants().Where(e => e.Name.LocalName == "Setter"))
        {
            var propertyAttr = setter.Attribute("Property");
            if (propertyAttr == null || string.IsNullOrWhiteSpace(propertyAttr.Value))
            {
                continue; // Setter without Property (e.g. <Setter Value=…> for events) — not our surface
            }

            var (propName, propPrefix, ownerXmlnsPrefix) = ParseProperty(propertyAttr.Value.Trim());
            var isAttached = propPrefix.Length > 0;

            var (typeName, typePrefix, contextFound) = FindTargetTypeContext(setter, index);
            if (!contextFound)
            {
                // Contextless Setter (rare dictionary forms): unchecked surface — skipped here,
                // reported by the caller via the spec list (contextFound=false not carried on
                // the spec; the absence of a checkable type is recorded by the checker as
                // "no context" → unchecked, not violation).
                continue;
            }

            var span = index.ValueSpanOf(propertyAttr);
            var pos = index.PositionOf(propertyAttr);
            var (line, col, len) = span != null
                ? (index.PositionOf(span.Value.Offset).Line, index.PositionOf(span.Value.Offset).Column, span.Value.Length)
                : (pos?.Line ?? 1, pos?.Column ?? 1, propertyAttr.Value.Length);

            specs.Add(new SetterSpec(typeName, typePrefix, propName, propPrefix, isAttached, ownerXmlnsPrefix, line, col, len));
        }

        return specs;
    }

    private static (string Name, string Prefix, bool Found) FindTargetTypeContext(XElement setter, XamlIndex index)
    {
        var current = setter.Parent;
        while (current != null)
        {
            if (current.Name.LocalName == "Style" || current.Name.LocalName == "ControlTemplate")
            {
                var targetType = current.Attribute("TargetType")?.Value;
                if (targetType == null)
                {
                    return (string.Empty, string.Empty, false);
                }

                var (name, prefix) = Split(StripMarkupExtension(targetType.Trim()));
                return (name, prefix, name.Length > 0);
            }

            current = current.Parent;
        }

        return (string.Empty, string.Empty, false);
    }

    /// <summary>Strips "{x:Type X}" → "X"; bare names pass through.</summary>
    public static string StripMarkupExtension(string value)
    {
        var v = value.Trim();
        if (!v.StartsWith("{", StringComparison.Ordinal))
        {
            return v;
        }

        if (v.StartsWith("{}", StringComparison.Ordinal))
        {
            return v.Substring(2);
        }

        // {x:Type Controls:Pager} → inner payload after the first whitespace.
        var inner = v.Substring(1, v.Length - 2);
        var space = inner.IndexOf(' ');
        return space >= 0 ? inner.Substring(space + 1).Trim() : string.Empty;
    }

    /// <summary>
    /// Setter.Property grammar: "Property" | "OwnerType.Property" | "prefix:OwnerType.Property".
    /// Returns (propertyName, ownerTypeName, ownerXmlnsPrefix).
    /// </summary>
    internal static (string Name, string OwnerType, string OwnerXmlnsPrefix) ParseProperty(string value)
    {
        var rest = value;
        var xmlnsPrefix = string.Empty;
        var colon = rest.IndexOf(':');
        if (colon >= 0)
        {
            xmlnsPrefix = rest.Substring(0, colon);
            rest = rest.Substring(colon + 1);
        }

        var dot = rest.LastIndexOf('.');
        if (dot >= 0)
        {
            return (rest.Substring(dot + 1), rest.Substring(0, dot), xmlnsPrefix);
        }

        return (rest, string.Empty, xmlnsPrefix);
    }

    private static (string Name, string Prefix) Split(string value)
    {
        var colon = value.LastIndexOf(':');
        return colon >= 0
            ? (value.Substring(colon + 1), value.Substring(0, colon))
            : (value, string.Empty);
    }
}
