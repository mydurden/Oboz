namespace Obozny;

/// <summary>
/// Інші позиції номенклатури без Виду (для діалогу масового призначення).
/// Поточну картку виключаємо — їй Вид уже обрали в комбо.
/// </summary>
internal static class NomenclatureWithoutVyd
{
    public static IReadOnlyList<NomenclatureItem> Others(
        IEnumerable<NomenclatureItem> all,
        Guid? excludeId)
    {
        return all
            .Where(n => excludeId is not Guid id || n.Id != id)
            .Where(n => string.IsNullOrWhiteSpace(n.Vyd))
            .OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(n => n.Unit, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
