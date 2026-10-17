using System;
using System.IO;

namespace WpfDesignGuard.Analyzers;

/// <summary>Path helpers (netstandard2.0 has no Path.GetRelativePath).</summary>
internal static class PathUtilities
{
    /// <summary>
    /// Path of <paramref name="filePath"/> relative to <paramref name="baseDir"/>,
    /// normalized to '/' separators. Comparison is case-insensitive on Windows
    /// (path separator '\'), ordinal elsewhere.
    /// </summary>
    public static string MakeRelative(string baseDir, string filePath)
    {
        var b = Normalize(baseDir);
        var f = Normalize(filePath);
        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!f.StartsWith(b, comparison))
        {
            // Outside the base: fall back to the file name (deterministic, reportable).
            return f.Substring(f.LastIndexOf('/') + 1);
        }

        var rel = f.Substring(Math.Min(b.Length + 1, f.Length));
        return rel.Length == 0 ? f.Substring(f.LastIndexOf('/') + 1) : rel;
    }

    public static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');
}
