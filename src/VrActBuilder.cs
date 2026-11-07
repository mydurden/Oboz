namespace Obozny;

/// <summary>
/// Акт ВР як перегляд/друк зведеного майна з акта ЯС або ЗЯС (не документ руху, без вартості).
/// Номер ВР не вигадуємо; дата ВР = дата основного акта.
/// </summary>
internal static class VrActBuilder
{
    public const string NoData = "н/д";

    /// <summary>Парний Акт ВР є для ЯС (форт/ОП) і ЗЯС (обладнання НВЗ).</summary>
    public static bool IsPairedSource(Document? doc) =>
        doc is { Type: DocType.QualityState or DocType.QualityChange };

    public static IReadOnlyList<Document> SourceDocuments(StrongpointRow row) =>
        AppStore.Instance.QualityStateDocumentsFor(row);

    public static Document? PrimaryDocument(StrongpointRow row) =>
        SourceDocuments(row)
            .OrderByDescending(d => d.Status == DocStatus.Posted)
            .ThenByDescending(d => d.Date)
            .ThenByDescending(d => d.CreatedAt)
            .FirstOrDefault();

    /// <summary>Показ номера Акта ВР: порожньо → «н/д» (не «б/н», не автоген).</summary>
    public static string DisplayVrNumber(Document? source) =>
        string.IsNullOrWhiteSpace(source?.VrActNumber) ? NoData : source!.VrActNumber.Trim();

    public static DateTime DateFor(Document source) => source.Date.Date;

    public static List<VrActLineRow> AggregateLines(Document source) =>
        AggregateLines(new[] { source });

    public static List<VrActLineRow> AggregateLines(IReadOnlyList<Document> docs)
    {
        var props = AppStore.Instance.Data.Properties.ToDictionary(p => p.Id);
        var summed = new Dictionary<string, (string Name, string Unit, decimal Qty)>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in docs)
        {
            foreach (var line in doc.Lines.Where(l => l.Quantity > 0 && !string.IsNullOrWhiteSpace(l.Name)))
            {
                props.TryGetValue(line.PropertyId, out var p);
                var name = line.Name.Trim();
                var unit = string.IsNullOrWhiteSpace(line.Unit) ? (p?.Unit ?? "").Trim() : line.Unit.Trim();
                var key = name.ToLowerInvariant() + "\0" + unit.ToLowerInvariant();
                if (summed.TryGetValue(key, out var have))
                    summed[key] = (have.Name, have.Unit, have.Qty + line.Quantity);
                else
                    summed[key] = (name, unit, line.Quantity);
            }
        }

        return summed.Values
            .OrderBy(x => x.Name, StringComparer.Create(Ua.Culture, ignoreCase: true))
            .ThenBy(x => x.Unit, StringComparer.Create(Ua.Culture, ignoreCase: true))
            .Select(x => new VrActLineRow
            {
                Name = x.Name,
                Unit = x.Unit,
                Quantity = x.Qty,
                QuantityText = Ua.Num(x.Qty, "#,##0.###")
            })
            .ToList();
    }

    /// <summary>
    /// Номери накладних (отримання), за якими на склад зайшли матеріали цього акта.
    /// Немає номерів → «н/д».
    /// </summary>
    public static string InvoiceNumbers(Document source)
    {
        var propIds = source.Lines
            .Where(l => l.Quantity > 0)
            .Select(l => l.PropertyId)
            .ToHashSet();
        if (propIds.Count == 0) return NoData;

        var nums = AppStore.Instance.Data.Documents
            .Where(d => d.Type == DocType.Receipt && d.Status == DocStatus.Posted)
            .Where(d => d.Lines.Any(l => propIds.Contains(l.PropertyId)))
            .Select(d => (d.Number ?? "").Trim())
            .Where(n => n.Length > 0
                        && !string.Equals(n, DocTypeNames.Numberless, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.Create(Ua.Culture, ignoreCase: true))
            .ToList();

        return nums.Count == 0 ? NoData : string.Join(", ", nums);
    }

    public static string InvoicesSentence(Document source) =>
        source.Type == DocType.QualityChange
            ? "Для обладнання невибухового інженерного загородження використано матеріали отримані згідно накладних №№ : "
              + InvoiceNumbers(source)
            : "Для обладнання опорного пункту (фортифікаційних споруд) використано матеріали отримані згідно накладних №№ : "
              + InvoiceNumbers(source);

    /// <summary>Рядки шапки перегляду/друку Акта ВР (ОП або загородження НВЗ).</summary>
    public static IEnumerable<string> HeaderLines(Document source, StrongpointRow? row = null)
    {
        if (source.Type == DocType.QualityChange)
        {
            yield return $"Назва інженерного загородження: {Empty(source.BarrierName)}";
            yield return $"Серія формуляру: {Empty(source.FormSeries)}";
            yield return $"Номер формуляру: {Empty(source.FormNumber)}";
        }
        else
        {
            var name = row != null
                ? StrongpointNameUi.BaseName(row.Name)
                : StrongpointNameUi.BaseName(source.StrongpointName ?? "");
            var reg = row != null ? row.RegNumber : source.StrongpointRegNumber;
            yield return $"Опорний пункт: {(string.IsNullOrWhiteSpace(name) ? "—" : name)}";
            yield return $"Реєстраційний номер: {Empty(reg)}";
        }

        yield return $"Дата: {DateFor(source):dd.MM.yyyy}";
        yield return InvoicesSentence(source);
    }

    private static string Empty(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s.Trim();

    public static List<StrongpointDocLinkRow> DocumentLinks(StrongpointRow row)
    {
        var docs = SourceDocuments(row);
        var links = new List<StrongpointDocLinkRow>();
        foreach (var d in docs
                     .OrderByDescending(x => x.Date)
                     .ThenByDescending(x => x.CreatedAt))
        {
            links.Add(new StrongpointDocLinkRow
            {
                Kind = "Акт ЯС",
                Number = DocTypeNames.FormatNumber(d.Number),
                DateText = d.Date.ToString("dd.MM.yyyy"),
                DocumentId = d.Id,
                IsVrAct = false
            });
            links.Add(new StrongpointDocLinkRow
            {
                Kind = "Акт ВР",
                Number = DisplayVrNumber(d),
                DateText = d.Date.ToString("dd.MM.yyyy"),
                DocumentId = d.Id,
                IsVrAct = true
            });
        }
        return links;
    }
}
