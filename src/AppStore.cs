using System.Text.Json;

namespace Obozny;

internal sealed class AppStore
{
    public static AppStore Instance { get; } = new();

    public AppData Data { get; private set; } = new();

    public event Action? Changed;

    private static readonly JsonSerializerOptions JsonOpt = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public void Load()
    {
        BackupService.EnsureLayout();
        if (File.Exists(Paths.DataFile))
        {
            var json = File.ReadAllText(Paths.DataFile);
            Data = JsonSerializer.Deserialize<AppData>(json, JsonOpt) ?? new AppData();
        }

        Data.StockSummarySticker ??= new StockSummaryStickerState();
        Data.Nomenclatures ??= new List<NomenclatureItem>();
        Data.VydKeywords ??= new List<VydKeywordEntry>();
        Data.CustomVyds ??= new List<string>();
        Data.VydDisplayUnits ??= new List<VydDisplayUnitEntry>();
        Data.VydSupplyServices ??= new List<VydSupplyServiceEntry>();
        EnsureNomenclatures();
        EnsureDefaultColumns();
        if (Data.Settings.ColumnLayoutVersion < 2)
        {
            foreach (var col in Data.Settings.Columns)
                col.WidthCustom = false;
            Data.Settings.ColumnLayoutVersion = 2;
        }
        // v3: пропорційні (* ) ширини від вікна — скидаємо старі піксельні «залиплі» ширини
        if (Data.Settings.ColumnLayoutVersion < 3)
        {
            foreach (var col in Data.Settings.Columns)
            {
                col.WidthCustom = false;
                var def = ColumnCatalog.All.FirstOrDefault(c => c.Key == col.Key);
                if (!string.IsNullOrEmpty(def.Key)) col.Width = def.DefaultWidth;
            }
            Data.Settings.ColumnLayoutVersion = 3;
        }
        // v4: порядок Кількість → О.В. → Ціна
        if (Data.Settings.ColumnLayoutVersion < 4)
        {
            var order = ColumnCatalog.All.Select((c, i) => (c.Key, i)).ToDictionary(x => x.Key, x => x.i);
            foreach (var col in Data.Settings.Columns)
            {
                if (order.TryGetValue(col.Key, out var i))
                    col.Order = i;
            }
            Data.Settings.ColumnLayoutVersion = 4;
        }
        // v5: ширину після ручного розтягування зберегли в пікселях,
        // а таблиця читає її як частку. Через це решта стовпців зникала.
        if (Data.Settings.ColumnLayoutVersion < 5)
        {
            foreach (var col in Data.Settings.Columns)
                col.WidthCustom = false;
            Data.Settings.ColumnLayoutVersion = 5;
        }
        RepairQualityWriteOffMovements();
        RepairQualityStateCompositeNumbers();
        MergeSplitStrongpointActs();
        CapsStrongpointNames();
        RecalcReservations();
        if (BackupService.NeedsEndOfDayStock(Data) && Data.Properties.Count > 0)
            BackupService.Stock(Data, "кінець_дня_після_сну");
        BackupService.RememberWorkDay(Data);
        Save();
    }

    /// <summary>
    /// Раніше акти якісного стану помилково проводились як надходження (плюс).
    /// Виправляємо знак руху і залишок на складі.
    /// </summary>
    private void RepairQualityWriteOffMovements()
    {
        foreach (var m in Data.Movements)
        {
            var isQuality = m.DocumentType is DocType.QualityChange or DocType.QualityState
                || (m.Kind ?? "").Contains("якіс", StringComparison.OrdinalIgnoreCase);
            if (!isQuality || m.QuantityDelta <= 0) continue;

            var wrong = m.QuantityDelta;
            m.QuantityDelta = -wrong;
            var item = Data.Properties.FirstOrDefault(p => p.Id == m.PropertyId);
            if (item == null) continue;
            item.Quantity -= 2 * wrong;
            if (item.Quantity < 0) item.Quantity = 0;
            if (item.Quantity == 0 && item.ReservedQuantity == 0)
                item.FullyWrittenOff = true;
        }
    }

    /// <summary>
    /// Номери виду 1125 "БАТОН" №343/3/6/15281 → акт ЯС: номер / назва ОП / реєстр. номер.
    /// </summary>
    private void RepairQualityStateCompositeNumbers()
    {
        foreach (var doc in Data.Documents)
        {
            if (!QualityStateNumber.LooksLike(doc.Number)) continue;
            var oldNumber = doc.Number;
            QualityStateNumber.ApplyTo(doc);
            foreach (var m in Data.Movements.Where(x => x.DocumentId == doc.Id || x.DocumentNumber == oldNumber))
            {
                m.DocumentId = doc.Id;
                m.DocumentNumber = doc.Number;
                m.DocumentType = DocType.QualityState;
                m.Kind = MoveKindNames.Of(DocType.QualityState);
            }
        }
    }

    public void Save(bool notify = true)
    {
        var json = JsonSerializer.Serialize(Data, JsonOpt);
        var tmp = Paths.DataFile + ".tmp";
        File.WriteAllText(tmp, json);
        File.Copy(tmp, Paths.DataFile, true);
        File.Delete(tmp);
        if (notify) Changed?.Invoke();
    }

    public void Notify() => Changed?.Invoke();

    public decimal QuantityOnDate(Guid propertyId, DateTime date)
    {
        var end = date.Date.AddDays(1).AddTicks(-1);
        return Data.Movements
            .Where(m => m.PropertyId == propertyId && m.DocumentDate <= end)
            .Sum(m => m.QuantityDelta);
    }

    public bool WasPresentOnDate(Guid propertyId, DateTime date) =>
        QuantityOnDate(propertyId, date) > 0;

    public IEnumerable<Document> DocumentsFor(Guid propertyId) =>
        Data.Documents
            .Where(d => d.Lines.Any(l => l.PropertyId == propertyId))
            .OrderByDescending(d => d.Date)
            .ThenByDescending(d => d.CreatedAt);

