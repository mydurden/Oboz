namespace Obozny;

/// <summary>Рядок зведення залишків по Виду (і од. вим., якщо в межах Виду їх кілька).</summary>
internal readonly record struct StockSummaryRow(string Vyd, string Unit, decimal Quantity);

/// <summary>
/// Зведення поточних залишків для стікера: лише номенклатура з обраним Видом і кількістю &gt; 0.
/// Майно без Виду не входить. Різні од. вим. у межах Виду — окремі рядки (не змішуються).
/// Якщо для Виду обрана О.В. відображення — сума через коефіцієнти; без коефіцієнта позицію не беремо.
/// </summary>
internal static class StockSummaryByVyd
{
    private static readonly StringComparer Uk = StringComparer.Create(
        new System.Globalization.CultureInfo("uk-UA"), ignoreCase: false);

    /// <summary>
    /// Фільтр увімкнених видів: <c>null</c> у збережених даних → усі з каталогу (фікс + custom);
    /// порожній список → нічого не показувати.
    /// </summary>
    public static IReadOnlySet<string> ResolveEnabledVyds(
        IReadOnlyList<string>? stored,
        IEnumerable<string>? customVyds = null)
    {
        if (stored == null)
            return new HashSet<string>(NomenclatureVydCatalog.Combined(customVyds), StringComparer.Ordinal);
        return new HashSet<string>(
            stored.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()),
            StringComparer.Ordinal);
    }

    /// <summary><c>true</c>, якщо автор зняв усі галочки (збережений порожній список).</summary>
    public static bool AllVydsDisabled(IReadOnlyList<string>? stored) =>
        stored is { Count: 0 };

    public static IReadOnlyList<StockSummaryRow> Compute(
        IEnumerable<PropertyItem> properties,
        IReadOnlyDictionary<Guid, NomenclatureItem> nomenclaturesById,
        IReadOnlyList<string>? enabledVyds = null,
        IEnumerable<string>? customVyds = null,
        IReadOnlyDictionary<string, string>? displayUnitByVyd = null)
    {
        var filter = ResolveEnabledVyds(enabledVyds, customVyds);
        var totals = new Dictionary<(string Vyd, string Unit), decimal>();
        foreach (var p in properties)
        {
            if (p.Quantity <= 0) continue;
            if (p.NomenclatureId is not Guid nid || nid == Guid.Empty) continue;
            if (!nomenclaturesById.TryGetValue(nid, out var nom)) continue;
            var vyd = (nom.Vyd ?? "").Trim();
            if (vyd.Length == 0) continue;
            if (!filter.Contains(vyd)) continue;

            string unit;
            decimal qty;
            if (displayUnitByVyd != null
                && displayUnitByVyd.TryGetValue(vyd, out var chosenRaw)
                && !string.IsNullOrWhiteSpace(chosenRaw))
            {
                var chosen = chosenRaw.Trim();
                var itemDisp = (nom.DisplayUnit ?? "").Trim();
                if (!string.Equals(itemDisp, chosen, StringComparison.Ordinal)) continue;
                if (nom.DisplayFactor is not decimal factor || factor <= 0) continue;
                unit = chosen;
                qty = p.Quantity * factor;
            }
            else
            {
                unit = string.IsNullOrWhiteSpace(p.Unit)
                    ? (string.IsNullOrWhiteSpace(nom.Unit) ? "шт" : nom.Unit.Trim())
                    : p.Unit.Trim();
                qty = p.Quantity;
            }

            if (qty <= 0) continue;
            var key = (vyd, unit);
            totals[key] = totals.TryGetValue(key, out var q) ? q + qty : qty;
        }

        return totals
            .Where(kv => kv.Value > 0)
            .OrderBy(kv => kv.Key.Vyd, Uk)
            .ThenBy(kv => kv.Key.Unit, Uk)
            .Select(kv => new StockSummaryRow(kv.Key.Vyd, kv.Key.Unit, kv.Value))
            .ToList();
    }

    /// <summary>Лише кількість (од. вим. — окрема колонка стікера).</summary>
    public static string FormatQty(decimal quantity) => Ua.FormatQty(quantity);

    /// <summary>Застарілий формат «кількість + од. вим.» в одному рядку (тести / сумісність).</summary>
    public static string FormatQtyWithUnit(decimal quantity, string unit) =>
        $"{FormatQty(quantity)} {(unit ?? "").Trim()}".TrimEnd();
}
