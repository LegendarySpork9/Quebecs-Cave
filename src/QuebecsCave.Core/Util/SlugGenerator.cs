using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace QuebecsCave.Core.Util;

public static class SlugGenerator
{
    /// <summary>
    /// Convert a free-text name into a URL-safe slug. Strips diacritics,
    /// lowercases, replaces non-alphanumeric runs with single hyphens.
    /// Example: "Stardew Valley: Year One" → "stardew-valley-year-one".
    /// </summary>
    public static string Slugify(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        var normalized = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }
        var stripped = sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        var slug = Regex.Replace(stripped, @"[^a-z0-9]+", "-").Trim('-');
        return slug;
    }
}
