namespace Obozny;

public enum DocType
{
    Inventory,
    Receipt,
    Issue,
    WriteOff,
    QualityChange,
    QualityState,
    /// <summary>Акт виконаних робіт (списання на роботи / ремонт).</summary>
    WorkAct,
    /// <summary>Акт списання вибухових матеріалів (Додаток 16, підривні роботи).</summary>
    ExplosiveWorks,
    /// <summary>Повернення на склад служби: майно списується з нашого складу.</summary>
    Return,
    /// <summary>Втрати: те саме списання, інша назва документа.</summary>
    Loss,
    /// <summary>Переміщення на інший підрозділ: майно знімається зі складу.</summary>
    Transfer
}

public enum DocStatus
{
    Draft,
    Posted
}

public enum ViewMode
{
    Stock,
    Movements,
    Strongpoints,
    /// <summary>Центральна таблиця: лише майно з типом номенклатури «Основні засоби».</summary>
    FixedAssets
}

/// <summary>Дані для стартового вікна «Зведена інформація».</summary>
public sealed record StartupSummary(int ReservedPositions, decimal ReservedValue, int DraftCount);

public sealed class StrongpointRow
{
    public string Name { get; set; } = "";
    public string RegNumber { get; set; } = "";
    public DateTime? CommissioningDate { get; set; }
    public decimal TotalValue { get; set; }
    /// <summary>Немає проведеного ЯС — у назві показуємо курсивний суфікс *ЧЕРНЕТКА*.</summary>
    public bool IsDraft { get; set; }
}

/// <summary>Рядок блоку «Документи опорного пункту» (ЯС або Акт ВР).</summary>
public sealed class StrongpointDocLinkRow
{
    public string Kind { get; set; } = "";
    public string Number { get; set; } = "";
    public string DateText { get; set; } = "";
    /// <summary>Id акта ЯС (для ЯС і для пов’язаного Акта ВР).</summary>
    public Guid? DocumentId { get; set; }
    public bool IsVrAct { get; set; }
}

/// <summary>Зведена позиція для Акта ВР (без вартості / категорії).</summary>
public sealed class VrActLineRow
{
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public string QuantityText { get; set; } = "";
    public decimal Quantity { get; set; }
}

public sealed class StrongpointLineRow
{
    public string StructureName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public string QuantityText { get; set; } = "";
    public string PriceText { get; set; } = "";
    public string ValueText { get; set; } = "";
}

public sealed class StrongpointActRow
{
    public Guid DocumentId { get; set; }
    public string DateText { get; set; } = "";
    public string Number { get; set; } = "";
    public string Note { get; set; } = "";
}

public sealed class StrongpointDetail
{
    public List<StrongpointLineRow> Lines { get; set; } = new();
    public List<StrongpointActRow> Acts { get; set; } = new();
}

public static class DocTypeNames
{
    public const string Numberless = "б/н";

    public static string Title(DocType type) => type switch
    {
        DocType.Inventory => "Інвентаризація",
        DocType.Receipt => "Накладна (вимога)",
        DocType.Issue => "Видача майна",
        DocType.Transfer => "Накладна",
        DocType.WriteOff => "Акт списання",
        DocType.QualityChange => "Акт зміни якісного стану",
        DocType.QualityState => "Акт якісного стану",
        DocType.WorkAct => "Акт виконаних робіт",
        DocType.ExplosiveWorks => "Акт списання вибухових матеріалів",
        DocType.Return => "Накладна",
        DocType.Loss => "Документи втрати майна",
        _ => type.ToString()
    };

    public static string FormatNumber(string? number) =>
        string.IsNullOrWhiteSpace(number) ? Numberless : number.Trim();

    public static string NormalizeNumber(string? number) =>
        string.IsNullOrWhiteSpace(number) ? Numberless : number.Trim();

    /// <summary>Списання зі складу: акт списання, ЯС, зміна ЯС, АВР, документи втрати.</summary>
    public static bool IsWriteOff(DocType type) =>
        type is DocType.WriteOff or DocType.Loss or DocType.QualityChange or DocType.QualityState
            or DocType.WorkAct or DocType.ExplosiveWorks;

