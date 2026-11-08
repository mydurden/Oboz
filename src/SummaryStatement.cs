namespace Obozny;

/// <summary>Ключ рядка Узагальнюючої відомості (Додаток 1): найменування + О.В.</summary>
internal readonly record struct SummaryStatementKey(string Name, string Unit);

/// <summary>Рядок відомості.</summary>
internal sealed class SummaryStatementRow
{
    public string Name { get; init; } = "";
    public string Unit { get; init; } = "";
    public decimal InQty { get; init; }
    public decimal OutQty { get; init; }
    /// <summary>Наявність на кінець останнього дня звітного періоду.</summary>
    public decimal Balance { get; init; }
}

/// <summary>Готовий звіт Додатка 1 для перегляду на екрані та (за потреби) експорту.</summary>
internal sealed class SummaryStatementReport
{
    public string Number { get; init; } = "";
    public DateTime PeriodFrom { get; init; }
    public DateTime PeriodTo { get; init; }
    public DateTime CompiledAt { get; init; }
    public string UnitName { get; init; } = "";
    public string UserName { get; init; } = "";
    /// <summary>Служба забезпечення, за якою відфільтровано відомість.</summary>
    public string SupplyService { get; init; } = "";
    public IReadOnlyList<SummaryStatementRow> Rows { get; init; } = Array.Empty<SummaryStatementRow>();
}

/// <summary>
/// Узагальнююча відомість обліку військового майна в електронній формі (Додаток 1).
/// Агрегація проведених рухів; ціна не входить.
/// </summary>
internal static class SummaryStatement
{
    private static readonly StringComparer Uk = StringComparer.Create(
        new System.Globalization.CultureInfo("uk-UA"), ignoreCase: false);

    /// <summary>
    /// Календарні межі суцільного діапазону місяців одного року.
    /// Місяці — 1…12; має бути непорожній суцільний набір.
    /// </summary>
    public static bool TryPeriodFromMonths(
        int year,
        IEnumerable<int> months,
        out DateTime periodFrom,
        out DateTime periodTo,
        out string? error)
    {
        periodFrom = default;
        periodTo = default;
        error = null;
        var list = months.Distinct().OrderBy(m => m).ToList();
        if (list.Count == 0)
        {
            error = "Оберіть хоча б один місяць.";
            return false;
        }
        if (list.Any(m => m < 1 || m > 12))
        {
            error = "Номер місяця має бути від 1 до 12.";
            return false;
        }
        if (!IsContiguous(list))
        {
            error = "Оберіть суцільний діапазон.";
            return false;
        }
        if (year < 1900 || year > 2100)
        {
            error = "Некоректний рік.";
            return false;
        }

        var first = list[0];
        var last = list[^1];
        periodFrom = new DateTime(year, first, 1);
        periodTo = new DateTime(year, last, DateTime.DaysInMonth(year, last));
        return true;
    }

