using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using WpfDesignGuard.Core.Xaml;

namespace WpfDesignGuard.Analyzers;

/// <summary>
/// Parse-call counter for the DG0012 memo pipeline: type-resolution
/// queries against the Compilation are counted so tests can assert that repeated specs
/// across files hit the memo (zero extra lookups) instead of re-resolving.
/// </summary>
internal sealed class SymbolQueryTelemetry
{
    private int _typeLookups;

    internal void Reset() => System.Threading.Interlocked.Exchange(ref _typeLookups, 0);

    internal void CountLookup() => System.Threading.Interlocked.Increment(ref _typeLookups);

    internal int Current => System.Threading.Volatile.Read(ref _typeLookups);
}

/// <summary>
/// Result of checking one SetterSpec against the compilation.
/// </summary>
internal enum SetterCheckResult
{
    /// <summary>Property exists on the target type (CLR property or *Property DP field along the base chain).</summary>
    Exists,

    /// <summary>Property missing — DG0012 diagnostic.</summary>
    Missing,

    /// <summary>Type or namespace mapping unresolvable: unchecked surface (R2), never a false positive.</summary>
    Unchecked,
}

/// <summary>
/// Symbol-layer Setter property existence: xmlns URI →
/// clr-namespace mapping via XmlnsDefinition assembly attributes, type lookup via
/// Compilation.GetTypeByMetadataName with GetTypesByMetadataName fallback (duplicate
/// types), existence along the base-type chain (CLR properties + "*Property" DP fields).
/// Lookups are memoized per (compilation reference, normalized spec key); no code is
/// executed and no user assembly is loaded.
/// </summary>
internal sealed class SetterExistenceChecker
{
    // The WPF presentation namespace — mapped through XmlnsDefinition attributes of the
    // referenced assemblies (PresentationFramework etc.), never a built-in type table.
    private const string PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private readonly Compilation _compilation;
    private readonly IReadOnlyDictionary<string, string> _prefixToClrNamespace;
    private readonly SymbolQueryTelemetry _telemetry;

    private readonly Dictionary<string, ITypeSymbol?> _typeMemo =
        new(StringComparer.Ordinal);

    private readonly Dictionary<string, SetterCheckResult> _existenceMemo =
        new(StringComparer.Ordinal);

    public SetterExistenceChecker(
        Compilation compilation,
        IReadOnlyDictionary<string, string> prefixToClrNamespace,
        SymbolQueryTelemetry telemetry)
    {
        _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        _prefixToClrNamespace = prefixToClrNamespace ?? throw new ArgumentNullException(nameof(prefixToClrNamespace));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    }

    /// <summary>
    /// Checks one spec. <paramref name="relativePath"/> is only used by callers for
    /// reporting; the result is pure symbol judgement.
    /// </summary>
    public SetterCheckResult Check(SetterSpec spec)
    {
        var clrNamespace = ResolveClrNamespace(spec.TypePrefix);
        if (clrNamespace == null)
        {
            return SetterCheckResult.Unchecked; // unmapped xmlns prefix → blind spot, R2
        }

        var type = ResolveType(clrNamespace, spec.TypeName);
        if (type == null)
        {
            return SetterCheckResult.Unchecked; // unknown type → blind spot, never a false positive
        }

        if (spec.IsAttached)
        {
            // Attached property: the OWNER travels with the property name (TextBlock.FontSize
            // → owner TextBlock); an explicit xmlns prefix on the owner maps through it.
            var ownerClr = ResolveClrNamespace(spec.OwnerXmlnsPrefix);
            if (ownerClr == null)
            {
                return SetterCheckResult.Unchecked;
            }

            var owner = ResolveType(ownerClr, spec.PropertyPrefix);
            if (owner == null)
            {
                return SetterCheckResult.Unchecked;
            }

            var memoKey = owner.ToDisplayString() + ".\u0001" + spec.PropertyName;
            if (_existenceMemo.TryGetValue(memoKey, out var cachedOwner))
            {
                return cachedOwner;
            }

            var attachedResult = HasStaticField(owner, spec.PropertyName + "Property")
                ? SetterCheckResult.Exists
                : SetterCheckResult.Missing;
            _existenceMemo[memoKey] = attachedResult;
            return attachedResult;
        }

        var memoKey2 = type.ToDisplayString() + "\u0001" + spec.PropertyName;
        if (_existenceMemo.TryGetValue(memoKey2, out var cached))
        {
            return cached;
        }

        var result = HasInstanceProperty(type, spec.PropertyName)
            ? SetterCheckResult.Exists
            : SetterCheckResult.Missing;

        _existenceMemo[memoKey2] = result;
        return result;
    }

    private IReadOnlyList<string>? _presentationClrNamespaces;

    private string? ResolveClrNamespace(string prefix)
    {
        if (prefix.Length == 0)
        {
            return PresentationNamespace; // default namespace: mapped via XmlnsDefinition below
        }

        return _prefixToClrNamespace.TryGetValue(prefix, out var clr) ? clr : null;
    }

