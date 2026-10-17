using System;
using System.Text;

namespace WpfDesignGuard.Core;

/// <summary>
/// Minimal JSONC preprocessing for designguard.json: strips <c>//</c> line comments,
/// <c>/* */</c> block comments and trailing commas while preserving every character
/// position (comment bytes become spaces, newlines survive) so JSON parse errors
/// keep pointing at the original line/column.
/// </summary>
public static class Jsonc
{
    public static string Strip(string source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        var sb = new StringBuilder(source.Length);
        var inString = false;
        var inLineComment = false;
        var inBlockComment = false;
        var escaped = false;

        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];

            if (inLineComment)
            {
                if (c == '\n')
                {
                    inLineComment = false;
                    sb.Append(c);
                }
                else
                {
                    sb.Append(' ');
                }
                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    inBlockComment = false;
                    sb.Append(' ');
                    i++; // consume '/'
                    sb.Append(' ');
                }
                else
                {
                    sb.Append(c == '\n' ? '\n' : ' ');
                }
                continue;
            }

            if (inString)
            {
                sb.Append(c);
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                else if (c == '\n')
                {
                    // Unterminated string literal with a raw newline: leave it to the JSON
                    // parser to reject (keeps error location truthful).
                    inString = false;
                }
                continue;
            }

            if (c == '"')
            {
                inString = true;
                sb.Append(c);
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                inLineComment = true;
                sb.Append(' ');
                i++;
                sb.Append(' ');
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                inBlockComment = true;
                sb.Append(' ');
                i++;
                sb.Append(' ');
                continue;
            }

            sb.Append(c);
        }

        return StripTrailingCommas(sb.ToString());
    }

    /// <summary>Removes <c>,</c> immediately before <c>}</c> or <c>]</c> (string-aware, position-preserving).</summary>
    private static string StripTrailingCommas(string source)
    {
        var sb = new StringBuilder(source.Length);
        var inString = false;
        var escaped = false;

        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];

            if (inString)
            {
                sb.Append(c);
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (c == '"')
            {
                inString = true;
                sb.Append(c);
                continue;
            }

            if (c == ',')
            {
                // Look ahead past whitespace for a closing bracket.
                var j = i + 1;
                while (j < source.Length && (source[j] == ' ' || source[j] == '\t' || source[j] == '\r' || source[j] == '\n'))
                {
                    j++;
                }

                if (j < source.Length && (source[j] == '}' || source[j] == ']'))
                {
                    sb.Append(' '); // keep column alignment
                    continue;
                }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
