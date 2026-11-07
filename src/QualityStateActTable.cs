namespace Obozny;

/// <summary>
/// Рядки таблиці акта якісного стану у форматі зразка:
/// заголовок фортспоруди (з кількістю) → перелік майна під нею.
/// </summary>
internal static class QualityStateActTable
{
    public sealed record Row(
        bool IsStructure,
        Guid? PropertyId,
        string Name,
        string Unit,
        string Category,
        string QuantityText,
        decimal? QuantityValue,
        string PriceText,
        decimal? PriceValue);

    public static List<Row> Build(Document doc)
    {
        var props = AppStore.Instance.Data.Properties.ToDictionary(p => p.Id);
        var ordered = doc.Structures.OrderBy(s => s.Order).ToList();
        var lines = doc.Lines.ToList();
        var result = new List<Row>();

        foreach (var s in ordered)
        {
            var (title, count) = FortStructureNaming.Parse(s.Name);
            result.Add(new Row(
                IsStructure: true,
                PropertyId: null,
                Name: title,
                Unit: "",
                Category: "",
                QuantityText: count.ToString(),
                QuantityValue: count,
                PriceText: "",
                PriceValue: null));

            foreach (var line in lines.Where(l => l.StructureId == s.Id))
            {
                props.TryGetValue(line.PropertyId, out var p);
                var cat = p?.Category ?? "";
                var price = DocumentViewWindow.ResolvePrice(line, props);
                result.Add(new Row(
                    IsStructure: false,
                    PropertyId: line.PropertyId,
                    Name: line.Name,
                    Unit: line.Unit,
                    Category: cat,
                    QuantityText: Ua.Num(line.Quantity, "#,##0.###"),
                    QuantityValue: line.Quantity,
                    PriceText: price > 0 ? Ua.FormatPrice(price) : "",
                    PriceValue: price > 0 ? price : null));
            }
        }

        // рядки без прив’язки до споруди
        var orphan = lines.Where(l => l.StructureId == null
                                      || ordered.All(s => s.Id != l.StructureId)).ToList();
        if (orphan.Count > 0)
        {
            result.Add(new Row(true, null, "Без споруди", "", "", "", null, "", null));
            foreach (var line in orphan)
            {
                props.TryGetValue(line.PropertyId, out var p);
                var price = DocumentViewWindow.ResolvePrice(line, props);
                result.Add(new Row(
                    false,
                    line.PropertyId,
                    line.Name,
                    line.Unit,
                    p?.Category ?? "",
                    Ua.Num(line.Quantity, "#,##0.###"),
                    line.Quantity,
                    price > 0 ? Ua.FormatPrice(price) : "",
                    price > 0 ? price : null));
            }
        }

        return result;
    }
}
