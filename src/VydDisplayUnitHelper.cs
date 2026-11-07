namespace Obozny;

/// <summary>О.В. відображення для Виду та унікальні офіційні О.В. з карток.</summary>
internal static class VydDisplayUnitHelper
{
    private static readonly StringComparer Uk = StringComparer.Create(
        new System.Globalization.CultureInfo("uk-UA"), ignoreCase: false);

    public const string AddUnitItem = "+ Додати О.В.";

    public static bool IsAddItem(string? value) =>
        string.Equals((value ?? "").Trim(), AddUnitItem, StringComparison.Ordinal);

    public static IReadOnlyList<string> UnitsForVyd(
        IEnumerable<VydDisplayUnitEntry>? entries,
        string? vyd)
    {
        var key = (vyd ?? "").Trim();
        if (key.Length == 0 || entries == null) return Array.Empty<string>();
        var entry = entries.FirstOrDefault(e =>
            string.Equals((e.Vyd ?? "").Trim(), key, StringComparison.Ordinal));
        if (entry?.Units == null || entry.Units.Count == 0) return Array.Empty<string>();
        return entry.Units
            .Select(u => (u ?? "").Trim())
            .Where(u => u.Length > 0 && !IsAddItem(u))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(u => u, Uk)
            .ToList();
    }

    /// <summary>
    /// Унікальні офіційні О.В. з карток номенклатури, відмінні від уже заданих для виду
    /// і від щойно введеного (щоб не дублювати в списку).
    /// </summary>
    public static IReadOnlyList<string> CandidateUnitsFromBase(
        IEnumerable<NomenclatureItem> nomenclatures,
        IEnumerable<string> alreadyForVyd,
        string? typedOwn)
    {
        var skip = new HashSet<string>(StringComparer.Ordinal);
        foreach (var u in alreadyForVyd)
        {
            var t = (u ?? "").Trim();
            if (t.Length > 0) skip.Add(t);
        }
        var typed = (typedOwn ?? "").Trim();
        if (typed.Length > 0) skip.Add(typed);

        return nomenclatures
            .Select(n => (n.Unit ?? "").Trim())
            .Where(u => u.Length > 0 && !skip.Contains(u))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(u => u, Uk)
            .ToList();
    }

    public static string? NormalizeUnitName(string? raw, IEnumerable<string> alreadyForVyd)
    {
        var t = (raw ?? "").Trim();
        if (t.Length == 0 || IsAddItem(t)) return null;
        if (alreadyForVyd.Any(u => string.Equals((u ?? "").Trim(), t, StringComparison.Ordinal)))
            return null;
        return t;
    }

    public static IReadOnlyDictionary<string, string> ToDisplayMap(
        IEnumerable<VydSummaryDisplayChoice>? choices)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (choices == null) return map;
        foreach (var c in choices)
        {
            var vyd = (c.Vyd ?? "").Trim();
            var unit = (c.DisplayUnit ?? "").Trim();
            if (vyd.Length == 0 || unit.Length == 0 || IsAddItem(unit)) continue;
            map[vyd] = unit;
        }
        return map;
    }

    public static List<VydSummaryDisplayChoice> FromDisplayMap(IReadOnlyDictionary<string, string> map) =>
        map
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => new VydSummaryDisplayChoice { Vyd = kv.Key.Trim(), DisplayUnit = kv.Value.Trim() })
            .OrderBy(c => c.Vyd, Uk)
            .ToList();
}
