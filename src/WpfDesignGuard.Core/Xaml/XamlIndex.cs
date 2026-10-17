using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace WpfDesignGuard.Core.Xaml;

/// <summary>
/// A concrete attribute-shaped occurrence collected across the three XAML scan forms
/// (the three scan forms): plain attribute, property element, and Setter Value.
/// Line/column are 1-based and point at the value span; Length covers the value text.
/// </summary>
public sealed class XamlAttributeOccurrence
{
    public XamlAttributeOccurrence(
        string propertyName,
        string value,
        int line,
        int column,
        int length,
        XElement? setterOwner,
        XElement ownerElement)
    {
        PropertyName = propertyName;
        Value = value;
        Line = line;
        Column = column;
        Length = length;
        SetterOwner = setterOwner;
        OwnerElement = ownerElement;
    }

    public string PropertyName { get; }

    /// <summary>Raw (untrimmed) value text.</summary>
    public string Value { get; }

    public int Line { get; }

    public int Column { get; }

    public int Length { get; }

    /// <summary>The Setter element when the occurrence came from a Setter (attribute or .Value child).</summary>
    public XElement? SetterOwner { get; }

    public XElement OwnerElement { get; }
}

/// <summary>
/// Parsed XAML with line info and comments stripped before evaluation.
/// The document keeps the original line numbers; comments are removed from the tree
/// before evaluation so that "tombstone" comments are never read as violations.
/// </summary>
public sealed class XamlIndex
{
    private readonly int[] _lineStarts;

    private XamlIndex(XDocument document, string text, int[] lineStarts)
    {
        Document = document;
        Text = text;
        _lineStarts = lineStarts;
    }

    public XDocument Document { get; }

    /// <summary>Original source text (comments intact — spans resolve against it).</summary>
    public string Text { get; }

    public static XamlIndex Parse(string text)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var document = XDocument.Load(
            new System.IO.StringReader(text),
            LoadOptions.SetLineInfo | LoadOptions.None);

        // Strip comments before evaluation: a tombstone comment must never count as a violation.
        document.DescendantNodes().OfType<XComment>().Remove();

