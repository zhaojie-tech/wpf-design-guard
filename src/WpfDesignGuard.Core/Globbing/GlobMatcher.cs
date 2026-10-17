using System;
using System.Collections.Generic;

namespace WpfDesignGuard.Core.Globbing;

/// <summary>
/// Minimal path glob matcher for scope/exemption/hard-zero patterns.
/// Semantics: <c>**</c> matches any number of path segments (including zero),
/// <c>*</c> matches any characters inside a single segment, <c>?</c> matches a single
/// character. Matching is ordinal case-sensitive by design (patterns are repo-relative
/// and reviewed as data); a per-pattern case-insensitive match is available for
/// Windows-host workflows.
/// </summary>
public sealed class GlobMatcher
{
    private readonly string[] _segments;
    private readonly StringComparison _comparison;

    public GlobMatcher(string pattern, bool ignoreCase = false)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException("Glob pattern must not be empty.", nameof(pattern));
        }

        _segments = Normalize(pattern).Split('/');
        _comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    public static string Normalize(string path) =>
        path.Replace('\\', '/').TrimStart('/').TrimEnd('/');

    public bool IsMatch(string path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        return Match(_segments, 0, Normalize(path).Split('/'), 0);
    }

    private bool Match(string[] pattern, int pi, string[] path, int si)
    {
        while (pi < pattern.Length)
        {
            if (pattern[pi] == "**")
            {
                // "**" matches zero or more segments.
                if (pi + 1 == pattern.Length)
                {
                    return true;
                }

                for (var j = si; j <= path.Length; j++)
                {
                    if (Match(pattern, pi + 1, path, j))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (si >= path.Length)
            {
                return false;
            }

            if (!MatchSegment(pattern[pi], path[si]))
            {
                return false;
            }

            pi++;
            si++;
        }

        return si == path.Length;
    }

    private bool MatchSegment(string patternSeg, string pathSeg)
    {
        if (patternSeg == "*")
        {
            return pathSeg.Length > 0;
        }

        if (patternSeg.IndexOf('*') < 0 && patternSeg.IndexOf('?') < 0)
        {
            return string.Equals(patternSeg, pathSeg, _comparison);
        }

        return WildcardMatch(patternSeg, 0, pathSeg, 0);
    }

    private bool WildcardMatch(string p, int pi, string s, int si)
    {
        while (pi < p.Length)
        {
            var pc = p[pi];
            if (pc == '*')
            {
                if (pi + 1 == p.Length)
                {
                    return true;
                }

                for (var j = si; j <= s.Length; j++)
                {
                    if (WildcardMatch(p, pi + 1, s, j))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (si >= s.Length)
            {
                return false;
            }

            if (pc != '?' && string.Compare(p, pi, s, si, 1, _comparison) != 0)
            {
                return false;
            }

            pi++;
            si++;
        }

        return si == s.Length;
    }

    /// <summary>True when the path matches at least one pattern and none of the excludes.</summary>
    public static bool MatchesAny(IReadOnlyList<GlobMatcher> includes, IReadOnlyList<GlobMatcher> excludes, string path)
    {
        for (var i = 0; i < excludes.Count; i++)
        {
            if (excludes[i].IsMatch(path))
            {
                return false;
            }
        }

        for (var i = 0; i < includes.Count; i++)
        {
            if (includes[i].IsMatch(path))
            {
                return true;
            }
        }

        return false;
    }
}
