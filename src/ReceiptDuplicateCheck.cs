namespace Obozny;

/// <summary>
/// Перед збереженням приймання (надходження, <see cref="DocType.Receipt"/>)
/// звіряє рядки з уже збереженими документами приймання.
/// </summary>
internal static class ReceiptDuplicateCheck
{
    public enum MatchKind
    {
        /// <summary>Збіг номера документа і назви майна; кількість і/або ціна інші.</summary>
        Soft,
        /// <summary>Повний збіг: номер, назва, кількість і ціна.</summary>
        Exact
    }

    public sealed record Hit(MatchKind Kind, string Number, string Name);

    private const decimal QtyEps = 0.0001m;
    private const decimal PriceEps = 0.05m;

    /// <summary>
    /// Знаходить збіги incoming-рядків приймання з уже збереженими документами Receipt.
    /// Порожній номер документа пропускаємо (при збереженні буде новий автономер).
    /// </summary>
    public static List<Hit> Find(IEnumerable<ImportRow> incoming, IEnumerable<Document> documents)
    {
        var existing = documents
            .Where(d => d.Type == DocType.Receipt)
            .SelectMany(d => d.Lines.Select(line => new ExistingLine(
                NormNumber(d.Number),
                DisplayNumber(d.Number),
                NormName(line.Name),
                line.Name?.Trim() ?? "",
                line.Quantity,
                line.UnitPrice)))
            .Where(e => e.NumberKey.Length > 0 && e.NameKey.Length > 0)
            .ToList();

        if (existing.Count == 0) return new List<Hit>();

        // Один хит на пару (рівень, номер, назва) — без дублів у діалозі.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hits = new List<Hit>();

        foreach (var row in incoming)
        {
            if (row.Quantity <= 0 || string.IsNullOrWhiteSpace(row.Name)) continue;
            var resolved = ImportGrouping.Resolve(row);
            if (resolved.Type != DocType.Receipt) continue;

            var numKey = NormNumber(resolved.Number);
            if (numKey.Length == 0) continue;
            var nameKey = NormName(row.Name);
            if (nameKey.Length == 0) continue;

            MatchKind? best = null;
            string displayNum = DisplayNumber(resolved.Number);
            string displayName = row.Name.Trim();

            foreach (var ex in existing)
            {
                if (ex.NumberKey != numKey || ex.NameKey != nameKey) continue;
                var qtySame = Math.Abs(ex.Quantity - row.Quantity) < QtyEps;
                var priceSame = Math.Abs(ex.UnitPrice - row.Price) < PriceEps;
                if (qtySame && priceSame)
                {
                    best = MatchKind.Exact;
                    displayNum = ex.DisplayNumber;
                    if (!string.IsNullOrWhiteSpace(ex.DisplayName))
                        displayName = ex.DisplayName;
                    break;
                }
                best ??= MatchKind.Soft;
                displayNum = ex.DisplayNumber;
                if (!string.IsNullOrWhiteSpace(ex.DisplayName))
                    displayName = ex.DisplayName;
            }

            if (best == null) continue;
            var key = $"{(int)best}|{numKey}|{nameKey}";
            if (!seen.Add(key)) continue;
            // Exact витісняє Soft для тієї ж пари номер+назва.
            if (best == MatchKind.Exact)
            {
                hits.RemoveAll(h =>
                    h.Kind == MatchKind.Soft
                    && NormNumber(h.Number) == numKey
                    && NormName(h.Name) == nameKey);
                var softKey = $"{(int)MatchKind.Soft}|{numKey}|{nameKey}";
                seen.Add(softKey);
            }
            hits.Add(new Hit(best.Value, displayNum, displayName));
        }

        return hits;
    }

    public static string BuildConfirmText(IReadOnlyList<Hit> hits)
    {
        var lines = new List<string>();
        foreach (var hit in hits)
        {
            if (hit.Kind == MatchKind.Exact)
            {
                lines.Add(
                    $"• Майно «{hit.Name}» уже було раніше прийнято за документом № {hit.Number} " +
                    "(та сама кількість і ціна).");
            }
            else
            {
                lines.Add(
                    $"• Номер документа «{hit.Number}» і майно «{hit.Name}» уже були в прийманні " +
                    "(кількість і/або ціна відрізняються).");
            }
        }

        return "Знайдено збіги з уже збереженим прийманням:\n\n"
               + string.Join("\n", lines)
               + "\n\nЗберегти все одно?";
    }

    private static string NormNumber(string? number)
    {
        var t = (number ?? "").Trim().ToLowerInvariant();
        while (t.Contains("  ")) t = t.Replace("  ", " ");
        return t;
    }

    private static string DisplayNumber(string? number) =>
        DocTypeNames.FormatNumber(number);

    private static string NormName(string? name)
    {
        var t = (name ?? "").Trim().ToLowerInvariant();
        while (t.Contains("  ")) t = t.Replace("  ", " ");
        return t;
    }

    private readonly record struct ExistingLine(
        string NumberKey,
        string DisplayNumber,
        string NameKey,
        string DisplayName,
        decimal Quantity,
        decimal UnitPrice);
}