    /// <summary>Зменшує залишок на складі.</summary>
    public static bool DecreasesStock(DocType type) =>
        IsWriteOff(type) || type is DocType.Issue or DocType.Return or DocType.Transfer;
}

/// <summary>Варіанти під кнопкою «Підготувати документи на списання майна».</summary>
public sealed record WriteOffChoice(string WorkTypeName, DocType DocType);

public static class WriteOffChoices
{
    public static readonly WriteOffChoice[] All =
    {
        new("Фортифікаційне обладнання (опорний пункт)", DocType.QualityState),
        new("Дообладнання фортифікацій (опорних пунктів)", DocType.QualityState),
        new("Обладнання невибухового інженерного загородження (у т.ч. АДЗ)", DocType.QualityChange),
        new("Ремонт невибухового інженерного загородження (у т.ч. АДЗ)", DocType.WorkAct),
        new("Списати майно на створення чи ремонт не типової споруди чи виду робіт", DocType.WorkAct),
        new("Вибухові роботи", DocType.ExplosiveWorks),
        new("Документи втрати майна", DocType.Loss)
    };
}

/// <summary>Тип позиції в довіднику номенклатури (не категорія якості І/ІІ/ІІІ).</summary>
public enum NomenclatureKind
{
    /// <summary>Запаси (матеріали, витратні тощо).</summary>
    Stocks = 0,
    /// <summary>Основні засоби.</summary>
    FixedAssets = 1
}

public static class NomenclatureKindNames
{
    public const string Stocks = "Запаси";
    public const string FixedAssets = "Основні засоби";

    public static readonly string[] All = { Stocks, FixedAssets };

    public static string Of(NomenclatureKind kind) => kind switch
    {
        NomenclatureKind.FixedAssets => FixedAssets,
        _ => Stocks
    };

    public static NomenclatureKind Parse(string? label)
    {
        var t = (label ?? "").Trim().ToLowerInvariant();
        if (t.Contains("основн") || t is "оз" or "о.з." or "fixedassets")
            return NomenclatureKind.FixedAssets;
        return NomenclatureKind.Stocks;
    }
}

/// <summary>Довідник видів номенклатури (алфавіт) і типовий Тип майна за Видом.</summary>
public static class NomenclatureVydCatalog
{
    /// <summary>Пункт combo для створення користувацького виду (не зберігається як Вид).</summary>
    public const string NewVydItem = "+ Новий вид майна";

    /// <summary>Фіксований перелік видів за українським алфавітом (код не видаляє і не перейменовує).</summary>
    public static readonly string[] All =
    {
        "Будівельні матеріали",
        "Дріт металевий",
        "Електроінструмент",
        "Загородження інженерні (Єгоза)",
        "Загородження інженерні (Колючий дріт)",
        "Загородження інженерні (Плутанка)",
        "Залізобетонні вироби",
        "Інженерна техніка (ОВТ)",
        "Інше",
        "Контейнери морські",
        "Лісоматеріали",
        "Макети",
        "Маскувальні сітки",
        "Металопрокат",
        "Мішки будівельні",
        "Пиломатеріали",
        "Плівка (гідроізоляція)",
        "Саперне майно",
        "Саперний провід",
        "Сітка для АДЗ/ІКАЗ",
        "Сітки рибальські (для АДЗ/ІКАЗ)",
        "Скоби будівельні",
        "Фортифікаційні споруди",
        "Цвяхи будівельні",
        "Шанцевий інструмент",
    };

    private static readonly StringComparer Uk = StringComparer.Create(
        new System.Globalization.CultureInfo("uk-UA"), ignoreCase: false);

    private static readonly HashSet<string> FixedAssetVyds = new(StringComparer.Ordinal)
    {
        "Шанцевий інструмент",
        "Електроінструмент",
        "Фортифікаційні споруди",
        "Макети",
        "Інженерна техніка (ОВТ)",
    };

    public static bool IsNewItem(string? value) =>
        string.Equals((value ?? "").Trim(), NewVydItem, StringComparison.Ordinal);

