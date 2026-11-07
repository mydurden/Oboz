namespace Obozny;

/// <summary>Група рядків імпорту, що стане одним документом.</summary>
public readonly record struct ResolvedImport(DocType Type, string Number, string StrongpointName, string RegNumber);

public sealed class ImportDocGroup
{
    public DocType Type { get; set; }
    public ImportMoveKind Kind { get; set; }
    public string Number { get; set; } = "";
    public DateTime Date { get; set; }
    public string StrongpointName { get; set; } = "";
    public string StrongpointRegNumber { get; set; } = "";
    public List<ImportRow> Rows { get; } = new();
}

public static class ImportGrouping
{
    public static List<ImportDocGroup> Group(IEnumerable<ImportRow> rows)
    {
        var ordered = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Name) && r.Quantity > 0)
            .OrderBy(r => r.Date)
            .ThenBy(r => r.Number)
            .ToList();

        var map = new Dictionary<string, ImportDocGroup>();
        var list = new List<ImportDocGroup>();

        foreach (var row in ordered)
        {
            var resolved = Resolve(row);
            var type = resolved.Type;
            var number = resolved.Number;
            var strongpoint = resolved.StrongpointName;
            var regNo = resolved.RegNumber;

            var key = ActKey(row.Date, number, type, regNo, strongpoint);
            if (!map.TryGetValue(key, out var group))
            {
                group = new ImportDocGroup
                {
                    Type = type,
                    Kind = row.Kind,
                    Number = number,
                    Date = row.Date.Date,
                    StrongpointName = strongpoint,
                    StrongpointRegNumber = regNo
                };
                map[key] = group;
                list.Add(group);
            }
            group.Rows.Add(row);
        }

        return list;
    }

    /// <summary>
    /// Назва ОП і реєстраційний номер — окремі стовпці журналу або складений номер акта.
    /// Списання з опорним пунктом стає актом якісного стану.
    /// </summary>
    public static ResolvedImport Resolve(ImportRow row)
    {
        var type = TypeOf(row.Kind);
        var number = string.IsNullOrWhiteSpace(row.Number) ? "" : row.Number.Trim();
        var sp = (row.StrongpointName ?? "").Trim();
        var reg = (row.StrongpointRegNumber ?? "").Trim();
        if (QualityStateNumber.TryParse(number, out var actNo, out var parsedName, out var parsedReg))
        {
            type = DocType.QualityState;
            number = actNo;
            if (sp.Length == 0) sp = parsedName;
            if (reg.Length == 0) reg = parsedReg;
        }
        else if (type == DocType.WriteOff && (sp.Length > 0 || reg.Length > 0))
            type = DocType.QualityState;

        return new ResolvedImport(type, number, QualityStateNumber.Caps(sp), reg);
    }

    public static string ActKey(DateTime date, string number, DocType type, string? reg, string? name)
    {
        var num = (number ?? "").Trim().ToLowerInvariant();
        var regKey = (reg ?? "").Trim().ToLowerInvariant();
        var nameKey = (name ?? "").Trim().ToLowerInvariant();
        var who = regKey.Length > 0 ? "r:" + regKey : "n:" + nameKey;
        // Акт якісного стану з іншого файла — той самий, якщо збігся номер і опорний пункт.
        if (type == DocType.QualityState && num.Length > 0)
            return $"qs|{num}|{who}";
        return $"{date:yyyyMMdd}|{num}|{type}|{who}";
    }

    public static DocType TypeOf(ImportMoveKind kind) => kind switch
    {
        ImportMoveKind.WriteOff => DocType.WriteOff,
        ImportMoveKind.Loss => DocType.Loss,
        ImportMoveKind.Issue => DocType.Issue,
        ImportMoveKind.Return => DocType.Return,
        ImportMoveKind.Transfer => DocType.Transfer,
        ImportMoveKind.Receipt => DocType.Receipt,
        ImportMoveKind.Inventory => DocType.Inventory,
        // Невизначений не перетворюємо на інвентаризацію навмання.
        ImportMoveKind.Unknown => DocType.Inventory,
        _ => DocType.Inventory
    };

    public static string MoveKindLabel(ImportMoveKind kind) => kind switch
    {
        ImportMoveKind.WriteOff or ImportMoveKind.Loss => MoveKindNames.WriteOff,
        ImportMoveKind.Receipt => MoveKindNames.Receipt,
        ImportMoveKind.Return => MoveKindNames.Return,
        ImportMoveKind.Transfer => MoveKindNames.Transfer,
        ImportMoveKind.Issue => MoveKindNames.Issue,
        ImportMoveKind.Inventory => MoveKindNames.Inventory,
        ImportMoveKind.Unknown => MoveKindNames.Unknown,
        _ => MoveKindNames.Unknown
    };

    public static string ReviewTitle(ImportDocGroup group, bool fromFile)
    {
        var num = DocTypeNames.FormatNumber(group.Number);
        if (LooksLikeAppNumber(group.Number))
        {
            var move = MoveKindLabel(group.Kind);
            return move == MoveKindNames.Unknown
                ? $"АПП № {num}"
                : $"АПП ({move.ToLowerInvariant()}) № {num}";
        }

        return group.Type switch
        {
            DocType.Return =>
                $"Накладна (повернення) № {num}",
            DocType.Transfer =>
                $"Накладна (переміщення) № {num}",
            DocType.Receipt =>
                $"Накладна (вимога) № {num}",
            DocType.Inventory when string.IsNullOrWhiteSpace(group.Number) && fromFile =>
                "Рух майна за період",
            DocType.Inventory when string.IsNullOrWhiteSpace(group.Number) =>
                "Інвентаризація",
            DocType.Inventory =>
                $"Інвентаризація № {num}",
            DocType.Issue =>
                $"Видача майна № {num}",
            DocType.WriteOff =>
                $"Акт списання № {num}",
            DocType.Loss =>
                $"Документи втрати майна № {num}",
            DocType.QualityState =>
                $"Акт якісного стану № {num}",
            DocType.QualityChange =>
                $"Акт зміни якісного стану № {num}",
            DocType.WorkAct =>
                $"Акт виконаних робіт № {num}",
            DocType.ExplosiveWorks =>
                $"Акт списання вибухових матеріалів № {num}",
            _ => fromFile ? "Рух майна за період" : "Додавання майна"
        };
    }

    public static bool LooksLikeAppNumber(string? number)
    {
        var t = (number ?? "").Trim();
        if (t.Length == 0) return false;
        // ExcelImporter.LooksLikeAppText — internal; дублюємо легку перевірку номера.
        var n = t.ToLowerInvariant();
        return n.Contains("апп") || (n.Contains("прийман") && n.Contains("передач"));
    }

    public static string ReviewSourceNote(bool fromFile) =>
        fromFile ? "Імпорт з файлу" : "Додано зі списку";
}

