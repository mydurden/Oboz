namespace Obozny;

/// <summary>
/// Ключ колонки майна в Книзі обліку (Додаток 14) у межах Виду:
/// Name + Category + UnitPrice.
/// </summary>
internal readonly record struct AccountingBookColumnKey(string Name, string Category, decimal UnitPrice)
{
    public static AccountingBookColumnKey From(PropertyItem p) =>
        new(
            (p.Name ?? "").Trim(),
            (p.Category ?? "").Trim(),
            p.UnitPrice);

    public string DisplayTitle
    {
        get
        {
            if (Category.Length == 0) return Name;
            return $"{Name} (кат. {Category})";
        }
    }
}

/// <summary>Колонка позиції в розділі Виду.</summary>
internal sealed class AccountingBookColumn
{
    public AccountingBookColumnKey Key { get; init; }
    public string Unit { get; init; } = "шт";
    public IReadOnlyList<Guid> PropertyIds { get; init; } = Array.Empty<Guid>();
}

/// <summary>Трійка надійшло / вибуло / становить для однієї колонки в рядку.</summary>
internal sealed class AccountingBookCell
{
    public decimal? InQty { get; init; }
    public decimal? OutQty { get; init; }
    /// <summary>Наявність на складі після рядка (включно з ReservedQuantity у поточній моделі).</summary>
    public decimal Balance { get; init; }
}

/// <summary>Рядок хронології проведених документів.</summary>
internal sealed class AccountingBookRow
{
    public DateTime RecordDate { get; init; }
    public string DocumentTitle { get; init; } = "";
    public string NumberAndDate { get; init; } = "";
    public string Counterparty { get; init; } = "";
    public Guid DocumentId { get; init; }
    /// <summary>
    /// Усі колонки розділу: без руху в рядку — In/Out null (прочерк у UI),
    /// Balance завжди = залишок на дату цієї проводки.
    /// </summary>
    public IReadOnlyDictionary<AccountingBookColumnKey, AccountingBookCell> Cells { get; init; }
        = new Dictionary<AccountingBookColumnKey, AccountingBookCell>();
}

/// <summary>Розділ книги для одного Виду.</summary>
internal sealed class AccountingBookSection
{
    public string Vyd { get; init; } = "";
    public IReadOnlyList<AccountingBookColumn> Columns { get; init; } = Array.Empty<AccountingBookColumn>();
    public IReadOnlyList<AccountingBookRow> Rows { get; init; } = Array.Empty<AccountingBookRow>();
    public bool IsEmpty => Columns.Count == 0 && Rows.Count == 0;
}

/// <summary>
/// Побудова перегляду Книги обліку військового майна (Додаток 14).
/// Лише проведені рухи; резерв не пишеться як вибуло.
/// </summary>
internal static class AccountingBook
{
    private static readonly StringComparer Uk = StringComparer.Create(
        new System.Globalization.CultureInfo("uk-UA"), ignoreCase: false);

    /// <summary>За замовчуванням «Від» у Змісті: 1 січня поточного року.</summary>
    public static DateTime DefaultPeriodFrom(DateTime? today = null)
    {
        var t = (today ?? DateTime.Today).Date;
        return new DateTime(t.Year, 1, 1);
    }

    /// <summary>За замовчуванням «До» у Змісті: поточна дата.</summary>
    public static DateTime DefaultPeriodTo(DateTime? today = null) =>
        (today ?? DateTime.Today).Date;

    /// <summary>Дата документа/рядка в межах Від–До (включно).</summary>
    public static bool InPeriod(DateTime date, DateTime periodFrom, DateTime periodTo)
    {
        var d = date.Date;
        return d >= periodFrom.Date && d <= periodTo.Date;
    }