    public static bool IsFixed(string? vyd) =>
        All.Contains((vyd ?? "").Trim(), StringComparer.Ordinal);

    /// <summary>Фіксований каталог + користувацькі види (алфавіт uk-UA), без пункту «+ Новий…».</summary>
    public static IReadOnlyList<string> Combined(IEnumerable<string>? customVyds)
    {
        var list = new List<string>(All);
        if (customVyds != null)
        {
            foreach (var raw in customVyds)
            {
                var t = (raw ?? "").Trim();
                if (t.Length == 0 || IsNewItem(t)) continue;
                if (list.Contains(t, StringComparer.Ordinal)) continue;
                list.Add(t);
            }
        }
        list.Sort(Uk);
        return list;
    }

    /// <summary>Пункти combo: види (combined) + в кінці «+ Новий вид майна».</summary>
    public static IReadOnlyList<string> ComboChoices(IEnumerable<string>? customVyds)
    {
        var combined = Combined(customVyds);
        var result = new List<string>(combined.Count + 1);
        result.AddRange(combined);
        result.Add(NewVydItem);
        return result;
    }

    /// <summary>Нормалізувати назву нового виду; null — відхилити.</summary>
    public static string? NormalizeNewVydName(string? raw, IEnumerable<string>? customVyds)
    {
        var t = (raw ?? "").Trim();
        if (t.Length == 0 || IsNewItem(t)) return null;
        if (Combined(customVyds).Contains(t, StringComparer.Ordinal)) return null;
        return t;
    }

    /// <summary>Тип майна за замовчуванням при виборі Виду (користувач може змінити вручну).</summary>
    public static NomenclatureKind DefaultKind(string? vyd)
    {
        var v = (vyd ?? "").Trim();
        if (v.Length == 0 || IsNewItem(v)) return NomenclatureKind.Stocks;
        return FixedAssetVyds.Contains(v) ? NomenclatureKind.FixedAssets : NomenclatureKind.Stocks;
    }
}

/// <summary>Паспорт номенклатури: назва, вид, одиниця, тип майна.</summary>
public sealed class NomenclatureItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    /// <summary>Вид з довідника (див. <see cref="NomenclatureVydCatalog"/>).</summary>
    public string Vyd { get; set; } = "";
    /// <summary>Офіційна О.В. (з документів / картки). Програма сама не змінює при налаштуванні відображення.</summary>
    public string Unit { get; set; } = "шт";
    /// <summary>О.В. відображення (з тих, що задані для Виду). Порожньо — не задано.</summary>
    public string DisplayUnit { get; set; } = "";
    /// <summary>
    /// Коефіцієнт: скільки одиниць відображення в 1 офіційній О.В. цієї картки.
    /// null — порожнє (позицію не брати в перерахунок відображення).
    /// </summary>
    public decimal? DisplayFactor { get; set; }
    public NomenclatureKind Kind { get; set; } = NomenclatureKind.Stocks;
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>О.В. відображення, задані користувачем для конкретного Виду.</summary>
public sealed class VydDisplayUnitEntry
{
    public string Vyd { get; set; } = "";
    public List<string> Units { get; set; } = new();
}

/// <summary>Обрана О.В. відображення для Виду в нотатці «Зведені залишки».</summary>
public sealed class VydSummaryDisplayChoice
{
    public string Vyd { get; set; } = "";
    public string DisplayUnit { get; set; } = "";
}

/// <summary>
/// Застаріла мапа Вид→служба (більше не використовується в UI/книзі).
/// Лишається в моделі лише для тихої десеріалізації старих obozny.json.
/// </summary>
public sealed class VydSupplyServiceEntry
{
    public string Vyd { get; set; } = "";
    public string Service { get; set; } = "";
}

