using System;
using System.Collections.Generic;
using System.Linq;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Globbing;

namespace WpfDesignGuard.Core.Governance;

/// <summary>
/// Hard-zero domain resolver (governance layer 2): a hit inside a domain for a listed
/// rule is always red regardless of the global baseline; the baseline never relaxes it.
/// </summary>
public sealed class HardZeroResolver
{
    private sealed class Domain
    {
        public Domain(string pattern, GlobMatcher matcher, IReadOnlyList<string> rules)
        {
            Pattern = pattern;
            Matcher = matcher;
            Rules = rules;
        }

        public string Pattern { get; }

        public GlobMatcher Matcher { get; }

        public IReadOnlyList<string> Rules { get; }
    }

    private readonly List<Domain> _domains;

    public HardZeroResolver(IReadOnlyDictionary<string, IReadOnlyList<string>> hardZero)
    {
        _domains = new List<Domain>();
        foreach (var kv in hardZero)
        {
            _domains.Add(new Domain(kv.Key, new GlobMatcher(kv.Key), kv.Value));
        }
    }

    public bool IsHardZero(string relativePath, string ruleId)
    {
        foreach (var domain in _domains)
        {
            if (domain.Rules.Contains(ruleId) && domain.Matcher.IsMatch(relativePath))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Human-readable domain patterns covering the file (for reports), or empty.</summary>
    public IReadOnlyList<string> DomainsFor(string relativePath)
    {
        var result = new List<string>();
        foreach (var domain in _domains)
        {
            if (domain.Matcher.IsMatch(relativePath))
            {
                result.Add(domain.Pattern);
            }
        }

        return result;
    }
}

/// <summary>
/// Exemption ledger with the bidirectional-equality semantics:
/// an unregistered hit is red, and a registered entry whose count no longer matches
/// the actual hits is also red (stale). Occurrences consume entries in registration
/// order; type-scoped entries (coverage gates) are consumed first for typed hits.
/// </summary>
public sealed class ExemptionLedger
{
    private sealed class Slot
    {
        public Slot(ExemptionEntry entry, int index)
        {
            Entry = entry;
            Index = index;
        }

        public ExemptionEntry Entry { get; }

        public int Index { get; }

        public int Consumed { get; set; }
    }

    private readonly Dictionary<string, List<Slot>> _byKey =
        new(StringComparer.Ordinal);

    public ExemptionLedger(IReadOnlyList<ExemptionEntry> entries)
    {
        if (entries is null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var key = Key(entries[i].Rule, entries[i].File);
            if (!_byKey.TryGetValue(key, out var slots))
            {
                slots = new List<Slot>();
                _byKey[key] = slots;
            }

            slots.Add(new Slot(entries[i], i));
        }
    }

    /// <summary>
    /// Consumes one occurrence. Returns the entry consumed, or null when unregistered.
    /// Typed hits prefer a type-scoped entry, then fall back to an untyped one.
    /// </summary>
    public ExemptionEntry? Match(string ruleId, string file, string? hitType)
    {
        if (!_byKey.TryGetValue(Key(ruleId, file), out var slots))
        {
            return null;
        }

        Slot? typed = null;
        Slot? untyped = null;
        foreach (var slot in slots)
        {
            if (slot.Consumed >= slot.Entry.Count)
            {
                continue;
            }

            if (hitType != null && string.Equals(slot.Entry.Type, hitType, StringComparison.Ordinal))
            {
                typed = slot;
                break;
            }

            if (slot.Entry.Type == null && untyped == null)
            {
                untyped = slot;
            }
        }

        var chosen = typed ?? untyped;
        if (chosen == null)
        {
            return null;
        }

        chosen.Consumed++;
        return chosen.Entry;
    }

    /// <summary>Entries whose consumed count differs from the registered count.</summary>
    public IReadOnlyList<StaleExemption> CollectStale()
    {
        var stale = new List<StaleExemption>();
        foreach (var slots in _byKey.Values)
        {
            foreach (var slot in slots)
            {
                if (slot.Consumed != slot.Entry.Count)
                {
                    stale.Add(new StaleExemption(slot.Entry, slot.Consumed));
                }
            }
        }

        return stale;
    }

    private static string Key(string ruleId, string file) => ruleId + "\u0001" + file;
}
