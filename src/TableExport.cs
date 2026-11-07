using System.Windows;
using ClosedXML.Excel;
using Microsoft.Win32;

namespace Obozny;

internal readonly record struct TableExportColumn(string Key, string Title);

/// <summary>Збереження видимих рядків «Залишків» або «Руху майна» в Excel.</summary>
internal static class TableExport
{
    public static void Save(
        Window owner,
        string fileTitle,
        string? filterNote,
        IReadOnlyList<TableExportColumn> columns,
        IReadOnlyList<PropertyRow> rows)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Зберегти таблицю в Excel",
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = fileTitle + ".xlsx",
            AddExtension = true,
            DefaultExt = "xlsx"
        };
        if (dlg.ShowDialog(owner) != true) return;

        try
        {
            using var wb = new XLWorkbook();
            var sheet = fileTitle.Length <= 31 ? fileTitle : fileTitle[..31];
            var ws = wb.Worksheets.Add(sheet);
            var r = 1;
            ws.Cell(r, 1).Value = fileTitle;
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Font.FontSize = 14;
            r++;
            if (!string.IsNullOrWhiteSpace(filterNote))
            {
                ws.Cell(r, 1).Value = filterNote;
                r++;
            }

            r++;
            var headerRow = r;
            for (var c = 0; c < columns.Count; c++)
            {
                var cell = ws.Cell(headerRow, c + 1);
                cell.Value = columns[c].Title;
                cell.Style.Font.Bold = true;
            }
            var valueCol = columns.Count + 1;
            ws.Cell(headerRow, valueCol).Value = "Вартість";
            ws.Cell(headerRow, valueCol).Style.Font.Bold = true;

            var qtyFormat = Ua.DecimalFormat(Ua.QtyDecimals());
            var priceFormat = Ua.DecimalFormat(Ua.PriceDecimals());
            int? qtyColumn = null;
            decimal qtySum = 0;
            decimal valueSum = 0;
            r = headerRow + 1;
            foreach (var row in rows)
            {
                for (var c = 0; c < columns.Count; c++)
                {
                    if (columns[c].Key == "Quantity") qtyColumn = c + 1;
                    WriteCell(ws.Cell(r, c + 1), row, columns[c].Key, qtyFormat, priceFormat);
                }
                var lineValue = row.Quantity * row.UnitPrice;
                var valueCell = ws.Cell(r, valueCol);
                valueCell.Value = lineValue;
                valueCell.Style.NumberFormat.Format = "#,##0.00";
                qtySum += row.Quantity;
                valueSum += lineValue;
                r++;
            }

            ws.Cell(r, 1).Value = "Разом";
            ws.Cell(r, 1).Style.Font.Bold = true;
            if (qtyColumn is int qc)
            {
                var cell = ws.Cell(r, qc);
                cell.Value = qtySum;
                cell.Style.NumberFormat.Format = qtyFormat;
                cell.Style.Font.Bold = true;
            }
            var totalValue = ws.Cell(r, valueCol);
            totalValue.Value = valueSum;
            totalValue.Style.NumberFormat.Format = "#,##0.00";
            totalValue.Style.Font.Bold = true;

            ws.SheetView.FreezeRows(headerRow);
            ws.Columns().AdjustToContents(1, 80);
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

    private static void WriteCell(IXLCell cell, PropertyRow row, string key, string qtyFormat, string priceFormat)
    {
        switch (key)
        {
            case "Date":
                if (row.Date is DateTime date)
                {
                    cell.Value = date;
                    cell.Style.DateFormat.Format = "dd.MM.yyyy";
                }
                break;
            case "Quantity":
                cell.Value = row.Quantity;
                cell.Style.NumberFormat.Format = qtyFormat;
                break;
            case "UnitPrice":
                cell.Value = row.UnitPrice;
                cell.Style.NumberFormat.Format = priceFormat;
                break;
            case "MoveKind":
                cell.Value = row.MoveKind;
                break;
            case "DocumentNumber":
                cell.Style.NumberFormat.Format = "@";
                cell.Value = row.DocumentNumber ?? "";
                break;
            case "Name":
                cell.Value = row.IsReserved ? row.Name + " (резерв)" : row.Name;
                break;
            case "Unit":
                cell.Value = row.Unit;
                break;
            case "Category":
                cell.Value = row.Category;
                break;
            default:
                cell.Value = "";
                break;
        }
    }
}