    /// <summary>Чи місяці утворюють суцільний діапазон (після сортування без дірок).</summary>
    public static bool IsContiguous(IEnumerable<int> months)
    {
        var list = months.Distinct().OrderBy(m => m).ToList();
        if (list.Count == 0) return false;
        for (var i = 1; i < list.Count; i++)
        {
            if (list[i] != list[i - 1] + 1)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Спробувати додати або зняти місяць, зберігаючи суцільність.
    /// Додавання — лише до краю (min−1 / max+1) або перший вибір.
    /// Зняття — лише з краю.
    /// </summary>
    public static bool TryToggleMonth(
        IReadOnlyCollection<int> selected,
        int month,
        bool wantSelected,
        out List<int> next,
        out string? error)
    {
        next = selected.Distinct().Where(m => m is >= 1 and <= 12).OrderBy(m => m).ToList();
        error = null;
        if (month is < 1 or > 12)
        {
            error = "Номер місяця має бути від 1 до 12.";
            return false;
        }

        var has = next.Contains(month);
        if (wantSelected == has)
            return true;

        if (wantSelected)
        {
            if (next.Count == 0)
            {
                next = [month];
                return true;
            }
            var min = next[0];
            var max = next[^1];
            if (month == min - 1 || month == max + 1)
            {
                next.Add(month);
                next.Sort();
                return true;
            }
            error = "Оберіть суцільний діапазон.";
            return false;
        }

        // Зняти
        if (next.Count == 1 && next[0] == month)
        {
            next = [];
            return true;
        }
        if (month == next[0] || month == next[^1])
        {
            next.Remove(month);
            return true;
        }
        error = "Зніміть крайній місяць.";
        return false;
    }

    public static SummaryStatementReport Build(
        IEnumerable<PropertyItem> properties,
        IEnumerable<Movement> movements,
        IEnumerable<Document> documents,
        DateTime periodFrom,
        DateTime periodTo,
        string? statementNumber = null,
        string? unitName = null,
        string? userName = null,
        DateTime? compiledAt = null,
        string? supplyService = null)
    {
        var from = periodFrom.Date;
        var to = periodTo.Date;
        var supply = SupplyServiceHelper.Normalize(supplyService) ?? "";
        if (from > to)
        {
            return new SummaryStatementReport
            {
                Number = (statementNumber ?? "").Trim(),
                PeriodFrom = from,
                PeriodTo = to,
                CompiledAt = (compiledAt ?? DateTime.Today).Date,
                UnitName = (unitName ?? "").Trim(),
                UserName = (userName ?? "").Trim(),
                SupplyService = supply,
                Rows = Array.Empty<SummaryStatementRow>()
            };
        }

        var propsById = properties
            .Where(p => p.Id != Guid.Empty)
            .Where(p => supply.Length == 0
                        || string.Equals(
                            SupplyServiceHelper.Normalize(p.SupplyService) ?? "",
                            supply,
                            StringComparison.Ordinal))
            .GroupBy(p => p.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var docsById = documents
            .GroupBy(d => d.Id)
            .ToDictionary(g => g.Key, g => g.First());

        // Name+Unit → накопичувачі
        var aggs = new Dictionary<SummaryStatementKey, Agg>(KeyComparer.Instance);

        foreach (var m in movements)
        {
            if (!IsPostedMovement(m, docsById)) continue;
            if (!propsById.TryGetValue(m.PropertyId, out var prop)) continue;

            var key = KeyOf(prop);
            if (key.Name.Length == 0) continue;

            if (!aggs.TryGetValue(key, out var agg))
            {
                agg = new Agg(key.Name, key.Unit);
                aggs[key] = agg;
            }

            var moveDate = ResolveMovementDate(m, docsById);
            // Баланс на кінець періоду: усі проведені рухи до «to» включно.
            if (moveDate > to) continue;

            if (m.QuantityDelta > 0)
            {
                agg.Balance += m.QuantityDelta;
                if (AccountingBook.InPeriod(moveDate, from, to))
                    agg.InQty += m.QuantityDelta;
            }
            else if (m.QuantityDelta < 0)
            {
                var outQty = -m.QuantityDelta;
                agg.Balance -= outQty;
                if (AccountingBook.InPeriod(moveDate, from, to))
                    agg.OutQty += outQty;
            }
        }

        var rows = new List<SummaryStatementRow>();
        foreach (var agg in aggs.Values)
        {
            var hadMove = agg.InQty != 0m || agg.OutQty != 0m;
            if (!hadMove && !HasNonZeroBalance(agg.Balance))
                continue;

            rows.Add(new SummaryStatementRow
            {
                Name = agg.Name,
                Unit = agg.Unit,
                InQty = agg.InQty,
                OutQty = agg.OutQty,
                Balance = agg.Balance
            });
        }

        rows.Sort((a, b) =>
        {
            var byName = Uk.Compare(a.Name, b.Name);
            if (byName != 0) return byName;
            return Uk.Compare(a.Unit, b.Unit);
        });

        return new SummaryStatementReport
        {
            Number = (statementNumber ?? "").Trim(),
            PeriodFrom = from,
            PeriodTo = to,
            CompiledAt = (compiledAt ?? DateTime.Today).Date,
            UnitName = (unitName ?? "").Trim(),
            UserName = (userName ?? "").Trim(),
            SupplyService = supply,
            Rows = rows
        };
    }

    public static SummaryStatementKey KeyOf(PropertyItem p) =>
        new((p.Name ?? "").Trim(), (p.Unit ?? "").Trim());

    /// <summary>Чи залишок показувати як наявний (не пилинка після округлення кількості).</summary>
    public static bool HasNonZeroBalance(decimal balance)
    {
        if (balance == 0m) return false;
        var places = Ua.QtyDecimals();
        if (places < 0) places = 0;
        if (places > 8) places = 8;
        var rounded = decimal.Round(balance, places, MidpointRounding.AwayFromZero);
        return rounded != 0m;
    }

    private static bool IsPostedMovement(
        Movement m,
        IReadOnlyDictionary<Guid, Document> docsById)
    {
        if (docsById.TryGetValue(m.DocumentId, out var doc))
            return doc.Status == DocStatus.Posted;
        return true;
    }

    private static DateTime ResolveMovementDate(
        Movement m,
        IReadOnlyDictionary<Guid, Document> docsById)
    {
        if (docsById.TryGetValue(m.DocumentId, out var doc))
            return doc.Date.Date;
        return m.DocumentDate.Date;
    }

    private sealed class Agg(string name, string unit)
    {
        public string Name { get; } = name;
        public string Unit { get; } = unit;
        public decimal InQty;
        public decimal OutQty;
        public decimal Balance;
    }

    private sealed class KeyComparer : IEqualityComparer<SummaryStatementKey>
    {
        public static readonly KeyComparer Instance = new();
        public bool Equals(SummaryStatementKey x, SummaryStatementKey y) =>
            string.Equals(x.Name, y.Name, StringComparison.Ordinal)
            && string.Equals(x.Unit, y.Unit, StringComparison.Ordinal);
        public int GetHashCode(SummaryStatementKey obj) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.Name),
                StringComparer.Ordinal.GetHashCode(obj.Unit));
    }
}