public sealed class PropertyItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "шт";
    public string Category { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public bool FullyWrittenOff { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>Посилання на паспорт у довіднику номенклатури.</summary>
    public Guid? NomenclatureId { get; set; }
    /// <summary>
    /// Служба забезпечення позиції: «ІС ССП» / «СЗУ» / «СОВТ та МСВ» (або порожньо).
    /// Persist у obozny.json.
    /// </summary>
    public string SupplyService { get; set; } = "";
}

public sealed class DocumentLine : System.ComponentModel.INotifyPropertyChanged
{
    private decimal _quantity;

    public Guid PropertyId { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Quantity
    {
        get => _quantity;
        set
        {
            if (_quantity == value) return;
            _quantity = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Quantity)));
        }
    }
    public bool Anachronistic { get; set; }
    /// <summary>Лише для акта якісного стану — фортифікаційна споруда в межах документа.</summary>
    public Guid? StructureId { get; set; }
    /// <summary>Ціна за одиницю на момент документа (0 — брати з картки майна).</summary>
    public decimal UnitPrice { get; set; }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public sealed class FortStructure
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public int Order { get; set; }
}

public sealed class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DocType Type { get; set; }
    public string Number { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Today;
    public DocStatus Status { get; set; } = DocStatus.Draft;
    public string Note { get; set; } = "";
    /// <summary>Назва виду робіт з меню підготовки списання.</summary>
    public string WorkTypeName { get; set; } = "";
    public List<DocumentLine> Lines { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? PostedAt { get; set; }

    // Акт якісного стану
    public string StrongpointName { get; set; } = "";
    public DateTime? CommissioningDate { get; set; }
    public string StrongpointRegNumber { get; set; } = "";
    /// <summary>Номер парного Акта ВР (окремо від № ЯС / ЗЯС). Порожньо → у ВР показуємо «н/д», не вигадуємо.</summary>
    public string VrActNumber { get; set; } = "";
    public List<FortStructure> Structures { get; set; } = new();
    public Guid? ActiveStructureId { get; set; }

    // Акт зміни якісного стану
    public string FormSeries { get; set; } = "";
    public string FormNumber { get; set; } = "";
    public string BarrierName { get; set; } = "";
}

public sealed class Movement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime At { get; set; } = DateTime.Now;
    public DateTime DocumentDate { get; set; }
    public Guid DocumentId { get; set; }
    public DocType DocumentType { get; set; }
    public string DocumentNumber { get; set; } = "";
    public Guid PropertyId { get; set; }
    public string PropertyName { get; set; } = "";
    public decimal QuantityDelta { get; set; }
    public string Kind { get; set; } = "";
}

public sealed class ColumnSetting
{
    public string Key { get; set; } = "";
    public bool Visible { get; set; } = true;
    public double Width { get; set; } = 160;
    public int Order { get; set; }
    public bool WidthCustom { get; set; }
    public int? DecimalPlaces { get; set; }
}

public sealed class UserSettings
{
    public string UnitName { get; set; } = "Підрозділ";
    public string UserName { get; set; } = "Сержант з МТЗ";
    public string? ProfileImagePath { get; set; }
    /// <summary>Ідентифікатор теми (lon, tuman, pshenytsia, grafit, smola, varta). Старі/видалені id підставляються в ThemeService.Resolve.</summary>
    public string Theme { get; set; } = "lon";
    public List<ColumnSetting> Columns { get; set; } = new();
    public int ColumnLayoutVersion { get; set; }
    /// <summary>Користувач видалив стікер «Керівні документи» — більше не створювати.</summary>
    public bool GuidesStickerRemoved { get; set; }
    /// <summary>Стікер керівних документів уже підігнано під текст.</summary>
    public bool GuidesFitted { get; set; }
    /// <summary>Користувач прийняв умови бета-версії.</summary>
    public bool BetaTermsAccepted { get; set; }
    /// <summary>Дата прийняття умов (старт відліку 3 місяці). null — ще не приймав.</summary>
    public DateTime? BetaTermsAcceptedAt { get; set; }
    /// <summary>HMAC-підпис прийняття (machine-id + дата + секрет збірки). Без валідного підпису строк вважається зіпсованим.</summary>
    public string? BetaSeal { get; set; }
}