    public List<StrongpointRow> StrongpointRows()
    {
        var props = Data.Properties.ToDictionary(p => p.Id);
        return QualityStateGroups()
            .Select(g =>
            {
                var total = 0m;
                foreach (var line in g.SelectMany(d => d.Lines))
                {
                    props.TryGetValue(line.PropertyId, out var p);
                    var price = line.UnitPrice > 0 ? line.UnitPrice : p?.UnitPrice ?? 0m;
                    total += price * line.Quantity;
                }

                return new StrongpointRow
                {
                    Name = g.Select(d => d.StrongpointName?.Trim() ?? "")
                        .FirstOrDefault(n => n.Length > 0) ?? "",
                    RegNumber = g.Select(d => d.StrongpointRegNumber?.Trim() ?? "")
                        .FirstOrDefault(n => n.Length > 0) ?? "",
                    CommissioningDate = g.Min(d => (d.CommissioningDate ?? d.Date).Date),
                    TotalValue = total,
                    // Чернетка, доки немає жодного проведеного ЯС цього ОП.
                    IsDraft = g.All(d => d.Status != DocStatus.Posted)
                };
            })
            .OrderBy(r => r.Name)
            .ThenBy(r => r.RegNumber)
            .ToList();
    }

    /// <summary>Усі акти ЯС цього ОП (чернетки й проведені) для картки / Акта ВР.</summary>
    public List<Document> QualityStateDocumentsFor(StrongpointRow row)
    {
        var key = StrongpointKey(row.RegNumber, row.Name);
        return QualityStateGroups()
            .FirstOrDefault(g => g.Key == key)
            ?.OrderBy(d => d.Date)
            .ThenBy(d => d.CreatedAt)
            .ToList() ?? new List<Document>();
    }

    /// <summary>Рядок переліку ОП за полями чернетки/акта ЯС (або null).</summary>
    public StrongpointRow? StrongpointRowFor(Document doc)
    {
        if (doc.Type != DocType.QualityState) return null;
        if (string.IsNullOrWhiteSpace(doc.StrongpointName) && string.IsNullOrWhiteSpace(doc.StrongpointRegNumber))
            return null;
        var key = StrongpointKey(doc.StrongpointRegNumber, doc.StrongpointName);
        return StrongpointRows().FirstOrDefault(r => StrongpointKey(r.RegNumber, r.Name) == key);
    }

    /// <summary>
    /// Правка дати основного акта (= дати ВР) і номера Акта ВР —
    /// лише з форми ЯС або ЗЯС (парний ВР).
    /// </summary>
    public void UpdateQualityStateDateAndVr(Document doc, DateTime date, string? vrActNumber)
    {
        if (!VrActBuilder.IsPairedSource(doc)) return;
        var target = Data.Documents.FirstOrDefault(d => d.Id == doc.Id) ?? doc;
        target.Date = date.Date;
        target.VrActNumber = (vrActNumber ?? "").Trim();
        if (!ReferenceEquals(target, doc))
        {
            doc.Date = target.Date;
            doc.VrActNumber = target.VrActNumber;
        }
        foreach (var m in Data.Movements.Where(x => x.DocumentId == target.Id))
            m.DocumentDate = target.Date;
        Save();
    }

