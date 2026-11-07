namespace Obozny;

/// <summary>
/// Три служби забезпечення для картки майна / імпорту / книги обліку.
/// </summary>
internal static class SupplyServiceHelper
{
    public const string IsSsp = "ІС ССП";
    public const string Szu = "СЗУ";
    public const string Sovt = "СОВТ та МСВ";

    /// <summary>Три фіксовані служби (порядок у комбо — як у ТЗ).</summary>
    public static readonly IReadOnlyList<string> All = new[] { IsSsp, Szu, Sovt };

    public static bool IsKnown(string? service)
    {
        var t = (service ?? "").Trim();
        if (t.Length == 0) return false;
        return All.Any(s => string.Equals(s, t, StringComparison.Ordinal));
    }

    public static string? Normalize(string? raw)
    {
        var t = (raw ?? "").Trim();
        if (t.Length == 0) return null;
        return All.FirstOrDefault(s => string.Equals(s, t, StringComparison.Ordinal));
    }

    /// <summary>
    /// Автопідстановка служби з тексту поля «Передає».
    /// Точний збіг з однією з трьох → вона;
    /// «Інженерний склад» / «інженерна служба» (і подібні явні інженерні формулювання без іншої служби) → ІС ССП;
    /// інакше — null (користувач обирає вручну).
    /// </summary>
    public static string? MapFromPeredaye(string? raw)
    {
        var t = Compact(raw);
        if (t.Length == 0) return null;

        var exact = Normalize(t);
        if (exact != null) return exact;

        var n = Norm(t);

        // Підрядок відомої служби (напр. «СОВТ та МСВ, склад»).
        foreach (var s in All.OrderByDescending(x => x.Length))
        {
            if (n.Contains(Norm(s)))
                return s;
        }

        // Інженерні формулювання без іншої служби зі списку.
        if (LooksLikeEngineeringSupply(n))
            return IsSsp;

        return null;
    }

    private static bool LooksLikeEngineeringSupply(string n)
    {
        if (n.Length == 0) return false;
        // «інженерний склад», «інженерна служба», «інженерна служба забезпечення» тощо.
        if (n.Contains("інженерн") && (n.Contains("склад") || n.Contains("служб")))
            return true;
        return false;
    }

    private static string Norm(string s)
    {
        var t = (s ?? "").Replace('\n', ' ').Replace('\r', ' ').ToLowerInvariant();
        while (t.Contains("  ")) t = t.Replace("  ", " ");
        return t.Trim();
    }

    private static string Compact(string? s)
    {
        var t = (s ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
        while (t.Contains("  ")) t = t.Replace("  ", " ");
        return t;
    }
}