public sealed class StickerNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public double Left { get; set; } = 160;
    public double Top { get; set; } = 160;
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 300;
    public string ColorKey { get; set; } = "mustard";
    public double FontSize { get; set; } = 16;
    public string Rtf { get; set; } = "";
    /// <summary>«guides» — стікер керівних документів. Порожнє для звичайної нотатки.</summary>
    public string Role { get; set; } = "";
}

/// <summary>Позиція постійного стікера «Зведені залишки» (підсумок по Виду номенклатури).</summary>
public sealed class StockSummaryStickerState
{
    public double Left { get; set; } = 48;
    public double Top { get; set; } = 120;
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 280;
    /// <summary>
    /// Які види показувати в нотатці. <c>null</c> — ще не налаштовували → усі з каталогу.
    /// Порожній список — автор зняв усі галочки.
    /// </summary>
    public List<string>? EnabledVyds { get; set; }
    /// <summary>
    /// Обрана О.В. відображення по Виду для підсумку в нотатці.
    /// Якщо для виду немає запису / порожньо — підсумок у офіційних О.В. (як раніше).
    /// </summary>
    public List<VydSummaryDisplayChoice>? SummaryDisplayUnits { get; set; }
}

/// <summary>
/// Вивчений ключ з назви номенклатури → Вид (напр. «дошк» → «Пиломатеріали»).
/// Один ключ — один Вид; конфлікт при навчанні не перезаписує старе.
/// </summary>
public sealed class VydKeywordEntry
{
    public string Keyword { get; set; } = "";
    public string Vyd { get; set; } = "";
}

public sealed class AppData
{
    public List<PropertyItem> Properties { get; set; } = new();
    /// <summary>Довідник номенклатури (паспорти: назва, о.в., запаси/ОЗ).</summary>
    public List<NomenclatureItem> Nomenclatures { get; set; } = new();
    public List<Document> Documents { get; set; } = new();
    public List<Movement> Movements { get; set; } = new();
    public UserSettings Settings { get; set; } = new();
    public List<StickerNote> Stickers { get; set; } = new();
    public StockSummaryStickerState StockSummarySticker { get; set; } = new();
    /// <summary>Словник ключів Виду, навчених з призначень автора (persist у obozny.json).</summary>
    public List<VydKeywordEntry> VydKeywords { get; set; } = new();
    /// <summary>Користувацькі види майна (поруч із фіксованим каталогом). Persist у obozny.json.</summary>
    public List<string> CustomVyds { get; set; } = new();
    /// <summary>О.В. відображення по Виду (для зведених залишків). Persist у obozny.json.</summary>
    public List<VydDisplayUnitEntry> VydDisplayUnits { get; set; } = new();
    /// <summary>
    /// Застаріла мапа Вид→служба (не використовується). Persist лишається для сумісності JSON.
    /// </summary>
    public List<VydSupplyServiceEntry> VydSupplyServices { get; set; } = new();
    public List<string> WorkDays { get; set; } = new();
    public string? LastActiveDate { get; set; }
    public bool HadFirstStockBackup { get; set; }
}

public static class StickerColors
{
    /// <summary>Body / Accent (темніша смужка) / колір тексту — як набір стікерів-закладок.</summary>
    public sealed record Swatch(string Key, string Body, string Accent, string Ink, string Label);

    public static readonly Swatch[] All =
    {
        new("dustyRose", "#E8B8B4", "#C08D88", "#2C241F", "Троянда"),
        new("sage", "#D4CBA0", "#AFA484", "#2C241F", "Шавлія"),
        new("teal", "#A8C9C2", "#799A92", "#2C241F", "Бірюза"),
        new("mauve", "#D4A0B4", "#AC7587", "#2C241F", "Орхідея"),
        new("jade", "#8FCBB0", "#51947A", "#2C241F", "Нефрит"),
        new("nude", "#D4B8B0", "#B2938B", "#2C241F", "Нюд"),
        new("mustard", "#E8C888", "#C5904D", "#2C241F", "Гірчиця"),
        new("plum", "#A898B0", "#5D4E62", "#2C241F", "Слива"),
        new("watermelon", "#E888A0", "#AD4C68", "#2C241F", "Кавун"),
        new("periwinkle", "#A8AEC8", "#767B91", "#2C241F", "Бузок")
    };