        return new XamlIndex(document, text, ComputeLineStarts(text));
    }

    private static int[] ComputeLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return starts.ToArray();
    }

    /// <summary>Absolute offset of a 1-based (line, column) pair.</summary>
    public int OffsetOf(int line, int column)
    {
        if (line < 1 || line > _lineStarts.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }

        return _lineStarts[line - 1] + Math.Max(0, column - 1);
    }

    /// <summary>1-based line/column of an absolute offset.</summary>
    public (int Line, int Column) PositionOf(int offset)
    {
        if (offset < 0 || offset > Text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        // Binary search for the line containing the offset.
        var lo = 0;
        var hi = _lineStarts.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (_lineStarts[mid] <= offset)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return (lo + 1, offset - _lineStarts[lo] + 1);
    }

    /// <summary>1-based line/column of an XObject (element or text node), or null when line info is absent.</summary>
    public (int Line, int Column)? PositionOf(XObject obj)
    {
        if (obj is IXmlLineInfo info && info.HasLineInfo())
        {
            return (info.LineNumber, info.LinePosition);
        }

        return null;
    }

    /// <summary>
    /// Resolves the exact source span of an attribute value by scanning forward from the
    /// attribute-name position for the <c>=</c> sign and the opening/closing quote.
    /// Returns null when the span cannot be located (caller falls back to name position).
    /// </summary>
    public (int Offset, int Length)? ValueSpanOf(XAttribute attribute)
    {
        var pos = PositionOf(attribute);
        if (pos == null)
        {
            return null;
        }

        var start = OffsetOf(pos.Value.Line, pos.Value.Column);
        var eq = Text.IndexOf('=', start);
        if (eq < 0)
        {
            return null;
        }

        var quoteStart = eq + 1;
        while (quoteStart < Text.Length && (Text[quoteStart] == ' ' || Text[quoteStart] == '\t'))
        {
            quoteStart++;
        }

        if (quoteStart >= Text.Length || (Text[quoteStart] != '"' && Text[quoteStart] != '\''))
        {
            return null;
        }

        var quote = Text[quoteStart];
        var close = Text.IndexOf(quote, quoteStart + 1);
        if (close < 0)
        {
            return null;
        }

        return (quoteStart + 1, close - quoteStart - 1);
    }

    /// <summary>
    /// Collects every occurrence of the named properties across the three scan forms:
    /// attributes (<c>Margin="…"</c>), property elements (<c>&lt;X.Margin&gt;…&lt;/X.Margin&gt;</c>),
    /// and Setter values (both <c>Value="…"</c> and <c>&lt;Setter.Value&gt;…&lt;/Setter.Value&gt;</c>).
    /// Namespace declarations and markup-extension values are included here and must be
    /// filtered by the rule using <see cref="XamlValue.IsMarkupExtension"/>.
    /// </summary>
    public IReadOnlyList<XamlAttributeOccurrence> CollectPropertyOccurrences(params string[] propertyNames)
    {
        var wanted = new HashSet<string>(propertyNames, StringComparer.Ordinal);
        var result = new List<XamlAttributeOccurrence>();

        foreach (var element in Document.Descendants())
        {
            var isSetter = element.Name.LocalName == "Setter";

            foreach (var attr in element.Attributes())
            {
                if (attr.IsNamespaceDeclaration)
                {
                    continue;
                }

                string? effectiveName = attr.Name.LocalName;
                XElement? setterOwner = null;

                // Attached-property form: TextBlock.FontSize="…" — the dot is part of the
                // XML local name, the scanned property is the segment after it.
                var dot = effectiveName.IndexOf('.');
                if (dot > 0)
                {
                    effectiveName = effectiveName.Substring(dot + 1);
                }

                if (isSetter && attr.Name.LocalName == "Value")
                {
                    // <Setter Property="Margin" Value="8,6"/>: the scanned property lives on Property=.
                    var declared = element.Attribute("Property")?.Value;
                    if (declared == null)
                    {
                        continue;
                    }

                    effectiveName = declared.Substring(declared.LastIndexOf('.') + 1);
                    setterOwner = element;
                }

                if (effectiveName != null && wanted.Contains(effectiveName) && attr.Value != null)
                {
                    var span = ValueSpanOf(attr);
                    var pos = PositionOf(attr);
                    var (line, col, len) = span != null
                        ? (PositionOf(span.Value.Offset).Item1, PositionOf(span.Value.Offset).Item2, span.Value.Length)
                        : (pos?.Line ?? 1, pos?.Column ?? 1, attr.Value.Length);

                    result.Add(new XamlAttributeOccurrence(
                        effectiveName, attr.Value, line, col, len,
                        setterOwner, element));
                }
            }

            // Property elements: <Grid.RowDefinitions>…</Grid.RowDefinitions>, <X.Margin>8</X.Margin>,
            // and the Setter.Value child form (<Setter Property="…"><Setter.Value>…</Setter.Value></Setter>).
            if (element.Name.LocalName.IndexOf('.') > 0 && !element.HasElements && !element.HasAttributes)
            {
                var dot = element.Name.LocalName.IndexOf('.');
                var owner = element.Parent;
                var parentIsSetter = owner != null && owner.Name.LocalName == "Setter";
                string name;
                if (parentIsSetter && element.Name.LocalName.EndsWith(".Value", StringComparison.Ordinal))
                {
                    var declared = owner!.Attribute("Property")?.Value;
                    if (declared == null)
                    {
                        continue;
                    }

                    name = declared.Substring(declared.LastIndexOf('.') + 1);
                }
                else
                {
                    name = element.Name.LocalName.Substring(dot + 1);
                }

                if (wanted.Contains(name))
                {
                    var textNode = element.Nodes().OfType<XText>().FirstOrDefault();
                    if (textNode != null && textNode.Value.Trim().Length > 0)
                    {
                        var pos = PositionOf(textNode);
                        // Span covers the trimmed value inside the text node.
                        var raw = textNode.Value;
                        var lead = raw.Length - raw.TrimStart().Length;
                        var valueLen = raw.Trim().Length;
                        result.Add(new XamlAttributeOccurrence(
                            name, raw.Trim(), pos?.Line ?? 1, (pos?.Column ?? 1) + lead, valueLen,
                            parentIsSetter ? owner : null,
                            owner ?? element));
                    }
                }
            }
        }

        return result;
    }

    /// <summary>Value span of an occurrence resolved through its owner attribute; null when unlocatable.</summary>
    public (int Offset, int Length)? ValueSpanOfAttributeOccurrence(XamlAttributeOccurrence occurrence)
    {
        // Walk the owner element's attributes to find the matching source attribute.
        foreach (var attr in occurrence.OwnerElement.Attributes())
        {
            if (attr.IsNamespaceDeclaration || attr.Value != occurrence.Value)
            {
                continue;
            }

            var span = ValueSpanOf(attr);
            if (span != null)
            {
                var (line, col) = PositionOf(span.Value.Offset);
                if (line == occurrence.Line && col == occurrence.Column)
                {
                    return span;
                }
            }
        }

        return null;
    }

    /// <summary>Absolute offset just past the element's name (insertion point for attributes).</summary>
    public int? OffsetOfElementNameEnd(System.Xml.Linq.XElement element)
    {
        var pos = PositionOf(element);
        if (pos == null)
        {
            return null;
        }

        var start = OffsetOf(pos.Value.Line, pos.Value.Column);
        var name = element.Name.LocalName;
        // The source name may carry a namespace prefix; find the end of the local name.
        var candidate = start + name.Length;
        if (candidate < Text.Length && Text.Substring(start, Math.Min(name.Length + 1, Text.Length - start)).Contains(name))
        {
            return start + name.Length;
        }

        return null;
    }

    /// <summary>
    /// The element's effective attribute value: plain attribute first, then the
    /// same-named property element, then (when inside a Setter) nothing — Setter
    /// contexts are consumed via <see cref="CollectPropertyOccurrences"/>.
    /// Returns the trimmed raw value, or null when absent.
    /// </summary>
    public string? EffectiveAttributeValue(XElement element, string propertyName)
    {
        var attr = element.Attribute(propertyName);
        if (attr != null)
        {
            return attr.Value.Trim();
        }

        var propEl = element.Elements().FirstOrDefault(e =>
            e.Name.LocalName == propertyName || e.Name.LocalName.EndsWith("." + propertyName, StringComparison.Ordinal));
        if (propEl != null && !propEl.HasElements)
        {
            var text = propEl.Value.Trim();
            return text.Length > 0 ? text : null;
        }

        return null;
    }
}
