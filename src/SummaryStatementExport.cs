using System.Text;
using System.Windows;
using ClosedXML.Excel;
using Microsoft.Win32;

namespace Obozny;

/// <summary>Збереження Узагальнюючої відомості (Додаток 1) у Excel за зразком форми.</summary>
internal static class SummaryStatementExport
{
    private const string BookTitle =
        "Книга обліку наявності та руху військового майна (склад, підрозділ)";

    public static void Save(Window owner, SummaryStatementReport report)
    {
        try
        {
            SaveCore(owner, report);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, FormatSaveError(ex), "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void SaveCore(Window owner, SummaryStatementReport report)
    {
        if (!BetaLicense.TryAllowDocumentImportExport(owner)) return;

        var suggested = SuggestFileName(report) + ".xlsx";
        var dlg = new SaveFileDialog
        {
            Title = "Зберегти узагальнюючу відомість у Excel",
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = suggested,
            AddExtension = true,
            DefaultExt = "xlsx",
            // Не покладатися на cwd (single-file / AV інколи ламає початкову теку).
            InitialDirectory = SafeDocumentsDir()
        };
        if (dlg.ShowDialog(owner) != true) return;

        var target = (dlg.FileName ?? "").Trim();
        if (target.Length == 0)
        {
            MessageBox.Show(owner, "Не вказано шлях до файлу.", "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dir = Path.GetDirectoryName(target);
        if (string.IsNullOrWhiteSpace(dir))
        {
            MessageBox.Show(owner, "Некоректний шлях до файлу.", "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!Directory.Exists(dir))
        {
            MessageBox.Show(owner,
                "Тека для збереження не існує або недоступна:\n" + dir,
                "Обозний", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Пишемо спочатку в %TEMP%, потім копіюємо — менше шансів, що AV підрубить запис «на льоту».
        var temp = Path.Combine(Path.GetTempPath(),
            "obozny-dodatok1-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Відомість");
                Write(ws, report);
                wb.SaveAs(temp);
            }

            File.Copy(temp, target, overwrite: true);
            MessageBox.Show(owner, "Файл збережено.", "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, FormatSaveError(ex), "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch { /* ignore */ }
        }
    }

    private static string FormatSaveError(Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Не вдалося зберегти узагальнюючу відомість.");
        if (ex is FileNotFoundException fnf)
        {
            sb.AppendLine("Не знайдено файл або компонент (часто антивірус видаляє dll під час роботи).");
            if (!string.IsNullOrWhiteSpace(fnf.FileName))
                sb.AppendLine("Файл: " + fnf.FileName);
        }
        else if (ex is UnauthorizedAccessException or IOException)
        {
            sb.AppendLine("Немає доступу до файлу (зайнятий іншою програмою або заблокований захистом).");
        }
        if (!string.IsNullOrWhiteSpace(ex.Message))
            sb.AppendLine(ex.Message);
        return sb.ToString();
    }

    private static string SafeDocumentsDir()
    {
        try
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(docs) && Directory.Exists(docs))
                return docs;
        }
        catch { /* ignore */ }
        try
        {
            var tmp = Path.GetTempPath();
            if (Directory.Exists(tmp)) return tmp;
        }
        catch { /* ignore */ }
        return Environment.CurrentDirectory;
    }

    internal static void Write(IXLWorksheet ws, SummaryStatementReport report)
    {
        ws.ShowGridLines = false;
        ws.Column(1).Width = 8;
        ws.Column(2).Width = 55;
        ws.Column(3).Width = 16;
        ws.Column(4).Width = 28;
        ws.Column(5).Width = 28;
        ws.Column(6).Width = 28;

        var numberPart = string.IsNullOrWhiteSpace(report.Number)
            ? "______"
            : report.Number.Trim();
        var title =
            $"Узагальнююча відомість № {numberPart} обліку військового майна в електронній формі";

        ws.Range(1, 1, 1, 6).Merge();
        var titleCell = ws.Cell(1, 1);
        titleCell.Value = title;
        titleCell.Style.Font.Bold = true;
        titleCell.Style.Font.FontSize = 14;
        titleCell.Style.Alignment.WrapText = true;
        ws.Row(1).Height = 28;

        ws.Range(2, 1, 2, 6).Merge();
        ws.Cell(2, 1).Value =
            "Додаток 1 до Інструкції з обліку військового майна у Збройних Силах України (пункт 7 розділу II)";
        ws.Cell(2, 1).Style.Font.FontSize = 10;
        ws.Cell(2, 1).Style.Alignment.WrapText = true;

        WriteMeta(ws, 4, "Найменування книги обліку військового майна", BookTitle);
        WriteMeta(ws, 5, "Реквізити книги обліку військового майна", BookRequisites(report));
        WriteMeta(ws, 6, "Служба забезпечення",
            string.IsNullOrWhiteSpace(report.SupplyService) ? "—" : report.SupplyService.Trim());
        WriteMeta(ws, 7, "Звітний період", FormatPeriod(report.PeriodFrom, report.PeriodTo));
        WriteMeta(ws, 8, "Дата складання узагальнюючої відомості",
            report.CompiledAt.ToString("dd.MM.yyyy"));

        const int headerRow = 10;
        string[] headers =
        [
            "№ з/п",
            "Найменування військового майна",
            "Одиниця виміру",
            "Кількість військового майна, що надійшло",
            "Кількість військового майна, що вибуло",
            "Кількість наявного військового майна"
        ];
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Alignment.WrapText = true;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEE8");
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }
        ws.Row(headerRow).Height = 48;

        var qtyFormat = Ua.DecimalFormat(Ua.QtyDecimals());
        var r = headerRow + 1;
        var n = 1;
        foreach (var row in report.Rows)
        {
            ws.Cell(r, 1).Value = n;
            ws.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(r, 2).Value = row.Name;
            ws.Cell(r, 3).Value = row.Unit;
            ws.Cell(r, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            WriteQty(ws.Cell(r, 4), row.InQty, qtyFormat);
            WriteQty(ws.Cell(r, 5), row.OutQty, qtyFormat);
            WriteQty(ws.Cell(r, 6), row.Balance, qtyFormat);
            for (var c = 1; c <= 6; c++)
                ws.Cell(r, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            r++;
            n++;
        }

        r += 1;
        ws.Range(r, 1, r, 6).Merge();
        ws.Cell(r, 1).Value =
            "______________________________________________________________________________________";
        r++;
        ws.Range(r, 1, r, 6).Merge();
        ws.Cell(r, 1).Value =
            "(посада, військове звання, підпис, власне ім’я, прізвище посадової (службової) особи, яка відповідає за стан обліку військового майна, або проводить звірку)";
        ws.Cell(r, 1).Style.Font.FontSize = 9;
        ws.Cell(r, 1).Style.Alignment.WrapText = true;
        ws.Row(r).Height = 32;

        r += 2;
        ws.Range(r, 1, r, 6).Merge();
        ws.Cell(r, 1).Value =
            "______________________________________________________________________________________";
        r++;
        ws.Range(r, 1, r, 6).Merge();
        ws.Cell(r, 1).Value =
            "(посада, військове звання, підпис, власне ім’я, прізвище посадової (службової) особи, яка безпосередньо веде його облік, або з якою проводиться звірка)";
        ws.Cell(r, 1).Style.Font.FontSize = 9;
        ws.Cell(r, 1).Style.Alignment.WrapText = true;
        ws.Row(r).Height = 32;

        r += 2;
        ws.Range(r, 1, r, 6).Merge();
        ws.Cell(r, 1).Value =
            "Наявність — останнє значення «Становить» на кінець звітного періоду. Надійшло і вибуло — сума руху за цей період. Ціни у відомості немає.";
        ws.Cell(r, 1).Style.Font.FontSize = 9;
        ws.Cell(r, 1).Style.Alignment.WrapText = true;

        ws.PageSetup.FitToPages(1, 0);
        ws.SheetView.FreezeRows(headerRow);
        ws.PageSetup.SetRowsToRepeatAtTop(1, headerRow);
    }

    internal static string SuggestFileName(SummaryStatementReport report)
    {
        var from = report.PeriodFrom.ToString("yyyy-MM");
        var to = report.PeriodTo.ToString("yyyy-MM");
        // ASCII дефіс — без en-dash у імені файлу (діалог збереження / оболонка).
        var period = from == to ? from : $"{from}-{to}";
        return SanitizeFileName($"Узагальнююча_відомість_Додаток_1_{period}");
    }

    private static void WriteMeta(IXLWorksheet ws, int row, string label, string value)
    {
        ws.Range(row, 1, row, 2).Merge();
        ws.Cell(row, 1).Value = label;
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Range(row, 3, row, 6).Merge();
        ws.Cell(row, 3).Value = value;
        ws.Cell(row, 3).Style.Alignment.WrapText = true;
    }

    private static string BookRequisites(SummaryStatementReport report)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(report.UnitName))
            parts.Add(report.UnitName.Trim());
        if (!string.IsNullOrWhiteSpace(report.UserName))
            parts.Add(report.UserName.Trim());
        return parts.Count == 0 ? "—" : string.Join(". ", parts);
    }

    private static string FormatPeriod(DateTime from, DateTime to) =>
        $"{from:dd.MM.yyyy} – {to:dd.MM.yyyy}";

    private static void WriteQty(IXLCell cell, decimal value, string format)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = format;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        var s = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(s) ? "Узагальнююча_відомість_Додаток_1" : s;
    }
}