    public static Swatch Get(string? key) =>
        All.FirstOrDefault(x => x.Key == key) ?? All[0];
}

public sealed class PropertyRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal Quantity { get; set; }
    public DateTime? Date { get; set; }
    public string MoveKind { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public Guid? DocumentId { get; set; }
    public bool IsReserved { get; set; }
    /// <summary>Уся кількість у резерві (немає вільного залишку).</summary>
    public bool IsFullyReserved { get; set; }
    public bool IsWrittenOff { get; set; }
    /// <summary>Тип руху невизначений — потрібен ручний вибір.</summary>
    public bool NeedsKindPick { get; set; }
}

/// <summary>
/// Робочі підписи типу руху (не назва документа).
/// У реєстрі «Рух майна»: списання, отримання, переміщення, повернення.
/// Інвентаризація — окремо, не змішувати з цим реєстром.
/// </summary>
public static class MoveKindNames
{
    public const string WriteOff = "Списання";
    public const string Receipt = "Отримання";
    public const string Transfer = "Переміщення";
    public const string Return = "Повернення";
    public const string Inventory = "Інвентаризація";
    public const string Issue = "Видача";
    public const string Unknown = "Невизначено";

    /// <summary>Робочі типи для реєстру «Рух майна» (без інвентаризації).</summary>
    public static readonly string[] WorkingKinds =
    {
        WriteOff, Receipt, Transfer, Return
    };

    /// <summary>Усі типи, які можна обрати вручну (включно з інвентаризацією).</summary>
    public static readonly string[] PickableKinds =
    {
        WriteOff, Receipt, Transfer, Return, Inventory, Issue
    };

    public static string Of(DocType type) => type switch
    {
        DocType.Inventory => Inventory,
        DocType.Receipt => Receipt,
        DocType.Issue => Issue,
        DocType.Return => Return,
        DocType.Transfer => Transfer,
        _ when DocTypeNames.IsWriteOff(type) => WriteOff,
        _ => Unknown
    };

    public static string Of(Movement m)
    {
        // Внутрішній документ «Видача майна» зменшує склад; не плутати з видом операції накладної.
        if (m.DocumentType == DocType.Issue)
            return Issue;
        var mapped = FromLabel(m.Kind);
        if (mapped != null) return mapped;
        return Of(m.DocumentType);
    }

    /// <summary>
    /// Підпис для колонки «Тип руху»: лише робочі типи.
    /// Назви документів («Акт ЯС», «Накладна…») сюди не потрапляють.
    /// </summary>
    public static string ForUi(ImportMoveKind kind, DocType documentType)
    {
        // Документи списання (ЯС / ЗЯС / АВР / акт списання / втрати) — завжди «Списання».
        if (DocTypeNames.IsWriteOff(documentType))
            return WriteOff;
        if (kind == ImportMoveKind.Unknown)
            return Unknown;
        return Of(ImportGrouping.TypeOf(kind));
    }

    /// <summary>Прибрати з підпису назву документа; повернути робочий тип руху.</summary>
    public static string Sanitize(string? label, DocType? documentType = null)
    {
        var mapped = FromLabel(label);
        if (mapped != null) return mapped;
        if (documentType != null) return Of(documentType.Value);
        return Unknown;
    }

