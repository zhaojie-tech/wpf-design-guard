using System;
using System.Collections.Generic;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Governance;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Core.Xaml.Rules;

/// <summary>
/// Base class of the XAML rule implementations. Rules are pure functions over
/// (XamlIndex, config): no IO, no global state — the same instance serves the
/// generator (single file) and the CLI (whole repo) verbatim.
/// </summary>
public abstract class XamlRule
{
    protected XamlRule(RuleDescriptor descriptor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    public RuleDescriptor Descriptor { get; }

    public string Id => Descriptor.Id;

    public abstract IReadOnlyList<RuleFinding> Evaluate(XamlIndex index, DesignGuardConfig config, string file);
}