/// <summary>Питання перед збереженням: що саме користувач проводить цим файлом.</summary>
internal static class ImportPosting
{
    public const string Accept = "accept";
    public const string Surrender = "surrender";
    public const string Transfer = "transfer";
    public const string Register = "register";
    public const string Inventory = "inventory";

    public static readonly (string Id, string Title)[] Choices =
    {
        (Accept, "Документ приймання"),
        (Surrender, "Здавання (повернення на склад служби)"),
        (Transfer, "Документ переміщення"),
        (Register, "Перелік майна — реєстр руху"),
        (Inventory, "Перелік майна — інвентаризація")
    };

    public static string Guess(IEnumerable<ImportRow> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0) return Inventory;
        var op = list.Select(r => r.Operation).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";
        var n = op.ToLowerInvariant();
        if (n.Contains("здаван")) return Surrender;
        if (n.Contains("прийман")) return Accept;
        // Накладна (вимога): «видача» служби підрозділу = приймання на наш склад.
        if (n.Contains("видат") || n.Contains("видача") || n.Contains("видач")) return Accept;
        if (n.Contains("отрим") || n.Contains("надход")) return Accept;

        // АПП у файлі — завжди реєстр руху, не інвентаризація.
        if (list.Any(r => ImportGrouping.LooksLikeAppNumber(r.Number)))
            return Register;

        var kinds = list.Select(r => r.Kind).Distinct().ToList();
        if (kinds.Count == 1 && kinds[0] == ImportMoveKind.Transfer) return Transfer;
        if (kinds.Count == 1 && kinds[0] == ImportMoveKind.Inventory) return Inventory;
        // Невизначені рядки — лишаємо «реєстр», щоб користувач явно обрав намір або тип у рядку.
        return Register;
    }

    public static void Apply(ImportRow row, string intent, ImportMoveKind originalKind, string originalOperation)
    {
        switch (intent)
        {
            case Accept:
                row.Kind = ImportMoveKind.Receipt;
                row.Operation = string.IsNullOrWhiteSpace(originalOperation) ? "приймання" : originalOperation;
                break;
            case Surrender:
                row.Kind = ImportMoveKind.Return;
                row.Operation = string.IsNullOrWhiteSpace(originalOperation) ? "здавання" : originalOperation;
                break;
            case Transfer:
                row.Kind = ImportMoveKind.Transfer;
                row.Operation = "переміщення";
                break;
            case Inventory:
                row.Kind = ImportMoveKind.Inventory;
                row.Operation = "";
                break;
            default:
                row.Kind = originalKind;
                row.Operation = originalOperation ?? "";
                break;
        }
    }

    public static string Label(string intent, ImportMoveKind originalKind, string originalOperation, string originalLabel)
    {
        return intent switch
        {
            Accept => MoveKindNames.Receipt,
            Surrender => MoveKindNames.Return,
            Transfer => MoveKindNames.Transfer,
            Inventory => MoveKindNames.Inventory,
            _ when originalKind == ImportMoveKind.Unknown => MoveKindNames.Unknown,
            _ => MoveKindNames.Sanitize(
                string.IsNullOrWhiteSpace(originalLabel)
                    ? ImportGrouping.MoveKindLabel(originalKind)
                    : originalLabel)
        };
    }
}