    /// <summary>null — якщо рядок порожній / не розпізнано як тип руху чи назву документа.</summary>
    private static string? FromLabel(string? label)
    {
        var kind = (label ?? "").Trim().ToLowerInvariant();
        if (kind.Length == 0) return null;
        if (kind.Contains("невизнач") || kind == "?" || kind == "—")
            return Unknown;
        // Заборонені як «тип руху» назви документів → робочий тип.
        if (kind.Contains("акт яс") || kind.Contains("якісн") || kind.Contains("зміна яс")
            || kind.Contains("зяс") || kind.Contains("виконаних робіт") || kind.Contains("акт робіт")
            || kind.Contains("акт списання") || kind.Contains("документи втрати") || kind.Contains("втрат")
            || kind.Contains("вибухов") || kind.Contains("підривн") || kind.Contains("подривн"))
            return WriteOff;
        if (kind.Contains("накладн") || kind.Contains("вимог"))
        {
            // Гола «накладна» без виду операції — не вгадувати.
            if (kind.Contains("отрим") || kind.Contains("надход")) return Receipt;
            if (kind.Contains("повер") || kind.Contains("здаван")) return Return;
            if (kind.Contains("переміщ") || kind.Contains("перемищ")) return Transfer;
            if (kind.Contains("спис")) return WriteOff;
            return Unknown;
        }
        if (kind.Contains("наказ"))
            return Unknown;
        if (kind.Contains("здаван") || kind.Contains("повер"))
            return Return;
        if (kind.Contains("прийман") || kind.Contains("отрим") || kind.Contains("надход"))
            return Receipt;
        if (kind.Contains("інвентар") || kind.Contains("оприбут") || kind.Contains("залиш")
            || kind.Contains("внесення"))
            return Inventory;
        if (kind.Contains("переміщ") || kind.Contains("перемищ"))
            return Transfer;
        // Лише повна назва документа «Видача майна» — Issue (−).
        // Вид операції / короткий текст «видача» — Receipt (+) для підрозділу.
        if (kind.Contains("видача майна"))
            return Issue;
        if (kind.Contains("видат") || kind.Contains("видача") || kind.Contains("видач"))
            return Receipt;
        if (kind.Contains("спис"))
            return WriteOff;
        // Точний збіг з робочими підписами.
        if (string.Equals(kind, WriteOff, StringComparison.OrdinalIgnoreCase)) return WriteOff;
        if (string.Equals(kind, Receipt, StringComparison.OrdinalIgnoreCase)) return Receipt;
        if (string.Equals(kind, Transfer, StringComparison.OrdinalIgnoreCase)) return Transfer;
        if (string.Equals(kind, Return, StringComparison.OrdinalIgnoreCase)) return Return;
        if (string.Equals(kind, Inventory, StringComparison.OrdinalIgnoreCase)) return Inventory;
        return null;
    }

    public static bool IsInventory(DocType type) => type == DocType.Inventory;

    public static bool IsInventoryLabel(string? label) =>
        string.Equals((label ?? "").Trim(), Inventory, StringComparison.OrdinalIgnoreCase);

    public static bool IsUnknown(string? label) =>
        string.Equals((label ?? "").Trim(), Unknown, StringComparison.OrdinalIgnoreCase)
        || string.IsNullOrWhiteSpace(label);

    public static ImportMoveKind ToImportKind(string label)
    {
        var sanitized = Sanitize(label);
        if (sanitized == WriteOff) return ImportMoveKind.WriteOff;
        if (sanitized == Receipt) return ImportMoveKind.Receipt;
        if (sanitized == Transfer) return ImportMoveKind.Transfer;
        if (sanitized == Return) return ImportMoveKind.Return;
        if (sanitized == Inventory) return ImportMoveKind.Inventory;
        if (sanitized == Issue) return ImportMoveKind.Issue;
        return ImportMoveKind.Unknown;
    }
}

public static class ColumnCatalog
{
    public static readonly ColumnDef[] All =
    {
        new("Name", new[] { "Найменування майна", "Наймен-ня майна", "Найм-ня майна", "Найменування", "Найм-ня" }, 280),
        new("Category", new[] { "Категорія", "Катег-ія", "Кат-ія" }, 140),
        new("Quantity", new[] { "Кількість", "Кіл-ть" }, 120),
        new("Unit", new[] { "Одиниці виміру", "Один-ці виміру", "Один. виміру", "Од-ці в-ру", "О.В." }, 140),
        new("UnitPrice", new[] { "Ціна за одиницю", "Ціна за один-ю", "Ціна" }, 140)
    };
}

public readonly record struct ColumnDef(string Key, string[] Aliases, double DefaultWidth);
