using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace Obozny;

public enum ImportMoveKind
{
    Inventory,
    Receipt,
    Return,
    Issue,
    WriteOff,
    Loss,
    Transfer,
    /// <summary>Тип руху не вдалося визначити — потрібен ручний вибір.</summary>
    Unknown
}

public sealed class ImportRow
{
    public DateTime Date { get; set; } = DateTime.Today;
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "шт";
    public string Category { get; set; } = "";
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public ImportMoveKind Kind { get; set; } = ImportMoveKind.Unknown;
    public string StrongpointName { get; set; } = "";
    public string StrongpointRegNumber { get; set; } = "";
    public string Note { get; set; } = "";
    /// <summary>Текст поля «Вид операції» з накладної (вимоги).</summary>
    public string Operation { get; set; } = "";
    /// <summary>Сирий текст поля «Передає» з шапки документа (якщо є).</summary>
    public string Peredaye { get; set; } = "";
    /// <summary>
    /// Служба забезпечення (ІС ССП / СЗУ / СОВТ та МСВ) — з автопідстановки Передає
    /// або ручного вибору в діалозі імпорту; порожньо = без вибору.
    /// </summary>
    public string SupplyService { get; set; } = "";
}

internal static class ExcelImporter
{
    public static List<ImportRow> Read(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".xls")
            return ReadXls(path);
        if (ext is ".xlsx" or ".xlsm")
            return ReadXlsx(path);
        return ReadCsv(path);
    }

    /// <summary>Excel 97–2003. ClosedXML читає лише xlsx, тож стару книгу спочатку перекладаємо у значення.</summary>
    private static List<ImportRow> ReadXls(string path)
    {
        var kind = PeekKind(path);
        if (kind == BookKind.Xlsx)
            return ReadXlsx(path);
        if (kind == BookKind.Xml)
            throw new InvalidDataException("Файл збережений старим XML Excel. Відкрийте його в Excel і збережіть як книгу .xlsx.");

        try
        {
            using var xlsx = ConvertXlsToXlsx(path);
            using var wb = new XLWorkbook(xlsx);
            return ReadWorkbook(wb, path);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                "Не вдалося прочитати старий файл Excel (.xls). Якщо він захищений паролем, зніміть пароль і збережіть файл ще раз.\n" + ex.Message,
                ex);
        }
    }

    private enum BookKind { Ole, Xlsx, Xml, Other }

    private static BookKind PeekKind(string path)
    {
        Span<byte> head = stackalloc byte[8];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var n = fs.Read(head);
        if (n >= 4 && head[0] == 0x50 && head[1] == 0x4B)
            return BookKind.Xlsx;
        if (n >= 8 && head[0] == 0xD0 && head[1] == 0xCF && head[2] == 0x11 && head[3] == 0xE0)
            return BookKind.Ole;
        if (n >= 1 && (head[0] == (byte)'<' || head[0] == 0xEF))
            return BookKind.Xml;
        return BookKind.Other;
    }

    private static MemoryStream ConvertXlsToXlsx(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var src = new HSSFWorkbook(fs);
        try { HSSFFormulaEvaluator.EvaluateAllFormulaCells(src); }
        catch { /* лишаємо збережені значення формул */ }

        using var dest = new XSSFWorkbook();
        var dateStyle = dest.CreateCellStyle();
        dateStyle.DataFormat = dest.CreateDataFormat().GetFormat("dd.mm.yyyy");

        for (var i = 0; i < src.NumberOfSheets; i++)
        {
            var sheet = src.GetSheetAt(i);
            var name = string.IsNullOrWhiteSpace(sheet.SheetName) ? "Аркуш" : sheet.SheetName;
            var outSheet = dest.CreateSheet(name);
            if (sheet.FirstRowNum < 0) continue;

            for (var r = sheet.FirstRowNum; r <= sheet.LastRowNum; r++)
            {
                var row = sheet.GetRow(r);
                if (row == null || row.FirstCellNum < 0) continue;
                var outRow = outSheet.CreateRow(r);
                for (var c = row.FirstCellNum; c < row.LastCellNum; c++)
                {
                    var cell = row.GetCell(c);
                    if (cell == null) continue;
                    CopyCell(cell, outRow.CreateCell(c), dateStyle);
                }
            }
        }

        var buffer = new MemoryStream();
        dest.Write(buffer, true);
        buffer.Position = 0;
        return buffer;
    }

    private static void CopyCell(ICell src, ICell dest, ICellStyle dateStyle)
    {
        var type = src.CellType == CellType.Formula ? src.CachedFormulaResultType : src.CellType;
        switch (type)
        {
            case CellType.Numeric:
                if (DateUtil.IsCellDateFormatted(src) && src.DateCellValue is DateTime date)
                {
                    dest.SetCellValue(date);
                    dest.CellStyle = dateStyle;
                }
                else
                {
                    dest.SetCellValue(src.NumericCellValue);
                }
                break;
            case CellType.String:
                dest.SetCellValue(src.StringCellValue);
                break;
            case CellType.Boolean:
                dest.SetCellValue(src.BooleanCellValue);
                break;
        }
    }

    private static List<ImportRow> ReadXlsx(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var wb = new XLWorkbook(fs);
        return ReadWorkbook(wb, path);
    }

    private static List<ImportRow> ReadWorkbook(XLWorkbook wb, string path)
    {
        var sheets = wb.Worksheets
            .OrderBy(ws =>
            {
                var n = ws.Name.ToLowerInvariant();
                if (n.Contains("наклад")) return 0;
                if (n.Contains("вимог")) return 1;
                return 2;
            })
            .ToList();

        foreach (var ws in sheets)
        {
            if (!LooksLikeWaybill(ws)) continue;
            var rows = ReadWaybill(ws, path);
            if (rows.Count > 0) return rows;
        }

        foreach (var ws in sheets)
        {
            var rows = ReadJournalSheet(ws);
            if (rows.Count > 0) return rows;
        }

        return new List<ImportRow>();
    }

    private static bool LooksLikeWaybill(IXLWorksheet ws)
    {
        var used = ws.RangeUsed();
        if (used == null) return false;
        var last = Math.Min(used.LastRow().RowNumber(), 50);
        var lastCol = Math.Min(used.LastColumn().ColumnNumber(), 25);
        for (var r = used.FirstRow().RowNumber(); r <= last; r++)
        {
            for (var c = 1; c <= lastCol; c++)
            {
                var n = Norm(CellText(ws.Cell(r, c)));
                if (n.Contains("накладна") && n.Contains("вимог")) return true;
                if (n.Contains("назва") && n.Contains("майна")) return true;
                if (n.Contains("відпущено") && n.Contains("прийнято")) return true;
            }
        }
        return false;
    }

    private static List<ImportRow> ReadWaybill(IXLWorksheet ws, string path)
    {
        var used = ws.RangeUsed();
        if (used == null) return new List<ImportRow>();

        var first = used.FirstRow().RowNumber();
        var last = used.LastRow().RowNumber();
        var lastCol = used.LastColumn().ColumnNumber();

        var headerRow = FindWaybillHeaderRow(ws, first, last, lastCol);
        if (headerRow == 0) return new List<ImportRow>();

        var map = MapWaybillColumns(ws, headerRow, lastCol);
        if (!map.ContainsKey("name")) return new List<ImportRow>();

        var date = ReadWaybillDate(ws, first, Math.Min(headerRow - 1, first + 25), lastCol, path);
        var number = ReadWaybillNumber(ws, first, Math.Min(headerRow - 1, first + 25), lastCol, path);

        var rows = new List<ImportRow>();
        for (var r = headerRow + 1; r <= last; r++)
        {
            if (IsIndexOrCaptionRow(ws, r, lastCol)) continue;

            var name = Compact(CellText(OptionalCell(ws, r, Col(map, "name"))));
            if (IsFooterName(name)) break;
            if (IsJunkName(name)) continue;

            var unitRaw = CellText(OptionalCell(ws, r, Col(map, "unit")));
            if (IsErrorText(unitRaw)) continue;

            var qtyCol = Col(map, "qtyOut") != 0 ? Col(map, "qtyOut") : Col(map, "qty");
            var qtyAlt = Col(map, "qty");
            var qty = CellDec(OptionalCell(ws, r, qtyCol));
            if (qty <= 0 && qtyAlt != 0 && qtyAlt != qtyCol)
                qty = CellDec(OptionalCell(ws, r, qtyAlt));
            if (qty <= 0) continue;

            rows.Add(new ImportRow
            {
                Date = date,
                Number = number,
                Name = name,
                Unit = string.IsNullOrWhiteSpace(unitRaw) || IsErrorText(unitRaw) ? "шт" : Compact(unitRaw),
                Category = Compact(CellText(OptionalCell(ws, r, Col(map, "category")))),
                Price = CellDec(OptionalCell(ws, r, Col(map, "price"))),
                Quantity = qty,
                Kind = ImportMoveKind.Unknown,
                StrongpointName = Compact(CellText(OptionalCell(ws, r, Col(map, "strongpoint")))),
                StrongpointRegNumber = Compact(CellText(OptionalCell(ws, r, Col(map, "reg"))))
            });
        }

        var operation = ReadOperation(ws, first, Math.Min(Math.Max(headerRow, first) + 5, first + 45), lastCol);
        var fromOperation = KindFromOperation(operation);
        if (fromOperation != null)
        {
            foreach (var row in rows)
            {
                row.Kind = fromOperation.Value;
                row.Operation = operation;
            }
        }
        else
        {
            // Накладна без «Виду операції» — не підставляти навмання (ні отримання, ні інвентаризацію).
            ApplySpecificTitleKind(ws, headerRow, lastCol, rows);
            foreach (var row in rows)
            {
                if (row.Kind is ImportMoveKind.Inventory or ImportMoveKind.Receipt or ImportMoveKind.Unknown)
                    row.Kind = ImportMoveKind.Unknown;
            }
        }

        var peredaye = ReadPeredaye(ws, first, Math.Min(headerRow - 1, first + 45), lastCol);
        var supply = SupplyServiceHelper.MapFromPeredaye(peredaye) ?? "";
        foreach (var row in rows)
        {
            row.Peredaye = peredaye;
            row.SupplyService = supply;
        }

        return rows;
    }

    /// <summary>
    /// Поле «Передає» у шапці накладної / руху: у тій самій клітинці після двокрапки,
    /// або в сусідній праворуч (як у зразках).
    /// </summary>
    internal static string ReadPeredaye(IXLWorksheet ws, int first, int last, int lastCol)
    {
        for (var r = first; r <= last; r++)
        {
            for (var c = 1; c <= lastCol; c++)
            {
                var text = CellText(ws.Cell(r, c));
                var n = Norm(text);
                // Ярлик «передає» / «передає:», не підписи «передав» у підвалі.
                if (!n.Contains("передає") && !n.StartsWith("передае")) continue;
                if (n.Contains("прийняв") && !n.Contains("передає")) continue;

                var inline = PeredayeAfterLabel(text);
                if (inline.Length > 0) return inline;

                for (var k = c + 1; k <= Math.Min(c + 6, lastCol); k++)
                {
                    var next = Compact(CellText(ws.Cell(r, k)));
                    if (IsPlausiblePeredayeValue(next)) return next;
                }
            }
        }
        return "";
    }

    /// <summary>Текст після «Передає:» у тій самій клітинці.</summary>
    internal static string PeredayeAfterLabel(string text)
    {
        var match = Regex.Match(text, @"передає\s*[:\-]\s*(\S.*)$", RegexOptions.IgnoreCase);
        if (!match.Success) return "";
        return Compact(match.Groups[1].Value);
    }

    private static bool IsPlausiblePeredayeValue(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var n = Norm(raw);
        if (n.Contains("передає") || n.Contains("прийняв") || n.Contains("приймає")) return false;
        if (n.Contains("підстава") || n.Contains("операц")) return false;
        if (n.Contains("сторін") || n.Contains("page")) return false;
        return true;
    }

    /// <summary>Поле «Вид операції» в шапці накладної (вимоги): у тій самій клітинці, праворуч або нижче.</summary>
    private static string ReadOperation(IXLWorksheet ws, int first, int last, int lastCol)
    {
        for (var r = first; r <= last; r++)
        {
            for (var c = 1; c <= lastCol; c++)
            {
                var text = CellText(ws.Cell(r, c));
                var n = Norm(text);
                if (!n.Contains("вид") || !n.Contains("операц")) continue;

                var inline = OperationAfterLabel(text);
                if (inline.Length > 0) return inline;

                // Праворуч / нижче: пропускаємо шум («Сторінка 1») і шукаємо відоме дієслово.
                string? fallback = null;
                for (var k = c + 1; k <= Math.Min(c + 8, lastCol); k++)
                {
                    var next = Compact(CellText(ws.Cell(r, k)));
                    if (!IsPlausibleOperationValue(next)) continue;
                    if (KindFromOperation(next) != null) return next;
                    fallback ??= next;
                }

                for (var dr = 1; dr <= 2; dr++)
                {
                    var below = Compact(CellText(ws.Cell(r + dr, c)));
                    if (!IsPlausibleOperationValue(below)) continue;
                    if (KindFromOperation(below) != null) return below;
                    fallback ??= below;
                }

                if (fallback != null) return fallback;
            }
        }
        return "";
    }

    /// <summary>
    /// Текст після «Вид операції:». Не відкидати «видача» через підрядок «вид».
    /// </summary>
    internal static string OperationAfterLabel(string text)
    {
        var match = Regex.Match(text, @"операц\p{L}*\s*[:\-]\s*(\S.*)$", RegexOptions.IgnoreCase);
        if (!match.Success)
            match = Regex.Match(text, @"операц\p{L}*\s+(\S.*)$", RegexOptions.IgnoreCase);
        if (!match.Success) return "";
        var rest = Compact(match.Groups[1].Value);
        if (rest.Length == 0) return "";
        // Повтор ярлика («вид операції»), а не значення («видача» / «здавання»).
        var n = Norm(rest);
        if (n.Contains("операц")) return "";
        if (n is "вид" || n.StartsWith("вид ")) return "";
        return rest;
    }

    /// <summary>Відсікти водяний знак / порожні підписи біля поля «Вид операції».</summary>
    private static bool IsPlausibleOperationValue(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var n = Norm(raw);
        if (n.Contains("операц")) return false;
        if (n.Contains("сторін") || n.Contains("page")) return false;
        if (n is "вид" || n.StartsWith("вид ")) return false;
        return true;
    }

    /// <summary>
    /// Здавання зменшує залишок підрозділу (повернення на склад служби).
    /// «Видача» на бланку накладної (вимоги) — видача від служби на підрозділ → для нас отримання (+).
    /// Порожнє значення лишає тип невизначеним (не вгадувати з назви файла).
    /// </summary>
    /// <summary>Розпізнати вид руху з тексту «Вид операції» (тести / імпорт).</summary>
    internal static ImportMoveKind? KindFromOperation(string raw)
    {
        var n = Norm(raw);
        if (n.Length == 0) return null;
        if (n.Contains("здаван")) return ImportMoveKind.Return;
        if (n.Contains("прийман")) return ImportMoveKind.Receipt;
        if (n.Contains("переміщ") || n.Contains("перемищ")) return ImportMoveKind.Transfer;
        if (n.Contains("втрат")) return ImportMoveKind.Loss;
        if (n.Contains("списан") || n.Contains("списат") || n.Contains("якіс") || n.Contains("виконаних робіт"))
            return ImportMoveKind.WriteOff;
        // Видача служби підрозділу = надходження на наш склад, не списання.
        if (n.Contains("видат") || n.Contains("видача") || n.Contains("видач")) return ImportMoveKind.Receipt;
        if (n.Contains("поверн")) return ImportMoveKind.Return;
        if (n.Contains("отрим") || n.Contains("надход")) return ImportMoveKind.Receipt;
        if (n.Contains("оприбут") || n.Contains("інвентар")) return ImportMoveKind.Inventory;
        return null;
    }

    private static int FindWaybillHeaderRow(IXLWorksheet ws, int first, int last, int lastCol)
    {
        var scanTo = Math.Min(last, first + 40);
        for (var r = first; r <= scanTo; r++)
        {
            for (var c = 1; c <= lastCol; c++)
            {
                var n = Norm(CellText(ws.Cell(r, c)));
                if (n.Contains("назва") && (n.Contains("майна") || n.Contains("військ") || n.Contains("наймен")))
                    return r;
            }
        }

        for (var r = first; r <= scanTo; r++)
        {
            if (!RowHasNameHeader(ws, r, lastCol)) continue;
            var map = MapWaybillColumns(ws, r, lastCol);
            if (map.ContainsKey("name") && (map.ContainsKey("qty") || map.ContainsKey("qtyOut")))
                return r;
        }
        return 0;
    }

    private static bool RowHasNameHeader(IXLWorksheet ws, int row, int lastCol)
    {
        for (var c = 1; c <= lastCol; c++)
        {
            var n = Norm(CellText(ws.Cell(row, c)));
            if (n.Contains("наймен") || n.Contains("назва") || n == "майно")
                return true;
        }
        return false;
    }

    private static Dictionary<string, int> MapWaybillColumns(IXLWorksheet ws, int headerRow, int lastCol)
    {
        var combined = new List<(int col, string text)>();
        for (var c = 1; c <= lastCol; c++)
        {
            var parts = new[]
            {
                CellText(ws.Cell(headerRow, c)),
                CellText(ws.Cell(headerRow + 1, c)),
                CellText(ws.Cell(headerRow + 2, c))
            };
            combined.Add((c, string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)))));
        }

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (col, text) in combined)
        {
            var n = Norm(text);
            if (IsRegHeader(n)) TryAdd(map, "reg", col);
            else if (IsStrongpointName(n)) TryAdd(map, "strongpoint", col);
            else if (n.Contains("назва") || n.Contains("наймен")) TryAdd(map, "name", col);
            else if (n.Contains("вартіст") || n.Contains("ціна") || n.Contains("цена")) TryAdd(map, "price", col);
            else if ((n.Contains("одиниц") && n.Contains("вим")) || n.Contains("одиниця виміру") || n is "ов" or "од" or "од.") TryAdd(map, "unit", col);
            else if (n.Contains("катег")) TryAdd(map, "category", col);
            else if (n.Contains("відпущ") || (n.Contains("прийнято") && !n.Contains("прийняти"))) TryAdd(map, "qtyOut", col);
            else if (IsQtyHeader(n) || n.Contains("видати") || n.Contains("прийняти")) TryAdd(map, "qty", col);
        }
        return map;
    }

    private static DateTime ReadWaybillDate(IXLWorksheet ws, int first, int last, int lastCol, string path)
    {
        DateTime? found = null;
        for (var r = first; r <= last; r++)
        {
            var rowText = "";
            for (var c = 1; c <= lastCol; c++)
                rowText += " " + Norm(CellText(ws.Cell(r, c)));
            if (rowText.Contains("дійсн")) continue;

            for (var c = 1; c <= lastCol; c++)
            {
                if (!TryGetDate(ws.Cell(r, c), out var date)) continue;
                found = date;
            }
        }

        if (found != null) return found.Value;

        var m = Regex.Match(Path.GetFileName(path), @"(\d{4})[.\-](\d{2})[.\-](\d{2})");
        if (m.Success
            && int.TryParse(m.Groups[1].Value, out var y)
            && int.TryParse(m.Groups[2].Value, out var mo)
            && int.TryParse(m.Groups[3].Value, out var d)
            && y is >= 2000 and <= 2100 && mo is >= 1 and <= 12 && d is >= 1 and <= 31)
        {
            try { return new DateTime(y, mo, d); }
            catch { /* ignore invalid calendar date */ }
        }

        return DateTime.Today;
    }

    private static string ReadWaybillNumber(IXLWorksheet ws, int first, int last, int lastCol, string path)
    {
        for (var r = first; r <= last; r++)
        {
            for (var c = 1; c <= lastCol; c++)
            {
                var n = Norm(CellText(ws.Cell(r, c)));
                if (!(n.Contains("накладна") && (n.Contains("вимог") || n.Contains("№")))) continue;
                for (var k = c + 1; k <= Math.Min(c + 4, lastCol); k++)
                {
                    var num = Compact(CellText(ws.Cell(r, k)));
                    if (!string.IsNullOrWhiteSpace(num) && !Norm(num).Contains("накладна"))
                        return num;
                }
            }
        }

        var m = Regex.Match(Path.GetFileNameWithoutExtension(path), @"(\d{3,6})[-_/](\d{3,6})");
        return m.Success ? $"{m.Groups[1].Value}/{m.Groups[2].Value}" : "";
    }

    private static bool TryGetDate(IXLCell cell, out DateTime date)
    {
        date = default;
        if (cell == null) return false;
        try
        {
            if (cell.IsEmpty()) return false;
            if (cell.DataType == XLDataType.DateTime)
            {
                date = cell.GetDateTime().Date;
                return true;
            }
            if (cell.DataType == XLDataType.Number)
            {
                var n = cell.GetDouble();
                if (n > 20000 && n < 80000)
                {
                    date = DateTime.FromOADate(n).Date;
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        var t = CellText(cell);
        if (!Regex.IsMatch(t, @"^\d{1,2}[./]\d{1,2}[./]\d{2,4}$")) return false;
        if (DateTime.TryParse(t, new CultureInfo("uk-UA"), DateTimeStyles.None, out var parsed)
            || DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
        {
            date = parsed.Date;
            return true;
        }
        return false;
    }

    private static bool IsIndexOrCaptionRow(IXLWorksheet ws, int r, int lastCol)
    {
        var texts = new List<string>();
        for (var c = 1; c <= lastCol; c++)
        {
            var t = CellText(ws.Cell(r, c));
            if (!string.IsNullOrWhiteSpace(t)) texts.Add(t);
        }
        if (texts.Count == 0) return true;

        var digits = texts.Count(t => int.TryParse(t, out var n) && n is >= 1 and <= 40);
        if (digits >= 5 && digits >= texts.Count - 1) return true;

        var joined = Norm(string.Join(" ", texts));
        if (joined.Contains("видати") && joined.Contains("відпущ")) return true;
        if (joined.Contains("назва") && joined.Contains("майна")) return true;
        return false;
    }

    private static bool IsJunkName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        var n = Norm(name);
        if (n is "0" or "-" or "x" or "х") return true;
        return IsErrorText(name);
    }

    private static bool IsFooterName(string name)
    {
        var n = Norm(name);
        return n.Contains("всього") || n.Contains("разом") || n.Contains("підпис")
            || n.Contains("переда") || n.Contains("прийняв") || n.Contains("комісі")
            || n.Contains("начальник");
    }

    private static bool IsErrorText(string raw)
    {
        var n = Norm(raw);
        return n is "#n/a" or "#н/д" or "#value!" or "#ref!" or "#div/0!" or "n/a";
    }

    private static List<ImportRow> ReadJournalSheet(IXLWorksheet ws)
    {
        var used = ws.RangeUsed();
        if (used == null) return new List<ImportRow>();

        var first = used.FirstRow().RowNumber();
        var last = used.LastRow().RowNumber();
        var lastCol = used.LastColumn().ColumnNumber();
        var headerRow = FindJournalHeaderRow(ws, first, last, lastCol);
        if (headerRow == 0) return new List<ImportRow>();

        var map = MapHeaders(Enumerable.Range(1, lastCol)
            .Select(c => (c, CellText(ws.Cell(headerRow, c))))
            .Where(x => !string.IsNullOrWhiteSpace(x.Item2)));
        var fallback = TitleKind(ws, headerRow, lastCol);

        var rows = new List<ImportRow>();
        for (var r = headerRow + 1; r <= last; r++)
        {
            var name = CellText(OptionalCell(ws, r, Col(map, "name")));
            if (string.IsNullOrWhiteSpace(name) || IsJunkName(name) || IsFooterName(name) || IsHeaderWord(name)) continue;
            var qtyCol = Col(map, "qtyOut") != 0 ? Col(map, "qtyOut") : Col(map, "qty");
            var qty = CellDec(OptionalCell(ws, r, qtyCol));
            if (qty <= 0) continue;
            var unit = CellText(OptionalCell(ws, r, Col(map, "unit")));
            var move = CellText(OptionalCell(ws, r, Col(map, "move")));
            var row = new ImportRow
            {
                Date = CellDate(OptionalCell(ws, r, Col(map, "date"))),
                Number = CellText(OptionalCell(ws, r, Col(map, "number"))),
                Name = Compact(name),
                Unit = string.IsNullOrWhiteSpace(unit) ? "шт" : unit,
                Category = CellText(OptionalCell(ws, r, Col(map, "category"))),
                Price = CellDec(OptionalCell(ws, r, Col(map, "price"))),
                Quantity = qty,
                Kind = string.IsNullOrWhiteSpace(move) ? fallback : ParseKind(move),
                Operation = Compact(move),
                StrongpointName = Mapped(ws, r, map, "strongpoint"),
                StrongpointRegNumber = Mapped(ws, r, map, "reg"),
                Note = Mapped(ws, r, map, "note")
            };
            GuardAppDocumentKind(row);
            rows.Add(row);
        }
        return rows;
    }

    private static int FindJournalHeaderRow(IXLWorksheet ws, int first, int last, int lastCol)
    {
        var scanTo = Math.Min(last, first + 40);
        for (var r = first; r <= scanTo; r++)
        {
            var map = MapHeaders(Enumerable.Range(1, lastCol)
                .Select(c => (c, CellText(ws.Cell(r, c))))
                .Where(x => !string.IsNullOrWhiteSpace(x.Item2)));
            if (map.ContainsKey("name") && (map.ContainsKey("qty") || map.ContainsKey("qtyOut")))
                return r;
        }
        return 0;
    }

    private static List<ImportRow> ReadCsv(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0) return new List<ImportRow>();
        var sample = lines.Take(40).OrderByDescending(l => Math.Max(l.Count(c => c == ';'), l.Count(c => c == ','))).First();
        var sep = sample.Count(c => c == ';') >= sample.Count(c => c == ',') ? ';' : ',';

        var headerIndex = -1;
        Dictionary<string, int>? map = null;
        var scan = Math.Min(lines.Length, 40);
        for (var i = 0; i < scan; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var candidate = MapHeaders(Split(lines[i], sep).Select((h, col) => (col + 1, h)));
            if (!candidate.ContainsKey("name") || (!candidate.ContainsKey("qty") && !candidate.ContainsKey("qtyOut")))
                continue;
            headerIndex = i;
            map = candidate;
            break;
        }
        if (map == null || headerIndex < 0) return new List<ImportRow>();
        var columns = map;

        var title = Path.GetFileNameWithoutExtension(path) + " " + string.Join(" ", lines.Take(headerIndex));
        var fallback = TitleKindFromText(title);
        var rows = new List<ImportRow>();
        foreach (var line in lines.Skip(headerIndex + 1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var p = Split(line, sep);
            string Get(string key)
            {
                var i = Col(columns, key) - 1;
                return i >= 0 && i < p.Length ? p[i].Trim().Trim('"') : "";
            }
            var name = Get("name");
            if (string.IsNullOrWhiteSpace(name) || IsJunkName(name) || IsFooterName(name) || IsHeaderWord(name)) continue;
            var qty = ParseDec(Get("qtyOut").Length > 0 ? Get("qtyOut") : Get("qty"));
            if (qty <= 0) continue;
            var move = Get("move");
            var row = new ImportRow
            {
                Date = ParseDate(Get("date")),
                Number = Get("number"),
                Name = Compact(name),
                Unit = string.IsNullOrWhiteSpace(Get("unit")) ? "шт" : Get("unit"),
                Category = Get("category"),
                Price = ParseDec(Get("price")),
                Quantity = qty,
                Kind = string.IsNullOrWhiteSpace(move) ? fallback : ParseKind(move),
                Operation = Compact(move),
                StrongpointName = Get("strongpoint"),
                StrongpointRegNumber = Get("reg"),
                Note = Get("note")
            };
            GuardAppDocumentKind(row);
            rows.Add(row);
        }
        return rows;
    }

    private static Dictionary<string, int> MapHeaders(IEnumerable<(int col, string text)> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (col, text) in headers)
        {
            var n = Norm(text);
            if (IsRegHeader(n))
                TryAdd(map, "reg", col);
            else if (IsStrongpointName(n))
                TryAdd(map, "strongpoint", col);
            else if (n.Contains("приміт"))
                TryAdd(map, "note", col);
            else if (n.Contains("наймен") || n.Contains("назва") || n == "майно") TryAdd(map, "name", col);
            else if (n.Contains("вартіст") || n.Contains("ціна") || n.Contains("цена")) TryAdd(map, "price", col);
            else if ((n.Contains("одиниц") && n.Contains("вим")) || n.Contains("вим") || n is "ов" or "од" or "од.") TryAdd(map, "unit", col);
            else if (n.Contains("одиниц") && !n.Contains("варті")) TryAdd(map, "unit", col);
            else if (n.Contains("катег")) TryAdd(map, "category", col);
            else if (n.Contains("відпущ")) TryAdd(map, "qtyOut", col);
            else if (IsQtyHeader(n)) TryAdd(map, "qty", col);
            else if (n.Contains("рух") || (n.Contains("операц") && !n.Contains("дата"))) TryAdd(map, "move", col);
            else if (n.Contains("номер") || n.Contains("№") || (n.Contains("документ") && !n.Contains("дата"))) TryAdd(map, "number", col);
            else if (n.Contains("дата")) TryAdd(map, "date", col);
        }
        return map;
    }

    private static void TryAdd(Dictionary<string, int> map, string key, int col)
    {
        if (!map.ContainsKey(key)) map[key] = col;
    }

    private static int Col(Dictionary<string, int> map, string key) =>
        map.TryGetValue(key, out var c) ? c : 0;

    private static string Mapped(IXLWorksheet ws, int row, Dictionary<string, int> map, string key)
    {
        var col = Col(map, key);
        return col == 0 ? "" : Compact(CellText(ws.Cell(row, col)));
    }

    /// <summary>«Реєстраційний номер опорного пункту» і «№ опорного пункту» — одне поле.</summary>
    private static bool IsRegHeader(string n) =>
        n.Contains("реєстр") || n.Contains("реєстрац")
        || ((n.Contains("№") || n.Contains("номер")) && (n.Contains("опорн") || n.Contains(" оп") || n.EndsWith("оп")));

    private static bool IsStrongpointName(string n) =>
        !IsRegHeader(n) && (
            (n.Contains("назва") && (n.Contains(" оп") || n.EndsWith("оп") || n.Contains("опорн")))
            || n.Contains("опорн"));

    private static bool IsQtyHeader(string n) =>
        n.Contains("кільк") || n.Contains("кво") || n == "qty"
        || n.Contains("к-ст") || n.Contains("к-ть") || n.Contains("кіл-");

    private static bool IsHeaderWord(string name)
    {
        var n = Norm(name);
        return n is "назва" or "найменування" or "майно" or "кількість" or "к-сть";
    }

    private static void ApplySpecificTitleKind(IXLWorksheet ws, int headerRow, int lastCol, List<ImportRow> rows)
    {
        var kind = TitleKind(ws, headerRow, lastCol);
        // Лише явні типи зі назви (списання, переміщення…). Не підставляти отримання/інвентаризацію.
        if (kind is ImportMoveKind.Inventory or ImportMoveKind.Receipt or ImportMoveKind.Unknown) return;
        foreach (var row in rows)
            row.Kind = kind;
    }

    private static ImportMoveKind TitleKind(IXLWorksheet ws, int headerRow, int lastCol)
    {
        var text = ws.Name;
        var from = Math.Max(1, headerRow - 12);
        for (var r = from; r < headerRow; r++)
        {
            for (var c = 1; c <= Math.Min(lastCol, 12); c++)
                text += " " + CellText(ws.Cell(r, c));
        }
        return TitleKindFromText(text);
    }

    private static ImportMoveKind TitleKindFromText(string text)
    {
        var n = Norm(text);
        // АПП / накладна без явного виду операції — не інвентаризація.
        if (LooksLikeAppText(n) || n.Contains("наклад") || n.Contains("вимог"))
            return ImportMoveKind.Unknown;

        var kind = ParseKind(text);
        if (kind != ImportMoveKind.Unknown) return kind;
        // Явна інвентаризація в заголовку — так.
        if (n.Contains("інвентар") || n.Contains("внесення залиш") || n.Contains("оприбутков"))
            return ImportMoveKind.Inventory;
        return ImportMoveKind.Unknown;
    }

    private static ImportMoveKind ParseKind(string raw)
    {
        var n = Norm(raw);
        if (n.Length == 0) return ImportMoveKind.Unknown;

        // «Акт приймання-передачі» / «АПП» — назва документа, не вид операції.
        // Без окремого дієслова руху (здавання, отримано…) тип лишається невизначеним.
        if (LooksLikeAppText(n) && !HasExplicitMoveVerb(n))
            return ImportMoveKind.Unknown;

        if (n.Contains("переміщ") || n.Contains("перемищ")) return ImportMoveKind.Transfer;
        if (n.Contains("втрат")) return ImportMoveKind.Loss;
        if (n.Contains("списан") || n.Contains("списат") || n.Contains("якіс") || n.Contains("виконаних робіт"))
            return ImportMoveKind.WriteOff;
        // «Видача» у журналі/операції накладної для підрозділу = отримання (+), не Issue (−).
        if (n.Contains("видат") || n.Contains("видача") || n.Contains("видач")) return ImportMoveKind.Receipt;
        if (n.Contains("поверн") || n.Contains("здаван")) return ImportMoveKind.Return;
        // «отримано/отримання» — так; голе «приймання» з АПП уже відсічено вище.
        if (n.Contains("отрим") || n.Contains("надход")) return ImportMoveKind.Receipt;
        if (n.Contains("прийман") && !n.Contains("передач")) return ImportMoveKind.Receipt;
        if (n.Contains("інвентар") || n.Contains("оприбут") || n.Contains("залиш")) return ImportMoveKind.Inventory;
        return ImportMoveKind.Unknown;
    }

    /// <summary>АПП — документ реєстру руху; ніколи не класифікувати як інвентаризацію.</summary>
    private static void GuardAppDocumentKind(ImportRow row)
    {
        if (!LooksLikeAppText(Norm(row.Number)) && !LooksLikeAppText(Norm(row.Note)))
            return;
        if (row.Kind == ImportMoveKind.Inventory)
            row.Kind = ImportMoveKind.Unknown;
    }

    internal static bool LooksLikeAppText(string? normalizedOrRaw)
    {
        var n = Norm(normalizedOrRaw ?? "");
        if (n.Length == 0) return false;
        // «АПП № 204/3», «апп-204», «акт приймання-передачі»
        if (Regex.IsMatch(n, @"(^|[^a-zа-яіїєґ])апп([^a-zа-яіїєґ]|$)")) return true;
        if (n.Contains("прийман") && n.Contains("передач")) return true;
        return false;
    }

    private static bool HasExplicitMoveVerb(string n) =>
        n.Contains("здаван") || n.Contains("поверн")
        || n.Contains("переміщ") || n.Contains("перемищ")
        || n.Contains("списан") || n.Contains("списат")
        || n.Contains("отримано") || n.Contains("отримання") || n.Contains("отрим ")
        || n.Contains("надход") || n.Contains("видат") || n.Contains("видача")
        || n.Contains("втрат");

    private static IXLCell? OptionalCell(IXLWorksheet ws, int row, int col) =>
        col <= 0 ? null : ws.Cell(row, col);

    private static string CellText(IXLCell? cell)
    {
        if (cell == null) return "";
        try
        {
            if (cell.IsEmpty()) return "";
            if (cell.DataType == XLDataType.Error) return "";
            return Compact(cell.GetFormattedString());
        }
        catch
        {
            return "";
        }
    }

    private static DateTime CellDate(IXLCell? cell)
    {
        if (cell == null || cell.IsEmpty()) return DateTime.Today;
        if (cell.DataType == XLDataType.DateTime) return cell.GetDateTime().Date;
        if (cell.DataType == XLDataType.Number)
        {
            var n = cell.GetDouble();
            if (n > 20000 && n < 80000) return DateTime.FromOADate(n).Date;
        }
        return ParseDate(CellText(cell));
    }

    private static decimal CellDec(IXLCell? cell)
    {
        if (cell == null || cell.IsEmpty()) return 0;
        try
        {
            if (cell.DataType == XLDataType.Error) return 0;
            if (cell.DataType == XLDataType.Number) return (decimal)cell.GetDouble();
        }
        catch
        {
            return 0;
        }
        return ParseDec(CellText(cell));
    }

    private static DateTime ParseDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return DateTime.Today;
        if (double.TryParse(raw.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var oa)
            && oa > 20000 && oa < 80000)
            return DateTime.FromOADate(oa).Date;
        if (DateTime.TryParse(raw, new CultureInfo("uk-UA"), DateTimeStyles.None, out var d)) return d.Date;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d.Date;
        return DateTime.Today;
    }

    private static decimal ParseDec(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        var m = Regex.Match(raw.Replace('\u00A0', ' ').Replace(" ", ""), @"-?\d+(?:[.,]\d+)?");
        if (!m.Success) return 0;
        decimal.TryParse(m.Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v);
        return v;
    }

    private static string Norm(string s)
    {
        var t = (s ?? "").Replace('\n', ' ').Replace('\r', ' ').ToLowerInvariant();
        while (t.Contains("  ")) t = t.Replace("  ", " ");
        return t.Trim();
    }

    private static string Compact(string s)
    {
        var t = (s ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
        while (t.Contains("  ")) t = t.Replace("  ", " ");
        return t;
    }

    private static string[] Split(string line, char sep) =>
        line.Split(sep).Select(x => x.Trim()).ToArray();
}
