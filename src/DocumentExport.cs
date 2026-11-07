using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ClosedXML.Excel;
using Microsoft.Win32;

namespace Obozny;

/// <summary>Друк і збереження документа в Excel.</summary>
internal static class DocumentExport
{
    public static void SaveExcel(Window owner, Document doc)
    {
        if (!BetaLicense.TryAllowDocumentImportExport(owner)) return;
        var title = FileTitle(doc);
        var dlg = new SaveFileDialog
        {
            Title = "Зберегти документ у Excel",
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = SanitizeFileName(title) + ".xlsx",
            AddExtension = true,
            DefaultExt = "xlsx"
        };
        if (dlg.ShowDialog(owner) != true) return;

        try
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Документ");
            WriteExcel(ws, doc);
            wb.SaveAs(dlg.FileName);
            MessageBox.Show(owner, "Файл збережено.", "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "Не вдалося зберегти файл:\n" + ex.Message, "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void Print(Window owner, Document doc)
    {
        var pd = new PrintDialog();
        if (pd.ShowDialog() != true) return;

        try
        {
            var flow = BuildFlowDocument(doc, pd.PrintableAreaWidth);
            var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
            pd.PrintDocument(paginator, FileTitle(doc));
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "Не вдалося надіслати на друк:\n" + ex.Message, "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void WriteExcel(IXLWorksheet ws, Document doc)
    {
        if (doc.Type == DocType.ExplosiveWorks)
        {
            ExplosiveWorksExport.Write(ws, doc);
            return;
        }

        var title = FileTitle(doc);
        var r = 1;
        ws.Cell(r, 1).Value = title;
        ws.Cell(r, 1).Style.Font.Bold = true;
        ws.Cell(r, 1).Style.Font.FontSize = 14;
        r += 2;

        foreach (var line in HeaderLines(doc))
        {
            ws.Cell(r, 1).Value = line;
            r++;
        }
        r++;

        if (doc.Type == DocType.QualityState)
        {
            WriteQualityStateExcel(ws, doc, ref r);
            ws.Columns().AdjustToContents(1, 60);
            ws.Column(1).Width = Math.Min(60, Math.Max(28, ws.Column(1).Width));
            return;
        }

        // Як у ЯС: Найменування | О.В. | Категорія | Кількість | Ціна; для надходження — ще Сума.
        var withSum = doc.Type == DocType.Receipt;
        var col = 1;
        ws.Cell(r, col++).Value = "Найменування";
        ws.Cell(r, col++).Value = "О.В.";
        ws.Cell(r, col++).Value = "Категорія";
        ws.Cell(r, col++).Value = "Кількість";
        ws.Cell(r, col++).Value = "Ціна";
        if (withSum)
            ws.Cell(r, col).Value = "Сума";
        else
            col--;
        ws.Range(r, 1, r, col).Style.Font.Bold = true;
        ws.Range(r, 1, r, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEE8");
        r++;

        var props = AppStore.Instance.Data.Properties.ToDictionary(p => p.Id);
        decimal total = 0;
        foreach (var line in doc.Lines)
        {
            props.TryGetValue(line.PropertyId, out var p);
            var price = DocumentViewWindow.ResolvePrice(line, props);
            var sum = price * line.Quantity;
            total += sum;

            col = 1;
            ws.Cell(r, col++).Value = line.Name;
            ws.Cell(r, col++).Value = line.Unit;
            ws.Cell(r, col++).Value = p?.Category ?? "";
            ws.Cell(r, col).Value = line.Quantity;
            ws.Cell(r, col).Style.NumberFormat.Format = "#,##0.###";
            col++;
            if (price > 0)
            {
                ws.Cell(r, col).Value = price;
                ws.Cell(r, col).Style.NumberFormat.Format = "#,##0.####";
            }
            col++;
            if (withSum)
            {
                ws.Cell(r, col).Value = sum;
                ws.Cell(r, col).Style.NumberFormat.Format = "#,##0.00";
            }
            r++;
        }

        if (withSum)
        {
            r++;
            ws.Cell(r, 1).Value = "Загальна вартість за накладною:";
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 6).Value = total;
            ws.Cell(r, 6).Style.Font.Bold = true;
            ws.Cell(r, 6).Style.NumberFormat.Format = "#,##0.00";
        }

        ws.Columns().AdjustToContents(1, 60);
        ws.Column(1).Width = Math.Min(60, Math.Max(24, ws.Column(1).Width));
    }

    private static void WriteQualityStateExcel(IXLWorksheet ws, Document doc, ref int r)
    {
        ws.Cell(r, 1).Value = "Найменування матеріалу";
        ws.Cell(r, 2).Value = "Одиниці виміру";
        ws.Cell(r, 3).Value = "Категорія";
        ws.Cell(r, 4).Value = "Кількість";
        ws.Cell(r, 5).Value = "Ціна";
        ws.Range(r, 1, r, 5).Style.Font.Bold = true;
        ws.Range(r, 1, r, 5).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEE8");
        r++;

        foreach (var row in QualityStateActTable.Build(doc))
        {
            ws.Cell(r, 1).Value = row.Name;
            ws.Cell(r, 2).Value = row.Unit;
            ws.Cell(r, 3).Value = row.Category;
            if (row.QuantityValue != null)
            {
                ws.Cell(r, 4).Value = row.QuantityValue.Value;
                ws.Cell(r, 4).Style.NumberFormat.Format = row.IsStructure ? "0" : "#,##0.###";
            }
            if (row.PriceValue != null)
            {
                ws.Cell(r, 5).Value = row.PriceValue.Value;
                ws.Cell(r, 5).Style.NumberFormat.Format = "#,##0.####";
            }
            if (row.IsStructure)
            {
                ws.Range(r, 1, r, 5).Style.Font.Bold = true;
                ws.Range(r, 1, r, 5).Style.Fill.BackgroundColor = XLColor.FromHtml("#E3F2E8");
            }
            r++;
        }
    }

    private static FlowDocument BuildFlowDocument(Document doc, double pageWidth)
    {
        var flow = new FlowDocument
        {
            PageWidth = pageWidth,
            PagePadding = new Thickness(48),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            ColumnWidth = pageWidth
        };

        flow.Blocks.Add(new Paragraph(new Run(FileTitle(doc)))
        {
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        });

        foreach (var line in HeaderLines(doc))
        {
            flow.Blocks.Add(new Paragraph(new Run(line))
            {
                Margin = new Thickness(0, 0, 0, 2),
                Foreground = Brushes.DimGray
            });
        }

        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 16, 0, 0) };
        var qs = doc.Type == DocType.QualityState;
        var withSum = doc.Type == DocType.Receipt;

        if (qs)
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(2.8, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(0.7, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(0.6, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(0.7, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(0.8, GridUnitType.Star) });
        }
        else if (withSum)
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(2.4, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(0.5, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(0.6, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(0.7, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(0.7, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(0.8, GridUnitType.Star) });
        }
        else
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(2.8, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(0.7, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(0.6, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(0.7, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(0.8, GridUnitType.Star) });
        }

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        if (qs)
        {
            group.Rows.Add(MakeRow(
                new[] { "Найменування матеріалу", "Одиниці виміру", "Категорія", "Кількість", "Ціна" },
                bold: true, header: true, structure: false));
            foreach (var row in QualityStateActTable.Build(doc))
            {
                group.Rows.Add(MakeRow(
                    new[] { row.Name, row.Unit, row.Category, row.QuantityText, row.PriceText },
                    bold: row.IsStructure, header: false, structure: row.IsStructure));
            }
        }
        else
        {
            string[] headerCells = withSum
                ? new[] { "Найменування", "О.В.", "Категорія", "Кількість", "Ціна", "Сума" }
                : new[] { "Найменування", "О.В.", "Категорія", "Кількість", "Ціна" };
            group.Rows.Add(MakeRow(headerCells, bold: true, header: true, structure: false));

            var props = AppStore.Instance.Data.Properties.ToDictionary(p => p.Id);
            decimal total = 0;
            foreach (var line in doc.Lines)
            {
                props.TryGetValue(line.PropertyId, out var p);
                var price = DocumentViewWindow.ResolvePrice(line, props);
                var sum = price * line.Quantity;
                total += sum;
                var qty = Ua.Num(line.Quantity, "#,##0.###");
                var priceText = price > 0 ? Ua.FormatPrice(price) : "";
                var cat = p?.Category ?? "";
                if (withSum)
                {
                    group.Rows.Add(MakeRow(new[]
                    {
                        line.Name, line.Unit, cat, qty, priceText, Ua.Num(sum, "#,##0.00")
                    }, bold: false, header: false, structure: false));
                }
                else
                {
                    group.Rows.Add(MakeRow(new[]
                    {
                        line.Name, line.Unit, cat, qty, priceText
                    }, bold: false, header: false, structure: false));
                }
            }

            flow.Blocks.Add(table);
            if (withSum)
            {
                flow.Blocks.Add(new Paragraph(new Run(
                    $"Загальна вартість за накладною: {Ua.Num(total, "#,##0.00")}"))
                {
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 13,
                    Margin = new Thickness(0, 14, 0, 0),
                    TextAlignment = TextAlignment.Right
                });
            }
            return flow;
        }

        flow.Blocks.Add(table);
        return flow;
    }

    private static TableRow MakeRow(string[] cells, bool bold, bool header, bool structure)
    {
        var row = new TableRow();
        for (var i = 0; i < cells.Length; i++)
        {
            var para = new Paragraph(new Run(cells[i] ?? ""))
            {
                Margin = new Thickness(4, 3, 4, 3),
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                TextAlignment = TextAlignment.Left
            };
            if (i >= 1) para.TextAlignment = TextAlignment.Center;
            if (i >= cells.Length - 2) para.TextAlignment = TextAlignment.Right;

            var cell = new TableCell(para)
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0.5),
                Padding = new Thickness(2)
            };
            if (header)
                cell.Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xE8));
            else if (structure)
                cell.Background = new SolidColorBrush(Color.FromRgb(0xE3, 0xF2, 0xE8));
            row.Cells.Add(cell);
        }
        return row;
    }

    private static IEnumerable<string> HeaderLines(Document doc)
    {
        var status = doc.Status == DocStatus.Posted ? "Проведено" : "Чернетка";
        yield return $"Дата: {doc.Date:dd.MM.yyyy}";
        if (!string.IsNullOrWhiteSpace(doc.WorkTypeName))
            yield return $"Вид робіт: {doc.WorkTypeName}";
        yield return $"Документ: {DocTypeNames.Title(doc.Type)}";
        yield return $"Тип руху: {MoveKindNames.Of(doc.Type)}";
        yield return $"Статус: {status}";

        if (doc.Type == DocType.QualityState)
        {
            yield return $"Назва опорного пункту: {Empty(doc.StrongpointName)}";
            yield return $"Дата введення в експлуатацію: {(doc.CommissioningDate ?? doc.Date):dd.MM.yyyy}";
            yield return $"Реєстраційний номер опорного пункту: {Empty(doc.StrongpointRegNumber)}";
            yield return $"№ Акта ВР: {VrActBuilder.DisplayVrNumber(doc)}";
            // Ті самі накладні, що в шапці Акта ВР (немає → «н/д»).
            yield return VrActBuilder.InvoicesSentence(doc);
        }
        else if (doc.Type == DocType.QualityChange)
        {
            yield return $"Серія формуляру: {Empty(doc.FormSeries)}";
            yield return $"Номер формуляру: {Empty(doc.FormNumber)}";
            yield return $"Назва інженерного загородження: {Empty(doc.BarrierName)}";
            yield return $"№ Акта ВР: {VrActBuilder.DisplayVrNumber(doc)}";
            yield return VrActBuilder.InvoicesSentence(doc);
        }
        else if (doc.Type == DocType.ExplosiveWorks)
        {
            yield return "Додаток 16 до Керівництва з підривної (вибухової) справи (п. 12.1.4)";
            yield return "Накладні (FIFO): №№ " + ExplosiveWorksAct.InvoiceNumbers(doc);
        }

        if (!string.IsNullOrWhiteSpace(doc.Note))
            yield return $"Примітка: {doc.Note}";
    }

    private static string FileTitle(Document doc)
    {
        var t = DocTypeNames.Title(doc.Type);
        var n = DocTypeNames.FormatNumber(doc.Number);
        return $"{t} № {n}";
    }

    private static string Empty(string s) => string.IsNullOrWhiteSpace(s) ? "—" : s;

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        var s = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(s) ? "Документ" : s;
    }
}
