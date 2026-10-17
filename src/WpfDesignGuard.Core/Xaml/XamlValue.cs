using System;
using System.Globalization;

namespace WpfDesignGuard.Core.Xaml;

/// <summary>Value-literal judgements shared by rules.</summary>
public static class XamlValue
{
    /// <summary>
    /// True when the value must be skipped as a literal: it starts with <c>{</c>
    /// (markup extension such as <c>{DynamicResource …}</c> / <c>{Binding X, StringFormat=…}</c>)
    /// or with the <c>{}</c> escape prefix, which marks a literal that must not be re-interpreted.
    /// </summary>
    public static bool IsMarkupExtension(string? value)
    {
        var trimmed = value?.TrimStart();
        return trimmed is { Length: > 0 } && trimmed[0] == '{';
    }

    /// <summary>True when the trimmed value parses as a plain number (invariant culture).</summary>
    public static bool IsNumber(string? value, out double number)
    {
        number = 0;
        if (value?.Trim() is not { Length: > 0 } trimmed)
        {
            return false;
        }

        return double.TryParse(
            trimmed.TrimEnd('f', 'F', 'd', 'D', 'm', 'M'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out number);
    }

    /// <summary>
    /// Parses a WPF Thickness grammar literal ("8", "8,4", "8,4,8,4" — also space separated).
    /// Returns the components, or null when the value is not a numeric Thickness literal
    /// (markup extensions and single-component named values are not literals).
    /// </summary>
    public static double[]? TryParseThickness(string? value)
    {
        if (value?.Trim() is not { Length: > 0 } trimmed || IsMarkupExtension(trimmed))
        {
            return null;
        }

        var parts = trimmed.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 1 && parts.Length != 2 && parts.Length != 4)
        {
            return null; // WPF Thickness grammar accepts 1, 2 or 4 components only
        }

        var numbers = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!IsNumber(parts[i], out var n))
            {
                return null;
            }

            numbers[i] = n;
        }

        return numbers;
    }
}