    /// <summary>
    /// Зміст: лише види, по яких є проведені рухи в періоді Від–До.
    /// Види без рухів на цей період (і порожні користувацькі) — не показуємо.
    /// </summary>
    public static IReadOnlyList<string> BuildToc(
        IEnumerable<NomenclatureItem> nomenclatures,
        IEnumerable<PropertyItem> properties,
        IEnumerable<Movement> movements,
        IEnumerable<Document> documents,
        DateTime periodFrom,
        DateTime periodTo)
    {
        var from = periodFrom.Date;
        var to = periodTo.Date;
        if (from > to) return Array.Empty<string>();

        var nomsById = nomenclatures
            .Where(n => n.Id != Guid.Empty)
            .GroupBy(n => n.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var propToVyd = new Dictionary<Guid, string>();
        foreach (var p in properties)
        {
            if (p.Id == Guid.Empty) continue;
            if (p.NomenclatureId is not Guid nid || nid == Guid.Empty) continue;
            if (!nomsById.TryGetValue(nid, out var nom)) continue;
            var v = (nom.Vyd ?? "").Trim();
            if (v.Length == 0 || NomenclatureVydCatalog.IsNewItem(v)) continue;
            propToVyd[p.Id] = v;
        }

        var docsById = documents
            .GroupBy(d => d.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in movements)
        {
            if (!propToVyd.TryGetValue(m.PropertyId, out var vyd)) continue;
            if (!IsPostedMovement(m, docsById)) continue;
            var moveDate = ResolveMovementDate(m, docsById);
            if (!InPeriod(moveDate, from, to)) continue;
            used.Add(vyd);
        }

        var list = used.ToList();
        list.Sort(Uk);
        return list;
    }

    public static AccountingBookSection BuildSection(
        string vyd,
        IEnumerable<PropertyItem> properties,
        IEnumerable<NomenclatureItem> nomenclatures,
        IEnumerable<Movement> movements,
        IEnumerable<Document> documents,
        string? unitName = null,
        DateTime? periodFrom = null,
        DateTime? periodTo = null)
    {
        var vydNorm = (vyd ?? "").Trim();
        var nomsById = nomenclatures
            .Where(n => n.Id != Guid.Empty)
            .GroupBy(n => n.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var props = properties
            .Where(p => PropertyBelongsToVyd(p, vydNorm, nomsById))
            .ToList();
        var propsById = props
            .Where(p => p.Id != Guid.Empty)
            .GroupBy(p => p.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var columns = BuildColumns(props, nomsById);
        var propToColumn = new Dictionary<Guid, AccountingBookColumnKey>();
        foreach (var col in columns)
        {
            foreach (var id in col.PropertyIds)
                propToColumn[id] = col.Key;
        }

        var allDocsById = documents
            .GroupBy(d => d.Id)
            .ToDictionary(g => g.Key, g => g.First());

        // Лише проведені рухи по позиціях цього Виду (чернетки/резерв не дають Movement).
        var relevant = movements
            .Where(m => propToColumn.ContainsKey(m.PropertyId))
            .Where(m => IsPostedMovement(m, allDocsById))
            .ToList();

        // У періоді Від–До — лише найменування з «надійшло» в цих датах.
        if (periodFrom is DateTime colFrom && periodTo is DateTime colTo)
        {
            columns = FilterColumnsWithInboundInPeriod(
                columns, relevant, propToColumn, allDocsById, colFrom, colTo);
            propToColumn = new Dictionary<Guid, AccountingBookColumnKey>();
            foreach (var col in columns)
            {
                foreach (var id in col.PropertyIds)
                    propToColumn[id] = col.Key;
            }
            relevant = relevant
                .Where(m => propToColumn.ContainsKey(m.PropertyId))
                .ToList();
        }

        // Колонки: раніше перше «надійшло» → лівіше (не алфавіт / Id).
        columns = OrderColumnsByFirstInbound(columns, relevant, propToColumn);

        var balance = columns.ToDictionary(c => c.Key, _ => 0m);
        var rows = new List<AccountingBookRow>();

        foreach (var group in relevant
                     .GroupBy(m => m.DocumentId)
                     .Select(g => new
                     {
                         DocumentId = g.Key,
                         Items = g.ToList(),
                         SortDate = g.Min(x => x.DocumentDate.Date),
                         SortAt = g.Min(x => x.At)
                     })
                     .OrderBy(g => g.SortDate)
                     .ThenBy(g => g.SortAt)
                     .ThenBy(g => g.DocumentId))
        {
            allDocsById.TryGetValue(group.DocumentId, out var doc);
            var cells = new Dictionary<AccountingBookColumnKey, AccountingBookCell>();

            var byCol = group.Items
                .GroupBy(m => propToColumn[m.PropertyId])
                .ToList();

            foreach (var colGroup in byCol)
            {
                var key = colGroup.Key;
                decimal inbound = 0;
                decimal outbound = 0;
                foreach (var m in colGroup)
                {
                    if (m.QuantityDelta > 0) inbound += m.QuantityDelta;
                    else if (m.QuantityDelta < 0) outbound += -m.QuantityDelta;
                }

                balance[key] = balance[key] + inbound - outbound;
                cells[key] = new AccountingBookCell
                {
                    InQty = inbound > 0 ? inbound : null,
                    OutQty = outbound > 0 ? outbound : null,
                    Balance = balance[key]
                };
            }

            // Баланс оновлюємо завжди; рядок у перегляд — лише в межах Від–До.
            if (cells.Count == 0) continue;

            foreach (var col in columns)
            {
                if (cells.ContainsKey(col.Key)) continue;
                cells[col.Key] = new AccountingBookCell
                {
                    InQty = null,
                    OutQty = null,
                    Balance = balance[col.Key]
                };
            }

            var recordDate = doc?.Date.Date ?? group.SortDate;
            if (periodFrom is DateTime pf && recordDate < pf.Date) continue;
            if (periodTo is DateTime pt && recordDate > pt.Date) continue;

            var docType = doc?.Type ?? group.Items[0].DocumentType;
            var number = doc != null
                ? DocTypeNames.FormatNumber(doc.Number)
                : DocTypeNames.FormatNumber(group.Items[0].DocumentNumber);

            var supply = ResolveSupplyFromProperties(group.Items, propsById);
            rows.Add(new AccountingBookRow
            {
                RecordDate = recordDate,
                DocumentTitle = DocumentShortTitle(docType),
                NumberAndDate = FormatNumberAndDate(number, recordDate),
                Counterparty = ResolveCounterparty(doc, unitName, supply),
                DocumentId = group.DocumentId,
                Cells = cells
            });
        }

        return new AccountingBookSection
        {
            Vyd = vydNorm,
            Columns = columns,
            Rows = rows
        };
    }

    /// <summary>
    /// Поточний «становить» по колонці: залишок після останнього рядка
    /// (резерв уже в моделі наявності книги — не віднімається).
    /// </summary>
    public static decimal CurrentBalance(AccountingBookSection section, AccountingBookColumnKey key)
    {
        if (section.Rows.Count == 0) return 0m;
        var last = section.Rows[section.Rows.Count - 1];
        return last.Cells.TryGetValue(key, out var cell) ? cell.Balance : 0m;
    }

    /// <summary>
    /// Чи колонка має наявність для «Фільтр по наявності»:
    /// актуальний становить після рядків періоду &gt; 0, і після округлення
    /// до формату кількості на екрані теж не нуль (пилинки 0,0001 → «0,000»).
    /// </summary>
    public static bool HasPositiveStanovyt(AccountingBookSection section, AccountingBookColumnKey key)
    {
        var bal = CurrentBalance(section, key);
        if (bal <= 0m) return false;
        var places = Ua.QtyDecimals();
        if (places < 0) places = 0;
        if (places > 8) places = 8;
        var rounded = decimal.Round(bal, places, MidpointRounding.AwayFromZero);
        return rounded > 0m;
    }

    /// <summary>
    /// Фільтр по наявності: лише колонки з поточним становить &gt; 0
    /// (після рядків періоду; нуль на екрані — ховати).
    /// Рядки проводок без змін; клітинки інших колонок лишаються в словнику.
    /// </summary>
    public static AccountingBookSection ApplyPresenceFilter(
        AccountingBookSection section,
        bool onlyPositiveBalance)
    {
        if (!onlyPositiveBalance || section.Columns.Count == 0)
            return section;

        var kept = section.Columns
            .Where(c => HasPositiveStanovyt(section, c.Key))
            .ToList();
        if (kept.Count == section.Columns.Count)
            return section;

        return new AccountingBookSection
        {
            Vyd = section.Vyd,
            Columns = kept,
            Rows = section.Rows
        };
    }

    /// <summary>Перша непорожня служба забезпечення серед позицій рядка книги.</summary>
    internal static string ResolveSupplyFromProperties(
        IEnumerable<Movement> items,
        IReadOnlyDictionary<Guid, PropertyItem> propsById)
    {
        foreach (var m in items)
        {
            if (!propsById.TryGetValue(m.PropertyId, out var p)) continue;
            var svc = SupplyServiceHelper.Normalize(p.SupplyService);
            if (svc != null) return svc;
        }
        return "";
    }

    /// <summary>Ключ колонки з майна (для тестів і UI).</summary>
    public static AccountingBookColumnKey ColumnKeyOf(PropertyItem p) =>
        AccountingBookColumnKey.From(p);

    /// <summary>
    /// Класифікація дельти: надійшло (&gt;0) / вибуло (&lt;0).
    /// Нульова дельта і резерв без проведення — не рядок.
    /// </summary>
    public static (decimal? InQty, decimal? OutQty) ClassifyDelta(decimal quantityDelta)
    {
        if (quantityDelta > 0) return (quantityDelta, null);
        if (quantityDelta < 0) return (null, -quantityDelta);
        return (null, null);
    }

    /// <summary>
    /// «становить» після застосування дельти до попереднього залишку.
    /// Поточний склад = Quantity + ReservedQuantity; резерв не зменшує становить.
    /// </summary>
    public static decimal BalanceAfter(decimal previousBalance, decimal quantityDelta) =>
        previousBalance + quantityDelta;

    /// <summary>Фізична наявність на складі (вільний + резерв).</summary>
    public static decimal WarehousePresence(PropertyItem p) =>
        p.Quantity + p.ReservedQuantity;

    /// <summary>Тип документа для колонки «Найменування документа» — повна назва з <see cref="DocTypeNames"/>.</summary>
    internal static string DocumentShortTitle(DocType type) => DocTypeNames.Title(type);

    /// <summary>Два рядки: номер, потім dd.MM.yyyy. Без «№» і без «від».</summary>
    internal static string FormatNumberAndDate(string? number, DateTime recordDate)
    {
        var n = DocTypeNames.FormatNumber(number);
        return $"{n}\n{recordDate:dd.MM.yyyy}";
    }

    /// <summary>
    /// Списання на ОП — два рядки в клітинці:
    /// списання
    /// (ОП {назва})
    /// </summary>
    internal static string FormatOpWriteOff(string? strongpointName)
    {
        var name = (strongpointName ?? "").Trim();
        if (name.Length == 0) return "";
        return "списання\n(ОП " + name + ")";
    }

    /// <summary>
    /// Колонка «постачальник / одержувач»:
    /// ОП → два рядки «списання» / «(ОП …)»; бар’єр/ЗЯС → назва;
    /// Накладна (Receipt) → служба з картки майна; інвентаризація → підрозділ профілю.
    /// </summary>
    internal static string ResolveCounterparty(
        Document? doc,
        string? unitName,
        string? supplyServiceFromProperty = null)
    {
        if (doc == null) return "";
        if (!string.IsNullOrWhiteSpace(doc.StrongpointName))
            return FormatOpWriteOff(doc.StrongpointName);
        if (!string.IsNullOrWhiteSpace(doc.BarrierName))
            return doc.BarrierName.Trim();
        if (doc.Type == DocType.Receipt)
        {
            var svc = (supplyServiceFromProperty ?? "").Trim();
            return svc.Length > 0 ? svc : "—";
        }
        if (doc.Type == DocType.Inventory)
        {
            var unit = (unitName ?? "").Trim();
            if (unit.Length > 0) return unit;
        }
        return "";
    }

    private static bool PropertyBelongsToVyd(
        PropertyItem p,
        string vyd,
        IReadOnlyDictionary<Guid, NomenclatureItem> nomsById)
    {
        if (vyd.Length == 0) return false;
        if (p.NomenclatureId is not Guid nid || nid == Guid.Empty) return false;
        if (!nomsById.TryGetValue(nid, out var nom)) return false;
        return string.Equals((nom.Vyd ?? "").Trim(), vyd, StringComparison.Ordinal);
    }

    private static IReadOnlyList<AccountingBookColumn> BuildColumns(
        IReadOnlyList<PropertyItem> props,
        IReadOnlyDictionary<Guid, NomenclatureItem> nomsById)
    {
        // Порядок тут тимчасовий; фінальний — OrderColumnsByFirstInbound.
        var groups = props
            .GroupBy(AccountingBookColumnKey.From)
            .ToList();

        var result = new List<AccountingBookColumn>(groups.Count);
        foreach (var g in groups)
        {
            var unit = ResolveAccountingUnit(g, nomsById);
            result.Add(new AccountingBookColumn
            {
                Key = g.Key,
                Unit = unit,
                PropertyIds = g.Select(p => p.Id).ToList()
            });
        }
        return result;
    }

    /// <summary>
    /// Лише колонки, по яких у Від–До було проведене «надійшло» (QuantityDelta &gt; 0).
    /// </summary>
    internal static IReadOnlyList<AccountingBookColumn> FilterColumnsWithInboundInPeriod(
        IReadOnlyList<AccountingBookColumn> columns,
        IEnumerable<Movement> postedMovements,
        IReadOnlyDictionary<Guid, AccountingBookColumnKey> propToColumn,
        IReadOnlyDictionary<Guid, Document> docsById,
        DateTime periodFrom,
        DateTime periodTo)
    {
        var from = periodFrom.Date;
        var to = periodTo.Date;
        if (from > to || columns.Count == 0)
            return Array.Empty<AccountingBookColumn>();

        var withIn = new HashSet<AccountingBookColumnKey>();
        foreach (var m in postedMovements)
        {
            if (m.QuantityDelta <= 0) continue;
            if (!propToColumn.TryGetValue(m.PropertyId, out var key)) continue;
            if (!InPeriod(ResolveMovementDate(m, docsById), from, to)) continue;
            withIn.Add(key);
        }

        if (withIn.Count == 0)
            return Array.Empty<AccountingBookColumn>();

        return columns.Where(c => withIn.Contains(c.Key)).ToList();
    }

    /// <summary>
    /// Колонки зліва направо за датою першого проведеного «надійшло» (QuantityDelta &gt; 0).
    /// Без приходу — в кінці; нічия — Name, Category, UnitPrice.
    /// </summary>
    internal static IReadOnlyList<AccountingBookColumn> OrderColumnsByFirstInbound(
        IReadOnlyList<AccountingBookColumn> columns,
        IEnumerable<Movement> postedMovements,
        IReadOnlyDictionary<Guid, AccountingBookColumnKey> propToColumn)
    {
        var firstIn = new Dictionary<AccountingBookColumnKey, (DateTime Date, DateTime At, Guid DocId)>();
        foreach (var m in postedMovements)
        {
            if (m.QuantityDelta <= 0) continue;
            if (!propToColumn.TryGetValue(m.PropertyId, out var key)) continue;
            var stamp = (m.DocumentDate.Date, m.At, m.DocumentId);
            if (!firstIn.TryGetValue(key, out var prev)
                || stamp.Item1 < prev.Date
                || (stamp.Item1 == prev.Date && stamp.Item2 < prev.At)
                || (stamp.Item1 == prev.Date && stamp.Item2 == prev.At && stamp.Item3.CompareTo(prev.DocId) < 0))
            {
                firstIn[key] = stamp;
            }
        }

        return columns
            .OrderBy(c => firstIn.TryGetValue(c.Key, out var s) ? s.Date : DateTime.MaxValue)
            .ThenBy(c => firstIn.TryGetValue(c.Key, out var s) ? s.At : DateTime.MaxValue)
            .ThenBy(c => firstIn.TryGetValue(c.Key, out var s) ? s.DocId : Guid.Empty)
            .ThenBy(c => c.Key.Name, Uk)
            .ThenBy(c => c.Key.Category, Uk)
            .ThenBy(c => c.Key.UnitPrice)
            .ToList();
    }

    /// <summary>Облікова О.В. (не DisplayUnit нотатки).</summary>
    private static string ResolveAccountingUnit(
        IEnumerable<PropertyItem> group,
        IReadOnlyDictionary<Guid, NomenclatureItem> nomsById)
    {
        foreach (var p in group)
        {
            var u = (p.Unit ?? "").Trim();
            if (u.Length > 0) return u;
            if (p.NomenclatureId is Guid nid
                && nomsById.TryGetValue(nid, out var nom))
            {
                var nu = (nom.Unit ?? "").Trim();
                if (nu.Length > 0) return nu;
            }
        }
        return "шт";
    }

    private static bool IsPostedMovement(
        Movement m,
        IReadOnlyDictionary<Guid, Document> docsById)
    {
        if (docsById.TryGetValue(m.DocumentId, out var doc))
            return doc.Status == DocStatus.Posted;
        // Рух без документа в списку (історичний імпорт) — вважаємо проведеним.
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
}
