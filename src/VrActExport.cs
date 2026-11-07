using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ClosedXML.Excel;
using Microsoft.Win32;

namespace Obozny;

/// <summary>Друк і Excel для Акта ВР (лише найменування / од. вим. / кількість).</summary>
internal static class VrActExport
{
    public static void SaveExcel(Window owner, Document source,
        IReadOnlyList<VrActLineRow> lines, StrongpointRow? row = null)
    {
        if (!BetaLicense.TryAllowDocumentImportExport(owner)) return;
        var title = FileTitle(source);
        var dlg = new SaveFileDialog
        {
            Title = "Зберегти Акт ВР у Excel",
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = SanitizeFileName(title) + ".xlsx",
            AddExtension = true,
            DefaultExt = "xlsx"
        };
        if (dlg.ShowDialog(owner) != true) return;

        try
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Акт ВР");
            var r = 1;
            ws.Cell(r, 1).Value = title;
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Font.FontSize = 14;
            r += 2;
            foreach (var line in VrActBuilder.HeaderLines(source, row))
            {
                ws.Cell(r, 1).Value = line;
                r++;
            }
            r++;
            ws.Cell(r, 1).Value = "Найменування";
            ws.Cell(r, 2).Value = "Од. вим.";
            ws.Cell(r, 3).Value = "Кількість";
            ws.Range(r, 1, r, 3).Style.Font.Bold = true;
            ws.Range(r, 1, r, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEE8");
            r++;
            foreach (var line in lines)
            {
                ws.Cell(r, 1).Value = line.Name;
                ws.Cell(r, 2).Value = line.Unit;
                ws.Cell(r, 3).Value = (double)line.Quantity;
                r++;
            }
            ws.Columns().AdjustToContents(1, 60);
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

    public static void Print(Window owner, Document source,
        IReadOnlyList<VrActLineRow> lines, StrongpointRow? row = null)
    {
        var pd = new PrintDialog();
        if (pd.ShowDialog() != true) return;
        try
        {
            var flow = BuildFlow(source, lines, row, pd.PrintableAreaWidth);
            var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
            pd.PrintDocument(paginator, FileTitle(source));
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "Не вдалося надіслати на друк:\n" + ex.Message, "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Сумісність із викликами з картки ОП (ЯС + StrongpointRow).</summary>
    public static void SaveExcel(Window owner, StrongpointRow row, Document yas,
        IReadOnlyList<VrActLineRow> lines) =>
        SaveExcel(owner, yas, lines, row);

    public static void Print(Window owner, StrongpointRow row, Document yas,
        IReadOnlyList<VrActLineRow> lines) =>
        Print(owner, yas, lines, row);

    private static FlowDocument BuildFlow(Document source,
        IReadOnlyList<VrActLineRow> lines, StrongpointRow? row, double pageWidth)
    {
        var flow = new FlowDocument
        {
            PageWidth = pageWidth,
            FontFamily = new FontFamily("Times New Roman"),
            FontSize = 12,
            PagePadding = new Thickness(40)
        };
        flow.Blocks.Add(new Paragraph(new Run(FileTitle(source)))
        {
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        foreach (var line in VrActBuilder.HeaderLines(source, row))
            flow.Blocks.Add(new Paragraph(new Run(line)) { Margin = new Thickness(0, 0, 0, 2) });

        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 12, 0, 0) };
        table.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        group.Rows.Add(MakeRow(["Найменування", "Од. вим.", "Кількість"], header: true));
        foreach (var line in lines)
            group.Rows.Add(MakeRow([line.Name, line.Unit, line.QuantityText], header: false));
        flow.Blocks.Add(table);
        return flow;
    }

    private static TableRow MakeRow(string[] cells, bool header)
    {
        var row = new TableRow();
        for (var i = 0; i < cells.Length; i++)
        {
            var para = new Paragraph(new Run(cells[i] ?? ""))
            {
                Margin = new Thickness(4, 3, 4, 3),
                FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
                TextAlignment = i == 0 ? TextAlignment.Left : TextAlignment.Center
            };
            if (i == cells.Length - 1) para.TextAlignment = TextAlignment.Right;
            var cell = new TableCell(para)
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0.5),
                Padding = new Thickness(2)
            };
            if (header)
                cell.Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xE8));
            row.Cells.Add(cell);
        }
        return row;
    }

    private static string FileTitle(Document source) =>
        $"Акт виконаних робіт № {VrActBuilder.DisplayVrNumber(source)}";

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        var s = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(s) ? "Акт_ВР" : s;
    }
}
