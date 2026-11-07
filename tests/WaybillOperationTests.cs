using System.IO;
using ClosedXML.Excel;
using Xunit;

namespace Obozny.Tests;

/// <summary>Регресія: Додаток 25 / накладна (вимога) з «Вид операції: видача».</summary>
public class WaybillOperationTests
{
    [Theory]
    [InlineData("Вид операції: видача", "видача")]
    [InlineData("Вид операції: Видача", "Видача")]
    [InlineData("Вид операції:  видача  ", "видача")]
    [InlineData("вид операції - здавання", "здавання")]
    public void OperationAfterLabel_extracts_value(string cell, string expected)
    {
        Assert.Equal(expected, ExcelImporter.OperationAfterLabel(cell));
    }

    [Fact]
    public void OperationAfterLabel_does_not_treat_vydacha_as_label_noise()
    {
        // Раніше Contains("вид") відкидав «видача» → порожньо → Unknown.
        Assert.Equal("видача", ExcelImporter.OperationAfterLabel("Вид операції: видача"));
        Assert.Equal(ImportMoveKind.Receipt, ExcelImporter.KindFromOperation("видача"));
        Assert.Equal(ImportMoveKind.Receipt, ExcelImporter.KindFromOperation("Видача"));
        Assert.Equal(ImportMoveKind.Receipt, ExcelImporter.KindFromOperation(" видача "));
    }

    [Theory]
    [InlineData("здавання", ImportMoveKind.Return)]
    [InlineData("видача", ImportMoveKind.Receipt)]
    [InlineData("приймання", ImportMoveKind.Receipt)]
    [InlineData("списання", ImportMoveKind.WriteOff)]
    public void KindFromOperation_maps_verbs(string op, ImportMoveKind expected)
    {
        Assert.Equal(expected, ExcelImporter.KindFromOperation(op));
    }

    [Fact]
    public void Read_dodatok25_inline_vydacha_is_receipt_waybill()
    {
        var path = Path.Combine(Path.GetTempPath(), "obozny-nakladna-vydacha-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("Накладна");
                ws.Cell(12, 9).Value = "Накладна (вимога) №";
                ws.Cell(12, 10).Value = "2426/1395";
                ws.Cell(18, 2).Value = "Вид операції: видача";
                ws.Cell(18, 9).Value = "Сторінка 1"; // шум біля поля — не має ламати inline
                ws.Cell(22, 3).Value = "Назва військового майна";
                ws.Cell(22, 7).Value = "Одиниця виміру";
                ws.Cell(22, 10).Value = "Вартість за одиницю";
                ws.Cell(22, 11).Value = "Кількість";
                ws.Cell(23, 11).Value = "Видати (прийняти)";
                ws.Cell(23, 12).Value = "Відпущено (прийнято)";
                ws.Cell(25, 3).Value = "Кутик 40х40х4";
                ws.Cell(25, 7).Value = "т";
                ws.Cell(25, 10).Value = 100;
                ws.Cell(25, 11).Value = 2;
                ws.Cell(25, 12).Value = 2;
                wb.SaveAs(path);
            }

            var rows = ExcelImporter.Read(path);
            Assert.NotEmpty(rows);
            Assert.All(rows, r => Assert.Equal(ImportMoveKind.Receipt, r.Kind));
            Assert.All(rows, r => Assert.Equal("видача", r.Operation, ignoreCase: true));
            Assert.Equal("2426/1395", rows[0].Number);

            var group = ImportGrouping.Group(rows).Single();
            Assert.Equal(DocType.Receipt, group.Type);
            Assert.Contains("вимога", ImportGrouping.ReviewTitle(group, true), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(MoveKindNames.Receipt, ImportGrouping.MoveKindLabel(group.Kind));
            Assert.Equal(ImportPosting.Accept, ImportPosting.Guess(rows));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Read_separate_cells_vydacha_skips_page_watermark()
    {
        var path = Path.Combine(Path.GetTempPath(), "obozny-nakladna-split-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("Накладна");
                ws.Cell(12, 9).Value = "Накладна (вимога) №";
                ws.Cell(12, 10).Value = "2426/1400";
                ws.Cell(18, 2).Value = "Вид операції:";
                ws.Cell(18, 3).Value = "Сторінка 1";
                ws.Cell(18, 4).Value = "видача";
                ws.Cell(22, 3).Value = "Назва військового майна";
                ws.Cell(22, 7).Value = "Одиниця виміру";
                ws.Cell(22, 11).Value = "Кількість";
                ws.Cell(25, 3).Value = "Пісок";
                ws.Cell(25, 7).Value = "куб.м";
                ws.Cell(25, 11).Value = 10;
                wb.SaveAs(path);
            }

            var rows = ExcelImporter.Read(path);
            Assert.NotEmpty(rows);
            Assert.Equal(ImportMoveKind.Receipt, rows[0].Kind);
            Assert.Equal("видача", rows[0].Operation, ignoreCase: true);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData("Передає: СОВТ та МСВ", "СОВТ та МСВ")]
    [InlineData("Передає: Інженерний склад в/ч А4638", "Інженерний склад в/ч А4638")]
    public void PeredayeAfterLabel_extracts_value(string cell, string expected)
    {
        Assert.Equal(expected, ExcelImporter.PeredayeAfterLabel(cell));
    }

    [Fact]
    public void Read_waybill_maps_peredaye_sovt_to_supply_service()
    {
        var path = Path.Combine(Path.GetTempPath(), "obozny-peredaye-sovt-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("Накладна");
                ws.Cell(12, 9).Value = "Накладна (вимога) №";
                ws.Cell(12, 10).Value = "3619";
                ws.Cell(15, 1).Value = "Передає";
                ws.Cell(15, 3).Value = "СОВТ та МСВ";
                ws.Cell(18, 2).Value = "Вид операції: видача";
                ws.Cell(22, 3).Value = "Назва військового майна";
                ws.Cell(22, 7).Value = "Одиниця виміру";
                ws.Cell(22, 11).Value = "Кількість";
                ws.Cell(25, 3).Value = "Арматура";
                ws.Cell(25, 7).Value = "т";
                ws.Cell(25, 11).Value = 4;
                wb.SaveAs(path);
            }

            var rows = ExcelImporter.Read(path);
            Assert.NotEmpty(rows);
            Assert.Equal("СОВТ та МСВ", rows[0].Peredaye);
            Assert.Equal("СОВТ та МСВ", rows[0].SupplyService);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Read_waybill_maps_engineering_warehouse_peredaye_to_is_ssp()
    {
        var path = Path.Combine(Path.GetTempPath(), "obozny-peredaye-eng-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("Накладна");
                ws.Cell(12, 9).Value = "Накладна (вимога) №";
                ws.Cell(12, 10).Value = "2426/1456";
                ws.Cell(3, 4).Value = "Передає:";
                ws.Cell(3, 5).Value = "Інженерний склад";
                ws.Cell(18, 2).Value = "Вид операції: видача";
                ws.Cell(22, 3).Value = "Назва військового майна";
                ws.Cell(22, 7).Value = "Одиниця виміру";
                ws.Cell(22, 11).Value = "Кількість";
                ws.Cell(25, 3).Value = "Кутик";
                ws.Cell(25, 7).Value = "шт";
                ws.Cell(25, 11).Value = 29;
                wb.SaveAs(path);
            }

            var rows = ExcelImporter.Read(path);
            Assert.NotEmpty(rows);
            Assert.Equal("Інженерний склад", rows[0].Peredaye);
            Assert.Equal("ІС ССП", rows[0].SupplyService);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
