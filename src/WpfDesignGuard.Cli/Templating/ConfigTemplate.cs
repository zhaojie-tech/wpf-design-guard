using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WpfDesignGuard.Core;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Rules;

namespace WpfDesignGuard.Cli.Templating;

/// <summary>
/// The init/baseline config template layer (ADR-0005).
///
/// Layering contract:
/// - Evaluation and baseline seeding ALWAYS use the fully-enabled configuration —
/// stock findings of token-dependent rules enter the baseline, so enabling them
/// later does not go red .
/// - The template only shapes the WRITTEN text: token-dependent implemented rules get
/// an explicit `"enabled": false` entry plus a how-to-re-enable comment. The engine's
/// contract ("unlisted = enabled") is untouched.
/// - Comments are anchor-injected after ConfigWriter's deterministic serialization
/// (ConfigWriter stays a pure serializer). Both init and baseline --update route
/// through <see cref="WriteWithTemplate"/> so the comments survive re-baselining.
/// </summary>
internal static class ConfigTemplate
{
    private static readonly string TokenComment =
        "  // ---- Token-dependent rules are OFF until you adopt a token table (ADR-0005). ----\n" +
        "  // These rules need tokens.fontSizes / tokens.spacingLadder to be meaningful; with\n" +
        "  // defaults they flag every literal in the repo. To enable later: flip \"enabled\"\n" +
        "  // to true for each, then RE-RUN `designguard init` — re-init re-seeds the baseline\n" +
        "  // from current state, which is the supported path for turning rules on\n" +
        "  // (baseline --update refuses red trees; see docs/guides/gradual-adoption.md).\n";

    /// <summary>Serializes config and injects the token-rule template block.</summary>
    public static string WriteWithTemplate(DesignGuardConfig config, bool firstInit)
    {
        var text = ConfigWriter.Write(config);
        return InjectTemplate(text, config, firstInit);
    }

    /// <summary>
    /// Anchor-based post-processing on the deterministic writer output: the rules object
    /// gains explicit disabled entries for token-dependent implemented rules (first init
    /// only — an existing user config carries its own rules and is never rewritten) and
    /// the how-to comment block.
    /// </summary>
    public static string InjectTemplate(string text, DesignGuardConfig config, bool firstInit)
    {
        var sb = new StringBuilder(text);
        var anchor = text.IndexOf("\"rules\": {", StringComparison.Ordinal);
        if (anchor < 0)
        {
            return text; // writer output without rules section — nothing to template
        }

        var insertAt = anchor + "\"rules\": {".Length;
        var body = new StringBuilder();

        var toDisable = RuleCatalog.TokenDependentImplemented(GuardVersion.Current)
            .Where(r => firstInit && !config.Rules.ContainsKey(r.Id))
            .ToList();

        if (toDisable.Count > 0)
        {
            // Entries must be syntactically continuous with the writer's output:
            // writer emits "  \"rules\": {\n" then entries "    \"DGxxxx\": { ... },\n",
            // or an empty object line "  },\n" when no rules are configured.
            foreach (var rule in toDisable)
            {
                body.Append($"\n    \"{rule.Id}\": {{ \"severity\": \"{rule.DefaultSeverity.ToString().ToLowerInvariant()}\", \"enabled\": false }},");
            }
        }

        sb.Insert(insertAt, body.ToString());

        // Comment block goes right before the "rules" key line start.
        var rulesLineStart = sb.ToString().LastIndexOf('\n', sb.ToString().IndexOf("\"rules\": {", StringComparison.Ordinal));
        sb.Insert(rulesLineStart + 1, TokenComment);

        return sb.ToString();
    }
}
