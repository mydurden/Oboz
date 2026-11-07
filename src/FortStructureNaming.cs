using System.Text.RegularExpressions;

namespace Obozny;

/// <summary>
/// Кількість фортспоруд зчитується з назви, як пише користувач:
/// «Бліндаж 3x3x2 м (2)», «… — 2», «… × 2», «… 2 шт».
/// </summary>
internal static class FortStructureNaming
{
    public static (string Title, int Count) Parse(string? raw)
    {
        var name = (raw ?? "").Trim();
        if (name.Length == 0) return ("без назви", 1);

        // (2) або (2 шт)
        var m = Regex.Match(name,
            @"^(?<t>.+?)\s*\((?<n>\d+)\s*(?:шт|од)?\)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (Try(m, out var parsed)) return parsed;

        // — 2 / – 2 / - 2 [шт]
        m = Regex.Match(name,
            @"^(?<t>.+?)\s+[\-–—]\s+(?<n>\d+)\s*(?:шт|од)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (Try(m, out parsed)) return parsed;

        // × 2 / х 2 (кирилиця) в кінці — не плутати з розмірами 3x3x2
        m = Regex.Match(name,
            @"^(?<t>.+?)\s+[×х]\s*(?<n>\d+)\s*(?:шт|од)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (Try(m, out parsed)) return parsed;

        // 2 шт / 2 од в кінці
        m = Regex.Match(name,
            @"^(?<t>.+?)\s+(?<n>\d+)\s*(?:шт|од)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (Try(m, out parsed)) return parsed;

        return (name, 1);
    }

    private static bool Try(Match m, out (string Title, int Count) result)
    {
        result = default;
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups["n"].Value, out var n) || n <= 0) return false;
        var title = m.Groups["t"].Value.Trim();
        if (title.Length == 0) return false;
        result = (title, n);
        return true;
    }
}