    /// <summary>
    /// Один акт якісного стану на один номер і реєстраційний номер,
    /// навіть якщо рядки прийшли з різних таблиць чи з різним написанням назви.
    /// </summary>
    private void MergeSplitStrongpointActs()
    {
        var groups = Data.Documents
            .Where(d => d.Type == DocType.QualityState && d.Status == DocStatus.Posted)
            .GroupBy(ActIdentity)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in groups)
        {
            var keep = group.OrderBy(d => d.CreatedAt).First();
            foreach (var extra in group.Where(d => d.Id != keep.Id).ToList())
            {
                keep.Lines.AddRange(extra.Lines);
                if (string.IsNullOrWhiteSpace(keep.StrongpointName) && !string.IsNullOrWhiteSpace(extra.StrongpointName))
                    keep.StrongpointName = extra.StrongpointName;
                if (string.IsNullOrWhiteSpace(keep.StrongpointRegNumber) && !string.IsNullOrWhiteSpace(extra.StrongpointRegNumber))
                    keep.StrongpointRegNumber = extra.StrongpointRegNumber;
                foreach (var movement in Data.Movements.Where(m => m.DocumentId == extra.Id))
                    movement.DocumentId = keep.Id;
                Data.Documents.Remove(extra);
            }
        }
    }

    private void CapsStrongpointNames()
    {
        foreach (var doc in Data.Documents)
        {
            var caps = QualityStateNumber.Caps(doc.StrongpointName);
            if (caps != (doc.StrongpointName ?? ""))
                doc.StrongpointName = caps;
        }
    }

    private static string ActIdentity(Document doc) =>
        ImportGrouping.ActKey(doc.Date, doc.Number, doc.Type, doc.StrongpointRegNumber, doc.StrongpointName);

    private Document? FindStoredAct(DocType type, string number, string reg, string name)
    {
        if (type != DocType.QualityState || string.IsNullOrWhiteSpace(number)) return null;
        var num = number.Trim();
        var regKey = (reg ?? "").Trim().ToLowerInvariant();
        var nameKey = (name ?? "").Trim().ToLowerInvariant();
        var sameNumber = Data.Documents
            .Where(d => d.Type == DocType.QualityState
                        && d.Status == DocStatus.Posted
                        && string.Equals((d.Number ?? "").Trim(), num, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (regKey.Length > 0)
        {
            var byReg = sameNumber.FirstOrDefault(d =>
                string.Equals((d.StrongpointRegNumber ?? "").Trim(), reg, StringComparison.OrdinalIgnoreCase));
            if (byReg != null) return byReg;
        }
        if (nameKey.Length > 0)
        {
            var byName = sameNumber.FirstOrDefault(d =>
                string.Equals((d.StrongpointName ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName;
        }
        return sameNumber.Count == 1 ? sameNumber[0] : null;
    }

    public StrongpointDetail StrongpointDetail(StrongpointRow row)
    {
        var props = Data.Properties.ToDictionary(p => p.Id);
        var key = StrongpointKey(row.RegNumber, row.Name);
        var docs = QualityStateGroups()
            .FirstOrDefault(g => g.Key == key)
            ?.ToList() ?? new List<Document>();

        var summed = new Dictionary<(string Structure, Guid PropertyId), (string Name, string Unit, decimal Qty, decimal Price)>();
        foreach (var doc in docs.OrderBy(d => d.Date))
        {
            var structures = doc.Structures.ToDictionary(s => s.Id, s => s.Name);
            foreach (var line in doc.Lines)
            {
                props.TryGetValue(line.PropertyId, out var p);
                var price = line.UnitPrice > 0 ? line.UnitPrice : p?.UnitPrice ?? 0m;
                var structName = "";
                if (line.StructureId != null && structures.TryGetValue(line.StructureId.Value, out var sn))
                    structName = sn;
                var lineKey = (structName, line.PropertyId);
                if (summed.TryGetValue(lineKey, out var have))
                    summed[lineKey] = (have.Name, have.Unit, have.Qty + line.Quantity, have.Price);
                else
                    summed[lineKey] = (line.Name, string.IsNullOrWhiteSpace(line.Unit) ? p?.Unit ?? "" : line.Unit, line.Quantity, price);
            }
        }

        var lines = summed
            .OrderBy(x => x.Key.Structure)
            .ThenBy(x => x.Value.Name)
            .Select(x => new StrongpointLineRow
            {
                StructureName = x.Key.Structure,
                Name = x.Value.Name,
                Unit = x.Value.Unit,
                QuantityText = Ua.Num(x.Value.Qty, "#,##0.###"),
                PriceText = Ua.Num(x.Value.Price, "#,##0.####"),
                ValueText = Ua.Num(x.Value.Price * x.Value.Qty, "#,##0.00")
            })
            .ToList();

        var acts = docs
            .OrderByDescending(d => d.Date)
            .Select(d => new StrongpointActRow
            {
                DocumentId = d.Id,
                DateText = d.Date.ToString("dd.MM.yyyy"),
                Number = d.Number,
                Note = d.Note
            })
            .ToList();

        return new StrongpointDetail { Lines = lines, Acts = acts };
    }

    /// <summary>
    /// Опорні пункти з проведених і чернеток ЯС — ОП з’являється в переліку вже на чернетці.
    /// </summary>
    private IEnumerable<IGrouping<string, Document>> QualityStateGroups() =>
        Data.Documents
            .Where(d => d.Type == DocType.QualityState
                        && d.Status is DocStatus.Posted or DocStatus.Draft)
            .Where(d => !string.IsNullOrWhiteSpace(d.StrongpointName)
                        || !string.IsNullOrWhiteSpace(d.StrongpointRegNumber))
            .GroupBy(d => StrongpointKey(d.StrongpointRegNumber, d.StrongpointName));

    private static string StrongpointKey(string? regNumber, string? name)
    {
        var reg = (regNumber ?? "").Trim().ToLowerInvariant();
        var n = (name ?? "").Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(reg) ? $"n:{n}" : $"r:{reg}";
    }

    public void ClearWarehouse()
    {
        Data.CustomVyds ??= new List<string>();
        Data.VydKeywords ??= new List<VydKeywordEntry>();
        Data.VydDisplayUnits ??= new List<VydDisplayUnitEntry>();
        Data.VydSupplyServices ??= new List<VydSupplyServiceEntry>();
        Data.Stickers ??= new List<StickerNote>();
        Data.StockSummarySticker ??= new StockSummaryStickerState();
        Data.Nomenclatures ??= new List<NomenclatureItem>();

        var hasAnything =
            Data.Properties.Count > 0
            || Data.Nomenclatures.Count > 0
            || Data.Documents.Count > 0
            || Data.Movements.Count > 0
            || Data.CustomVyds.Count > 0
            || Data.VydKeywords.Count > 0
            || Data.VydDisplayUnits.Count > 0
            || Data.VydSupplyServices.Count > 0
            || Data.Stickers.Count > 0;

        if (hasAnything)
            BackupService.BeforeWarehouseClear(Data);

        Data.Properties.Clear();
        Data.Nomenclatures.Clear();
        Data.Documents.Clear();
        Data.Movements.Clear();
        Data.CustomVyds.Clear();
        Data.VydKeywords.Clear();
        Data.VydDisplayUnits.Clear();
        Data.VydSupplyServices.Clear();
        Data.Stickers.Clear();
        Data.StockSummarySticker = new StockSummaryStickerState();
        Data.HadFirstStockBackup = false;
        // Settings / WorkDays / LastActiveDate — лишаємо.
        Save();
    }

    /// <summary>
    /// Міграція: кожне майно без номенклатури отримує паспорт (тип за замовчуванням — Запаси).
    /// Name/Unit на Property лишаються копіями; при правці номенклатури синхронізуються.
    /// </summary>
    public void EnsureNomenclatures()
    {
        Data.Nomenclatures ??= new List<NomenclatureItem>();
        var byId = Data.Nomenclatures.ToDictionary(n => n.Id);
        foreach (var p in Data.Properties)
        {
            if (p.NomenclatureId is Guid nid && byId.ContainsKey(nid))
                continue;
            var nom = FindOrCreateNomenclature(p.Name, p.Unit, NomenclatureKind.Stocks, createIfMissing: true);
            p.NomenclatureId = nom.Id;
            byId[nom.Id] = nom;
        }
    }

    public NomenclatureItem? GetNomenclature(Guid? id) =>
        id == null || id == Guid.Empty
            ? null
            : Data.Nomenclatures.FirstOrDefault(n => n.Id == id.Value);

    /// <summary>Знайти або створити паспорт за назвою+одиницею.</summary>
    public NomenclatureItem FindOrCreateNomenclature(
        string? name, string? unit, NomenclatureKind kind = NomenclatureKind.Stocks, bool createIfMissing = true)
    {
        var n = (name ?? "").Trim();
        var u = string.IsNullOrWhiteSpace(unit) ? "шт" : unit.Trim();
        var keyN = NormKey(n);
        var keyU = NormKey(u);
        var found = Data.Nomenclatures.FirstOrDefault(x =>
            NormKey(x.Name) == keyN && NormKey(string.IsNullOrWhiteSpace(x.Unit) ? "шт" : x.Unit) == keyU);
        if (found != null) return found;
        if (!createIfMissing)
            throw new InvalidOperationException("Номенклатуру не знайдено.");
        var item = new NomenclatureItem
        {
            Name = n.Length > 0 ? n : "Без назви",
            Unit = u,
            Kind = kind,
            CreatedAt = DateTime.Now
        };
        Data.Nomenclatures.Add(item);
        return item;
    }

    /// <summary>Прив’язати майно до номенклатури (створити паспорт за потреби).</summary>
    public NomenclatureItem EnsurePropertyNomenclature(PropertyItem property)
    {
        var existing = GetNomenclature(property.NomenclatureId);
        if (existing != null) return existing;
        var nom = FindOrCreateNomenclature(property.Name, property.Unit, NomenclatureKind.Stocks);
        property.NomenclatureId = nom.Id;
        return nom;
    }

    /// <summary>Оновити паспорт і синхронізувати Name/Unit у всіх пов’язаних Property.</summary>
    public void UpdateNomenclature(
        Guid id,
        string name,
        string unit,
        NomenclatureKind kind,
        string? note = null,
        string? vyd = null,
        string? displayUnit = null,
        decimal? displayFactor = null,
        bool updateDisplayFields = false,
        string? supplyService = null,
        bool updateSupplyService = false)
    {
        var nom = Data.Nomenclatures.FirstOrDefault(n => n.Id == id)
                  ?? throw new InvalidOperationException("Номенклатуру не знайдено.");
        nom.Name = (name ?? "").Trim();
        // Офіційна О.В. — лише з цього поля (програма не підміняє відображенням).
        nom.Unit = string.IsNullOrWhiteSpace(unit) ? "шт" : unit.Trim();
        nom.Kind = kind;
        if (vyd != null) nom.Vyd = vyd.Trim();
        if (note != null) nom.Note = note.Trim();
        if (updateDisplayFields)
        {
            nom.DisplayUnit = (displayUnit ?? "").Trim();
            nom.DisplayFactor = displayFactor is > 0 ? displayFactor : null;
        }
        if (updateSupplyService)
            ApplyNomenclatureSupply(nom, supplyService);
        foreach (var p in Data.Properties.Where(p => p.NomenclatureId == id))
        {
            p.Name = nom.Name;
            p.Unit = nom.Unit;
        }
        if (!string.IsNullOrWhiteSpace(nom.Vyd))
            NomenclatureVydNameMatch.LearnFromName(nom.Name, nom.Vyd, Data.VydKeywords);
        Save();
    }

    public NomenclatureItem AddNomenclature(
        string name,
        string unit,
        NomenclatureKind kind,
        string? note = null,
        string? vyd = null,
        string? displayUnit = null,
        decimal? displayFactor = null,
        string? supplyService = null)
    {
        var item = new NomenclatureItem
        {
            Name = (name ?? "").Trim(),
            Unit = string.IsNullOrWhiteSpace(unit) ? "шт" : unit.Trim(),
            Kind = kind,
            Vyd = (vyd ?? "").Trim(),
            DisplayUnit = (displayUnit ?? "").Trim(),
            DisplayFactor = displayFactor is > 0 ? displayFactor : null,
            Note = (note ?? "").Trim(),
            SupplyService = SupplyServiceHelper.Normalize(supplyService) ?? "",
            CreatedAt = DateTime.Now
        };
        if (item.Name.Length == 0) item.Name = "Без назви";
        Data.Nomenclatures.Add(item);
        if (!string.IsNullOrWhiteSpace(item.Vyd))
            NomenclatureVydNameMatch.LearnFromName(item.Name, item.Vyd, Data.VydKeywords);
        Save();
        return item;
    }

    /// <summary>
    /// Масове призначення служби забезпечення на паспорти + синхрон на пов’язані картки майна.
    /// Порожнє значення очищає службу. Один Save на пакет.
    /// </summary>
    public void UpdateNomenclatureSupplyServices(IEnumerable<(Guid Id, string? SupplyService)> updates)
    {
        EnsureNomenclatures();
        var changed = false;
        foreach (var (id, supplyRaw) in updates)
        {
            var nom = Data.Nomenclatures.FirstOrDefault(n => n.Id == id);
            if (nom == null) continue;
            if (!ApplyNomenclatureSupply(nom, supplyRaw)) continue;
            changed = true;
        }
        if (changed) Save();
    }

    /// <summary>
    /// Показати службу паспорта; якщо порожньо — єдина непорожня серед пов’язаних карток
    /// (різні значення → порожньо, щоб не підставляти «випадкову»).
    /// </summary>
    public string ResolveNomenclatureSupply(NomenclatureItem nom)
    {
        var stored = SupplyServiceHelper.Normalize(nom.SupplyService) ?? "";
        if (stored.Length > 0) return stored;
        var linked = Data.Properties
            .Where(p => p.NomenclatureId == nom.Id)
            .Select(p => SupplyServiceHelper.Normalize(p.SupplyService) ?? "")
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return linked.Count == 1 ? linked[0] : "";
    }

    /// <returns>true, якщо значення на паспорті або картках змінилось.</returns>
    private bool ApplyNomenclatureSupply(NomenclatureItem nom, string? supplyRaw)
    {
        var norm = SupplyServiceHelper.Normalize(supplyRaw) ?? "";
        var changed = !string.Equals(nom.SupplyService ?? "", norm, StringComparison.Ordinal);
        nom.SupplyService = norm;
        foreach (var p in Data.Properties.Where(p => p.NomenclatureId == nom.Id))
        {
            if (string.Equals(p.SupplyService ?? "", norm, StringComparison.Ordinal)) continue;
            p.SupplyService = norm;
            changed = true;
        }
        return changed;
    }

    /// <summary>Додати користувацький вид у CustomVyds (фіксований каталог не чіпаємо).</summary>
    public string? AddCustomVyd(string? rawName)
    {
        Data.CustomVyds ??= new List<string>();
        var name = NomenclatureVydCatalog.NormalizeNewVydName(rawName, Data.CustomVyds);
        if (name == null) return null;
        Data.CustomVyds.Add(name);
        Save();
        return name;
    }

    /// <summary>О.В. відображення для Виду (копія списку).</summary>
    public IReadOnlyList<string> GetDisplayUnitsForVyd(string? vyd) =>
        VydDisplayUnitHelper.UnitsForVyd(Data.VydDisplayUnits, vyd);

    /// <summary>Додати О.В. відображення для Виду. Повертає нормалізовану назву або null.</summary>
    public string? AddDisplayUnitForVyd(string? vyd, string? unitRaw)
    {
        var key = (vyd ?? "").Trim();
        if (key.Length == 0 || NomenclatureVydCatalog.IsNewItem(key)) return null;
        Data.VydDisplayUnits ??= new List<VydDisplayUnitEntry>();
        var existing = GetDisplayUnitsForVyd(key);
        var unit = VydDisplayUnitHelper.NormalizeUnitName(unitRaw, existing);
        if (unit == null) return null;
        var entry = Data.VydDisplayUnits.FirstOrDefault(e =>
            string.Equals((e.Vyd ?? "").Trim(), key, StringComparison.Ordinal));
        if (entry == null)
        {
            entry = new VydDisplayUnitEntry { Vyd = key, Units = new List<string>() };
            Data.VydDisplayUnits.Add(entry);
        }
        entry.Units ??= new List<string>();
        entry.Units.Add(unit);
        Save();
        return unit;
    }

    /// <summary>
    /// Масове оновлення О.В. відображення + коефіцієнта (ті самі правила, що на картці).
    /// Один Save на пакет. Порожній / недійсний коеф. → null; «+ Додати О.В.» → порожня О.В.
    /// </summary>
    public void UpdateNomenclatureDisplayFields(
        IEnumerable<(Guid Id, string? DisplayUnit, decimal? DisplayFactor)> updates)
    {
        EnsureNomenclatures();
        var changed = false;
        foreach (var (id, displayUnitRaw, displayFactor) in updates)
        {
            var nom = Data.Nomenclatures.FirstOrDefault(n => n.Id == id);
            if (nom == null) continue;
            var (unit, factor) = NormalizeDisplayPair(displayUnitRaw, displayFactor);
            if (string.Equals(nom.DisplayUnit ?? "", unit, StringComparison.Ordinal)
                && nom.DisplayFactor == factor)
                continue;
            nom.DisplayUnit = unit;
            nom.DisplayFactor = factor;
            changed = true;
        }
        if (changed) Save();
    }

    /// <summary>Нормалізація пари О.В.+коеф. як на картці / у масовому вікні.</summary>
    internal static (string DisplayUnit, decimal? DisplayFactor) NormalizeDisplayPair(
        string? displayUnitRaw, decimal? displayFactor)
    {
        var unit = (displayUnitRaw ?? "").Trim();
        if (VydDisplayUnitHelper.IsAddItem(unit)) unit = "";
        var factor = displayFactor is > 0 ? displayFactor : null;
        return (unit, factor);
    }

    /// <summary>
    /// Масово записати Вид для позицій номенклатури (діалог після вибору Виду в картці).
    /// Тип майна (ОЗ/Запаси) не чіпаємо — користувач змінює в картці за потреби.
    /// Паралельно навчає ключі з назв (конфлікт ключа → старе лишається).
    /// </summary>
    public void AssignNomenclatureVyds(IEnumerable<(Guid Id, string Vyd)> assignments)
    {
        EnsureNomenclatures();
        Data.VydKeywords ??= new List<VydKeywordEntry>();
        var changed = false;
        foreach (var (id, vydRaw) in assignments)
        {
            var nom = Data.Nomenclatures.FirstOrDefault(n => n.Id == id);
            if (nom == null) continue;
            var vyd = (vydRaw ?? "").Trim();
            if (vyd.Length == 0) continue;
            if (NomenclatureVydNameMatch.LearnFromName(nom.Name, vyd, Data.VydKeywords))
                changed = true;
            if (string.Equals(nom.Vyd, vyd, StringComparison.Ordinal)) continue;
            nom.Vyd = vyd;
            changed = true;
        }
        if (changed) Save();
    }

    /// <summary>Тип номенклатури позиції (без паспорта — Запаси після Ensure).</summary>
    public NomenclatureKind KindOf(PropertyItem property)
    {
        EnsureNomenclatures();
        var nom = GetNomenclature(property.NomenclatureId);
        if (nom != null) return nom.Kind;
        return EnsurePropertyNomenclature(property).Kind;
    }

    public bool IsFixedAsset(PropertyItem property) =>
        KindOf(property) == NomenclatureKind.FixedAssets;

    /// <summary>Майно, чий паспорт має тип «Основні засоби».</summary>
    public List<PropertyItem> FixedAssetProperties() =>
        Data.Properties
            .Where(IsFixedAsset)
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Unit, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Майно типу «Запаси» для таблиці залишків на складі.</summary>
    public List<PropertyItem> StockProperties() =>
        Data.Properties
            .Where(p => !IsFixedAsset(p))
            .ToList();

    /// <summary>
    /// Перелік майна для додавання в чернетку списання/зняття зі складу:
    /// лише запаси, або запаси + основні засоби (кнопка «Основні засоби»).
    /// </summary>
    public List<PropertyItem> PropertiesForDraftPick(bool includeFixedAssets)
    {
        if (!includeFixedAssets) return StockProperties();
        return Data.Properties
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Unit, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public sealed class ImportSummary
    {
        public int Rows { get; set; }
        public int Receipts { get; set; }
        public int Returns { get; set; }
        public int WriteOffs { get; set; }
        public int Losses { get; set; }
        public int Issues { get; set; }
        public int Transfers { get; set; }
        public int Inventory { get; set; }
        public int OnStock { get; set; }
        public int WrittenOff { get; set; }
        /// <summary>Позиції з від’ємним залишком після імпорту.</summary>
        public int NegativeStock { get; set; }
        public List<string> Warnings { get; } = new();
    }

    public ImportSummary ImportRows(List<ImportRow> rows, string? documentNote = null)
    {
        var summary = new ImportSummary { Rows = rows.Count };
        if (rows.Count == 0) return summary;

        var note = string.IsNullOrWhiteSpace(documentNote)
            ? "Імпорт з файлу"
            : documentNote.Trim();

        // Спочатку дата. У той самий день прихід іде перед списанням:
        // інакше отримання цього дня виглядає так, ніби його ще не було.
        var ordered = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Name) && r.Quantity > 0)
            .OrderBy(r => r.Date)
            .ThenBy(r => SameDayInboundFirst(r.Kind))
            .ThenBy(r => r.Number)
            .ToList();

        var docs = new Dictionary<string, Document>();

        foreach (var row in ordered)
        {
            if (row.Kind == ImportMoveKind.Unknown)
            {
                summary.Warnings.Add(
                    $"{row.Date:dd.MM.yyyy} «{row.Name}»: тип руху невизначений — рядок пропущено. Оберіть тип вручну.");
                continue;
            }

            var item = FindOrCreateItem(row);
            var resolved = ImportGrouping.Resolve(row);
            var type = resolved.Type;
            var number = resolved.Number;

            var key = ImportGrouping.ActKey(row.Date, number, type, resolved.RegNumber, resolved.StrongpointName);
            if (!docs.TryGetValue(key, out var doc))
            {
                doc = FindStoredAct(type, number, resolved.RegNumber, resolved.StrongpointName);
                if (doc == null)
                {
                    doc = new Document
                    {
                        Type = type,
                        Number = string.IsNullOrWhiteSpace(number) ? NextNumber(Prefix(type)) : number,
                        Date = row.Date.Date,
                        Status = DocStatus.Posted,
                        PostedAt = DateTime.Now,
                        Note = note,
                        StrongpointName = resolved.StrongpointName,
                        StrongpointRegNumber = resolved.RegNumber,
                        CommissioningDate = type == DocType.QualityState ? row.Date.Date : null
                    };
                    Data.Documents.Add(doc);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(doc.StrongpointName))
                        doc.StrongpointName = resolved.StrongpointName;
                    if (string.IsNullOrWhiteSpace(doc.StrongpointRegNumber))
                        doc.StrongpointRegNumber = resolved.RegNumber;
                }
                docs[key] = doc;
            }

            MergeImportNote(doc, string.IsNullOrWhiteSpace(row.Note) ? row.Operation : row.Note, note);

            doc.Lines.Add(new DocumentLine
            {
                PropertyId = item.Id,
                Name = item.Name,
                Unit = item.Unit,
                Quantity = row.Quantity,
                UnitPrice = row.Price > 0 ? row.Price : item.UnitPrice
            });

            // Kind у русі — робочий тип руху, не назва документа і не «Вид операції» дослівно.
            var movementKind = MoveKindNames.Of(type);

            if (DocTypeNames.DecreasesStock(type))
            {
                var have = item.Quantity;
                if (row.Quantity > have + 0.0001m)
                {
                    var after = have - row.Quantity;
                    summary.Warnings.Add(
                        $"{row.Date:dd.MM.yyyy} «{item.Name}»: у файлі {Ua.Num(row.Quantity, "#,##0.###")}, " +
                        $"на складі було {Ua.Num(have, "#,##0.###")} → {Ua.Num(after, "#,##0.###")} {item.Unit}");
                }

                // Повний рух, без обрізання до нуля — мінус лишається на складі.
                item.Quantity -= row.Quantity;
                if (item.Quantity < -0.0001m)
                    item.FullyWrittenOff = false;
                else if (DocTypeNames.IsWriteOff(type) && Math.Abs(item.Quantity) <= 0.0001m)
                    item.FullyWrittenOff = true;
                AddMovement(doc, item.Id, item.Name, -row.Quantity, movementKind);
            }
            else
            {
                item.Quantity += row.Quantity;
                item.FullyWrittenOff = false;
                AddMovement(doc, item.Id, item.Name, row.Quantity, movementKind);
            }

            switch (row.Kind)
            {
                case ImportMoveKind.WriteOff: summary.WriteOffs++; break;
                case ImportMoveKind.Loss: summary.Losses++; break;
                case ImportMoveKind.Issue: summary.Issues++; break;
                case ImportMoveKind.Return: summary.Returns++; break;
                case ImportMoveKind.Transfer: summary.Transfers++; break;
                case ImportMoveKind.Receipt: summary.Receipts++; break;
                case ImportMoveKind.Unknown: break;
                default: summary.Inventory++; break;
            }
        }

        foreach (var p in Data.Properties)
        {
            if (p.Quantity < -0.0001m)
                p.FullyWrittenOff = false;
            else if (p.Quantity > 0.0001m)
                p.FullyWrittenOff = false;
            else
                p.FullyWrittenOff = Data.Movements.Any(m =>
                    m.PropertyId == p.Id && m.QuantityDelta < 0 && DocTypeNames.IsWriteOff(m.DocumentType));
        }

        MergeSplitStrongpointActs();
        if (docs.Count > 0)
            BackupService.Movement(Data, docs.Values.First(), "імпорт");
        BackupService.Stock(Data, "після_імпорту");
        summary.OnStock = Data.Properties.Count(p => !p.FullyWrittenOff && p.Quantity > 0.0001m);
        summary.WrittenOff = Data.Properties.Count(p => p.FullyWrittenOff);
        summary.NegativeStock = Data.Properties.Count(p => p.Quantity < -0.0001m);
        Save();
        return summary;
    }

    /// <summary>0 — прихід того дня, 1 — списання та інше зменшення.</summary>
    private static int SameDayInboundFirst(ImportMoveKind kind) =>
        kind is ImportMoveKind.Receipt or ImportMoveKind.Inventory ? 0 : 1;

    private static void MergeImportNote(Document doc, string? rowNote, string fallback)
    {
        var extra = (rowNote ?? "").Trim();
        if (extra.Length == 0) return;
        if (string.IsNullOrWhiteSpace(doc.Note) || doc.Note == fallback)
            doc.Note = extra;
        else if (!doc.Note.Split('\n').Any(line => line.Trim() == extra))
            doc.Note += "\n" + extra;
    }

    private PropertyItem FindOrCreateItem(ImportRow row)
    {
        var n = NormKey(row.Name);
        var unit = NormKey(string.IsNullOrWhiteSpace(row.Unit) ? "шт" : row.Unit);
        var same = Data.Properties
            .Where(p => NormKey(p.Name) == n
                        && NormKey(string.IsNullOrWhiteSpace(p.Unit) ? "шт" : p.Unit) == unit)
            .ToList();
        // Однакова назва з іншою ціною — окрема картка. Збіг лише в межах 5 копійок.
        var found = same.FirstOrDefault(p => Math.Abs(p.UnitPrice - row.Price) < 0.05m);

        if (found != null)
        {
            if (found.UnitPrice == 0 && row.Price > 0) found.UnitPrice = row.Price;
            if (string.IsNullOrWhiteSpace(found.Category) && !string.IsNullOrWhiteSpace(row.Category))
                found.Category = row.Category;
            ApplySupplyService(found, row.SupplyService);
            EnsurePropertyNomenclature(found);
            return found;
        }

        var item = new PropertyItem
        {
            Name = row.Name,
            Unit = string.IsNullOrWhiteSpace(row.Unit) ? "шт" : row.Unit,
            Category = row.Category,
            UnitPrice = row.Price,
            Quantity = 0
        };
        ApplySupplyService(item, row.SupplyService);
        EnsurePropertyNomenclature(item);
        Data.Properties.Add(item);
        return item;
    }

    /// <summary>Записати службу забезпечення з імпорту, якщо користувач (або авто) обрав значення.</summary>
    private static void ApplySupplyService(PropertyItem item, string? supply)
    {
        var svc = SupplyServiceHelper.Normalize(supply);
        if (svc == null) return;
        item.SupplyService = svc;
    }

    private static string NormKey(string s)
    {
        var t = (s ?? "").Trim().ToLowerInvariant();
        while (t.Contains("  ")) t = t.Replace("  ", " ");
        return t;
    }

    public void AddInventory(PropertyItem item)
    {
        EnsurePropertyNomenclature(item);
        Data.Properties.Add(item);
        var doc = new Document
        {
            Type = DocType.Inventory,
            Number = NextNumber("ІНВ"),
            Date = DateTime.Today,
            Status = DocStatus.Posted,
            PostedAt = DateTime.Now,
            Lines = { new DocumentLine { PropertyId = item.Id, Name = item.Name, Unit = item.Unit, Quantity = item.Quantity } }
        };
        Data.Documents.Add(doc);
        AddMovement(doc, item.Id, item.Name, item.Quantity, MoveKindNames.Of(DocType.Inventory));
        BackupService.Movement(Data, doc, "зберегти");
        if (!Data.HadFirstStockBackup)
            BackupService.Stock(Data, "перші_залишки");
        Save();
    }

    public Document CreateDraft(DocType type, string number, DateTime date, string note, string? workTypeName = null)
    {
        var doc = new Document
        {
            Type = type,
            Number = DocTypeNames.NormalizeNumber(number),
            Date = date.Date,
            Note = note,
            WorkTypeName = (workTypeName ?? "").Trim(),
            Status = DocStatus.Draft,
            CommissioningDate = type == DocType.QualityState ? date.Date : null
        };
        if (doc.Number != DocTypeNames.Numberless && QualityStateNumber.LooksLike(doc.Number))
            QualityStateNumber.ApplyTo(doc);
        else if (type == DocType.QualityState)
            doc.CommissioningDate ??= date.Date;
        return doc;
    }

    /// <summary>Копія для редагування. Збережена чернетка в даних не змінюється, доки не натиснуть «Зберегти».</summary>
    public static Document CloneDraft(Document src)
    {
        var copy = new Document { Id = src.Id, CreatedAt = src.CreatedAt };
        CopyDraft(src, copy);
        return copy;
    }

    public void SaveDraft(Document doc)
    {
        var target = StoreDraft(doc);
        RecalcAnachronistic(target);
        ApplyReservation(target);
        // Дата ЯС = дата ВР: якщо змінили дату проведеного ЯС через чернетку — підтягнути рухи.
        if (target.Type == DocType.QualityState)
        {
            foreach (var m in Data.Movements.Where(x => x.DocumentId == target.Id))
                m.DocumentDate = target.Date;
        }
        BackupService.Movement(Data, target, "зберегти");
        Save();
    }

    /// <summary>Кладе чернетку в дані. Якщо така вже збережена — переносить у неї поточний вміст.</summary>
    public Document StoreDraft(Document doc)
    {
        var stored = Data.Documents.FirstOrDefault(d => d.Id == doc.Id);
        if (stored == null)
        {
            Data.Documents.Add(doc);
            return doc;
        }

        if (!ReferenceEquals(stored, doc))
            CopyDraft(doc, stored);
        return stored;
    }

    private static void CopyDraft(Document from, Document to)
    {
        to.Type = from.Type;
        to.Number = from.Number;
        to.Date = from.Date;
        to.Status = from.Status;
        to.Note = from.Note;
        to.WorkTypeName = from.WorkTypeName;
        to.PostedAt = from.PostedAt;
        to.StrongpointName = from.StrongpointName;
        to.CommissioningDate = from.CommissioningDate;
        to.StrongpointRegNumber = from.StrongpointRegNumber;
        to.VrActNumber = from.VrActNumber;
        to.ActiveStructureId = from.ActiveStructureId;
        to.FormSeries = from.FormSeries;
        to.FormNumber = from.FormNumber;
        to.BarrierName = from.BarrierName;
        to.Structures = from.Structures
            .Select(s => new FortStructure { Id = s.Id, Name = s.Name, Order = s.Order })
            .ToList();
        to.Lines = from.Lines.Select(l => new DocumentLine
        {
            PropertyId = l.PropertyId,
            Name = l.Name,
            Unit = l.Unit,
            Quantity = l.Quantity,
            Anachronistic = l.Anachronistic,
            StructureId = l.StructureId,
            UnitPrice = l.UnitPrice
        }).ToList();
    }

    public string? DeleteDocument(Document doc)
    {
        if (doc == null) return "Документ не знайдено.";
        if (!Data.Documents.Contains(doc))
        {
            var found = Data.Documents.FirstOrDefault(d => d.Id == doc.Id);
            if (found == null) return "Документ не знайдено.";
            doc = found;
        }

        Data.Documents.Remove(doc);
        Data.Movements.RemoveAll(m => m.DocumentId == doc.Id);
        RebuildStockFromMovements();
        RecalcReservations();
        BackupService.Movement(Data, doc, "видалити");
        Save();
        return null;
    }

    private void RebuildStockFromMovements()
    {
        foreach (var p in Data.Properties)
        {
            p.Quantity = 0;
            p.ReservedQuantity = 0;
            p.FullyWrittenOff = false;
        }

        foreach (var m in Data.Movements
                     .OrderBy(m => m.DocumentDate)
                     .ThenBy(m => m.At))
        {
            var item = Data.Properties.FirstOrDefault(p => p.Id == m.PropertyId);
            if (item == null) continue;
            item.Quantity += m.QuantityDelta;
        }

        foreach (var p in Data.Properties)
        {
            if (p.Quantity < -0.0001m)
            {
                // від’ємний залишок — видимий на складі, не «повністю списано»
                p.FullyWrittenOff = false;
                continue;
            }
            if (p.Quantity > 0.0001m)
            {
                p.FullyWrittenOff = false;
                continue;
            }
            p.FullyWrittenOff = Data.Movements.Any(m =>
                m.PropertyId == p.Id && m.QuantityDelta < 0 && DocTypeNames.IsWriteOff(m.DocumentType));
        }
    }

    public string? Post(Document doc)
    {
        if (doc.Status == DocStatus.Posted)
            return "Документ уже проведено.";
        if (doc.Lines.Count == 0)
            return "У документі немає рядків.";

        RecalcAnachronistic(doc);

        foreach (var line in doc.Lines)
        {
            var item = Data.Properties.FirstOrDefault(p => p.Id == line.PropertyId);
            if (item == null)
                return $"Майно «{line.Name}» не знайдено.";

            if (DocTypeNames.DecreasesStock(doc.Type))
            {
                var available = item.Quantity + item.ReservedQuantity;
                if (line.Quantity > available + 0.0001m)
                    return $"Недостатньо «{item.Name}». Є {available}, у документі {line.Quantity}.";
            }
        }

        doc = StoreDraft(doc);
        doc.Status = DocStatus.Posted;
        doc.PostedAt = DateTime.Now;
        RecalcReservations();

        foreach (var line in doc.Lines)
        {
            var item = Data.Properties.First(p => p.Id == line.PropertyId);
            if (DocTypeNames.DecreasesStock(doc.Type))
            {
                item.Quantity -= line.Quantity;
                if (DocTypeNames.IsWriteOff(doc.Type)
                    && Math.Abs(item.Quantity) <= 0.0001m
                    && item.ReservedQuantity <= 0.0001m)
                    item.FullyWrittenOff = true;
                else if (item.Quantity < -0.0001m)
                    item.FullyWrittenOff = false;
                AddMovement(doc, item.Id, item.Name, -line.Quantity, MoveKindNames.Of(doc.Type));
            }
            else
            {
                item.Quantity += line.Quantity;
                item.FullyWrittenOff = false;
                AddMovement(doc, item.Id, item.Name, line.Quantity, MoveKindNames.Of(doc.Type));
            }
        }

        BackupService.Movement(Data, doc, "провести");
        Save();
        return null;
    }

    public void RecalcAnachronistic(Document doc)
    {
        foreach (var line in doc.Lines)
            line.Anachronistic = QuantityOnDate(line.PropertyId, doc.Date) <= 0;
    }

    public void CloseAppBackup()
    {
        if (Data.Properties.Count > 0)
            BackupService.Stock(Data, "закриття");
        Save();
    }

    private void ApplyReservation(Document doc)
    {
        if (!DocTypeNames.DecreasesStock(doc.Type))
            return;

        RecalcReservations();
    }

    public void RecalcReservations()
    {
        foreach (var p in Data.Properties)
        {
            p.Quantity += p.ReservedQuantity;
            p.ReservedQuantity = 0;
        }

        foreach (var draft in Data.Documents.Where(d =>
                     d.Status == DocStatus.Draft && DocTypeNames.DecreasesStock(d.Type)))
        {
            foreach (var line in draft.Lines)
            {
                var item = Data.Properties.FirstOrDefault(p => p.Id == line.PropertyId);
                if (item == null) continue;
                var canReserve = Math.Min(line.Quantity, item.Quantity);
                item.Quantity -= canReserve;
                item.ReservedQuantity += canReserve;
            }
        }
    }

    private void AddMovement(Document doc, Guid propertyId, string name, decimal delta, string kind)
    {
        Data.Movements.Add(new Movement
        {
            DocumentId = doc.Id,
            DocumentDate = doc.Date,
            DocumentType = doc.Type,
            DocumentNumber = doc.Number,
            PropertyId = propertyId,
            PropertyName = name,
            QuantityDelta = delta,
            Kind = kind,
            At = DateTime.Now
        });
    }

    /// <summary>Ручний вибір робочого типу руху для рядка (коли імпорт не визначив Kind).</summary>
    public bool SetMovementKind(Guid documentId, Guid propertyId, string moveKindLabel)
    {
        var label = (moveKindLabel ?? "").Trim();
        if (string.IsNullOrEmpty(label) || MoveKindNames.IsUnknown(label)) return false;
        var importKind = MoveKindNames.ToImportKind(label);
        if (importKind == ImportMoveKind.Unknown) return false;

        var docType = ImportGrouping.TypeOf(importKind);
        var kind = MoveKindNames.Of(docType);
        var changed = false;
        foreach (var m in Data.Movements.Where(x => x.DocumentId == documentId && x.PropertyId == propertyId))
        {
            m.Kind = kind;
            m.DocumentType = docType;
            changed = true;
        }
        var doc = Data.Documents.FirstOrDefault(d => d.Id == documentId);
        if (doc != null)
            doc.Type = docType;
        if (changed) Save();
        return changed;
    }

    public string NextNumber(string prefix)
    {
        var n = Data.Documents.Count + 1;
        return $"{prefix}-{n:0000}";
    }

    /// <summary>Зведення для вікна при старті: резерв і чернетки списання.</summary>
    public StartupSummary GetStartupSummary()
    {
        var reserved = Data.Properties.Where(p => p.ReservedQuantity > 0.0001m).ToList();
        var value = reserved.Sum(p => p.UnitPrice * p.ReservedQuantity);
        var drafts = Data.Documents.Count(d =>
            d.Status == DocStatus.Draft && DocTypeNames.DecreasesStock(d.Type));
        return new StartupSummary(reserved.Count, value, drafts);
    }

    private static string Prefix(DocType type) => type switch
    {
        DocType.WriteOff => "СП",
        DocType.QualityChange => "ЗЯС",
        DocType.QualityState => "ЯС",
        DocType.WorkAct => "АВР",
        DocType.ExplosiveWorks => "ВВР",
        DocType.Issue => "ВД",
        DocType.Receipt => "ОТР",
        DocType.Return => "ПОВ",
        DocType.Transfer => "ПЕР",
        DocType.Loss => "ВТР",
        _ => "ДОК"
    };

    private void EnsureDefaultColumns()
    {
        if (Data.Settings.Columns.Count > 0) return;
        for (var i = 0; i < ColumnCatalog.All.Length; i++)
        {
            var c = ColumnCatalog.All[i];
            Data.Settings.Columns.Add(new ColumnSetting
            {
                Key = c.Key,
                Visible = true,
                Width = c.DefaultWidth,
                Order = i
            });
        }
    }
}
