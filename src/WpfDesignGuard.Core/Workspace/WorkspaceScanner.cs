using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WpfDesignGuard.Core.Configuration;
using WpfDesignGuard.Core.Globbing;
using WpfDesignGuard.Core.Governance;

namespace WpfDesignGuard.Core.Workspace;

/// <summary>
/// Enumerates the CLI scan surface: files under the scope root matching the global
/// include/exclude (rule-effective surfaces are applied per rule by the caller via
/// <see cref="EffectiveScope"/>, mirroring the generator's per-rule filter).
/// Paths are returned scope-root-relative with '/' separators — the same form the
/// config, the exemption ledger and the baseline snapshot use.
/// </summary>
public static class WorkspaceScanner
{
    public static IReadOnlyList<string> ScanXamlFiles(string rootDirectory, ScopeConfig scope)
    {
        if (rootDirectory is null)
        {
            throw new ArgumentNullException(nameof(rootDirectory));
        }

        if (scope is null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (!Directory.Exists(rootDirectory))
        {
            return Array.Empty<string>();
        }

        var include = scope.Include.Select(p => new GlobMatcher(p)).ToList();
        var exclude = scope.Exclude.Select(p => new GlobMatcher(p)).ToList();

        var root = Path.GetFullPath(rootDirectory);
        var result = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            // Skip build output regardless of scope (bin/obj never belong to a design surface).
            // netstandard2.0 has no Path.GetRelativePath; prefixes are canonical absolute
            // paths from Directory.EnumerateFiles, so an ordinal strip is exact.
            var relative = GlobMatcher.Normalize(
                file.StartsWith(root, StringComparison.Ordinal) ? file.Substring(root.Length + 1) : file);
            var segments = relative.Split('/');
            if (segments.Any(s => s == "bin" || s == "obj"))
            {
                continue;
            }

            if (GlobMatcher.MatchesAny(include, exclude, relative))
            {
                result.Add(relative);
            }
        }

        return result.OrderBy(p => p, StringComparer.Ordinal).ToList();
    }
}