    private ITypeSymbol? ResolveType(string clrNamespace, string typeName)
    {
        if (clrNamespace == PresentationNamespace)
        {
            // Default namespace: map through XmlnsDefinition attributes of referenced
            // assemblies (symbol route — no built-in WPF type table).
            foreach (var clrNs in GetPresentationClrNamespaces())
            {
                var candidate = LookupClrType(clrNs, typeName);
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null; // declared but unresolvable in this compilation → unchecked
        }

        return LookupClrType(clrNamespace, typeName);
    }

    private ITypeSymbol? LookupClrType(string clrNamespace, string typeName)
    {
        var memoKey = clrNamespace + "\u0001" + typeName;
        if (_typeMemo.TryGetValue(memoKey, out var cached))
        {
            return cached;
        }

        _telemetry.CountLookup();

        ITypeSymbol? found;
        var metadataName = clrNamespace + "." + typeName;

        var exact = _compilation.GetTypeByMetadataName(metadataName);
        if (exact != null)
        {
            found = exact;
        }
        else
        {
            // Duplicate types across referenced assemblies make GetTypeByMetadataName
            // return null (R5 external finding) — fall back to all matches.
            var candidates = _compilation.GetTypesByMetadataName(metadataName).ToList();
            found = candidates.Count > 0 ? candidates[0] : null;
        }

        _typeMemo[memoKey] = found;
        return found;
    }

    /// <summary>
    /// clr-namespaces advertised for the presentation URI via
    /// [assembly: System.Windows.Markup.XmlnsDefinition] on referenced assemblies.
    /// Empty when no referenced assembly advertises it (non-WPF compilation) —
    /// everything becomes unchecked surface (R2), never a false positive.
    /// </summary>
    private IReadOnlyList<string> GetPresentationClrNamespaces()
    {
        if (_presentationClrNamespaces != null)
        {
            return _presentationClrNamespaces;
        }

        var list = new List<string>();
        foreach (var reference in _compilation.References)
        {
            if (_compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
            {
                continue;
            }

            foreach (var attr in assembly.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() != "System.Windows.Markup.XmlnsDefinitionAttribute" ||
                    attr.ConstructorArguments.Length < 2)
                {
                    continue;
                }

                if (Equals(attr.ConstructorArguments[0].Value, PresentationNamespace) &&
                    attr.ConstructorArguments[1].Value is string clrNs)
                {
                    list.Add(clrNs);
                }
            }
        }

        _presentationClrNamespaces = list;
        return list;
    }

    private static bool HasInstanceProperty(ITypeSymbol type, string propertyName)
    {
        var current = (ITypeSymbol?)type;
        while (current != null)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol p && p.Name == propertyName)
                {
                    return true;
                }

                // Dependency-property backing field also proves the setter target exists.
                if (member is IFieldSymbol f && f.IsStatic && f.Name == propertyName + "Property")
                {
                    return true;
                }
            }

            current = current.BaseType;
        }

        return false;
    }

    private static bool HasStaticField(ITypeSymbol type, string fieldName)
    {
        var current = (ITypeSymbol?)type;
        while (current != null)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IFieldSymbol f && f.IsStatic && f.Name == fieldName)
                {
                    return true;
                }
            }

            current = current.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Builds the xmlns-prefix → clr-namespace map from the XAML text declarations
    /// (clr-namespace:… forms). The default presentation namespace needs no entry.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseClrNamespaceDeclarations(string xamlText)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        // xmlns:local="clr-namespace:My.App;assembly=Lib" / xmlns:sys="clr-namespace:System;assembly=mscorlib"
        var idx = xamlText.IndexOf("clr-namespace:", StringComparison.Ordinal);
        while (idx >= 0)
        {
            var declStart = xamlText.LastIndexOf("xmlns:", idx, StringComparison.Ordinal);
            if (declStart >= 0)
            {
                var prefixStart = declStart + "xmlns:".Length;
                var eq = xamlText.IndexOf('=', prefixStart);
                if (eq > prefixStart)
                {
                    var prefix = xamlText.Substring(prefixStart, eq - prefixStart).Trim();
                    var valueStart = xamlText.IndexOf('"', eq);
                    var valueEnd = valueStart >= 0 ? xamlText.IndexOf('"', valueStart + 1) : -1;
                    if (prefix.Length > 0 && valueStart >= 0 && valueEnd > valueStart)
                    {
                        var value = xamlText.Substring(valueStart + 1, valueEnd - valueStart - 1);
                        if (value.StartsWith("clr-namespace:", StringComparison.Ordinal))
                        {
                            var ns = value.Substring("clr-namespace:".Length);
                            var semi = ns.IndexOf(';');
                            if (semi >= 0)
                            {
                                ns = ns.Substring(0, semi);
                            }

                            map[prefix] = ns;
                        }
                    }
                }
            }

            idx = xamlText.IndexOf("clr-namespace:", idx + 1, StringComparison.Ordinal);
        }

        return map;
    }
}
