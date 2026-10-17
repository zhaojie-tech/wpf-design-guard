using System;
using System.Collections.Generic;

namespace WpfDesignGuard.Core;

/// <summary>Book-keeping for tooling: which rule IDs have a runtime implementation at this version.</summary>
public static class XamlRuleSurfaceKicker
{
    public static IReadOnlyList<string> KnownRuleIds { get; } = new[]
    {
        "DG0001", "DG0002", "DG0003", "DG0005", "DG0007", "DG0010", "DG0012", "DG0015", "DG0016",
    };
}
