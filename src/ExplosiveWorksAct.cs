namespace Obozny;

/// <summary>Рядок таблиці Акта списання вибухових матеріалів (Додаток 16).</summary>
internal sealed class ExplosiveWorksActRow
{
    public int Number { get; init; }
    public string Name { get; init; } = "";
    public string Unit { get; init; } = "";
    /// <summary>Загальна кількість Name+Unit в обліку (з урахуванням статусу документа).</summary>
    public decimal ReceivedQty { get; init; }
    /// <summary>Витрати за рядком акта.</summary>
    public decimal SpentQty { get; init; }
    /// <summary>Підлягає здаванню = отримано − витрати.</summary>
    public decimal ReturnQty { get; init; }
}

/// <summary>
/// Дані Додатка 16: витрати з рядків документа; отримано/здати — з обліку Name+Unit;
/// номери накладних — FIFO як у Акта ВР.
/// </summary>
internal static class ExplosiveWorksAct
{
    private static readonly StringComparer Uk = StringComparer.Create(
        new System.Globalization.CultureInfo("uk-UA"), ignoreCase: false);

    public static IReadOnlyList<ExplosiveWorksActRow> Build(Document doc)
    {
        var props = AppStore.Instance.Data.Properties;
        var stockByKey = new Dictionary<(string Name, string Unit), decimal>();
        foreach (var p in props)
        {
            var name = (p.Name ?? "").Trim();
            if (name.Length == 0) continue;
            var unit = (p.Unit ?? "").Trim();
            var key = (name, unit);
            stockByKey.TryGetValue(key, out var q);
            stockByKey[key] = q + p.Quantity;
        }

        var spentByKey = new Dictionary<(string Name, string Unit), decimal>();
        foreach (var line in doc.Lines.Where(l => l.Quantity > 0))
        {
            var name = (line.Name ?? "").Trim();
            if (name.Length == 0) continue;
            var unit = (line.Unit ?? "").Trim();
            var key = (name, unit);
            spentByKey.TryGetValue(key, out var q);
            spentByKey[key] = q + line.Quantity;
        }

        // Один рядок на Name+Unit (як у зразку — позиція боєприпасу).
        var keys = spentByKey.Keys
            .Concat(doc.Lines
                .Where(l => !string.IsNullOrWhiteSpace(l.Name))
                .Select(l => ((l.Name ?? "").Trim(), (l.Unit ?? "").Trim())))
            .Distinct()
            .OrderBy(k => k.Item1, Uk)
            .ThenBy(k => k.Item2, Uk)
            .ToList();

        var rows = new List<ExplosiveWorksActRow>();
        var n = 1;
        foreach (var key in keys)
        {
            if (key.Item1.Length == 0) continue;
            stockByKey.TryGetValue(key, out var stock);
            spentByKey.TryGetValue(key, out var spent);
            if (spent == 0m && stock == 0m) continue;
            // Чернетка: склад ще з витратою; проведений — склад уже без витрати документа.
            var received = doc.Status == DocStatus.Posted
                ? stock + spent
                : stock;
            rows.Add(new ExplosiveWorksActRow
            {
                Number = n++,
                Name = key.Item1,
                Unit = key.Item2,
                ReceivedQty = received,
                SpentQty = spent,
                ReturnQty = received - spent
            });
        }

        return rows;
    }

    /// <summary>Номери накладних отримання за FIFO під кількість рядків акта.</summary>
    public static string InvoiceNumbers(Document doc) => VrActBuilder.InvoiceNumbers(doc);

    public static IReadOnlyList<(string Number, DateTime Date)> InvoiceDetails(Document doc)
    {
        var remaining = doc.Lines
            .Where(l => l.Quantity > 0)
            .GroupBy(l => l.PropertyId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        if (remaining.Count == 0) return Array.Empty<(string, DateTime)>();

        var receipts = AppStore.Instance.Data.Documents
            .Where(d => d.Type == DocType.Receipt && d.Status == DocStatus.Posted)
            .OrderBy(d => d.Date)
            .ThenBy(d => d.CreatedAt)
            .ToList();

        var list = new List<(string Number, DateTime Date)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var receipt in receipts)
        {
            if (remaining.Values.All(q => q <= 0)) break;

            var took = false;
            foreach (var line in receipt.Lines.Where(l => l.Quantity > 0))
            {
                if (!remaining.TryGetValue(line.PropertyId, out var need) || need <= 0)
                    continue;
                var take = Math.Min(need, line.Quantity);
                if (take <= 0) continue;
                remaining[line.PropertyId] = need - take;
                took = true;
            }

            if (!took) continue;

            var n = (receipt.Number ?? "").Trim();
            if (n.Length == 0
                || string.Equals(n, DocTypeNames.Numberless, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!seen.Add(n)) continue;
            list.Add((n, receipt.Date.Date));
        }

        return list;
    }
}
