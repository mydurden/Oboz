using ClosedXML.Excel;

namespace Obozny;

/// <summary>Excel Додаток 16 (Акт списання вибухових матеріалів) за зразком форми.</summary>
internal static class ExplosiveWorksExport
{
    public static void Write(IXLWorksheet ws, Document doc)
    {
        ws.ShowGridLines = false;
        for (var c = 1; c <= 22; c++)
            ws.Column(c).Width = 4.2;
        ws.Column(2).Width = 14;
        ws.Column(12).Width = 12;
        ws.Column(14).Width = 14;
        ws.Column(17).Width = 10;
        ws.Column(20).Width = 14;

        // Норма — правий кут
        ws.Cell(1, 16).Value = "Додаток 16";
        ws.Cell(2, 16).Value = "до Керівництва з підривної (вибухової) справи";
        ws.Cell(3, 16).Value = "у Збройних Силах України (пункт 12.1.4)";
        ws.Range(1, 16, 3, 22).Style.Font.FontSize = 9;
        ws.Range(1, 16, 3, 22).Style.Alignment.WrapText = true;

        // ЗАТВЕРДЖУЮ
        ws.Cell(5, 14).Value = "ЗАТВЕРДЖУЮ";
        ws.Cell(5, 14).Style.Font.Bold = true;
        ws.Cell(6, 14).Value = "Командир військової частини ________";
        ws.Cell(7, 14).Value = "звання";
        ws.Cell(7, 18).Value = "Ім'я ПРІЗВИЩЕ";
        ws.Cell(8, 14).Value = "(військове звання, підпис, ініціали та прізвище)";
        ws.Cell(8, 14).Style.Font.FontSize = 8;
        ws.Cell(9, 14).Value = "дд";
        ws.Cell(9, 16).Value = "місяць";
        ws.Cell(9, 18).Value = "рррр";
        ws.Cell(9, 20).Value = "року";

        // Заголовок акта
        var num = DocTypeNames.FormatNumber(doc.Number);
        ws.Cell(11, 11).Value = "АКТ №";
        ws.Cell(11, 13).Value = num;
        ws.Cell(11, 13).Style.Font.Bold = true;
        ws.Range(12, 2, 12, 20).Merge();
        ws.Cell(12, 2).Value =
            "списання вибухових матеріалів, витрачених під час підривних робіт";
        ws.Cell(12, 2).Style.Font.Bold = true;
        ws.Cell(12, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(13, 7).Value = doc.Date.ToString("dd.MM.yyyy") + " року";

        // Комісія — підказки
        ws.Cell(14, 1).Value = "Комісія у складі:";
        ws.Cell(15, 1).Value = "Голова комісії:";
        ws.Cell(16, 2).Value = "посада - командир ІСвід чи ІСВ";
        ws.Cell(16, 2).Style.Font.FontSize = 9;
        ws.Cell(16, 2).Style.Font.Italic = true;
        ws.Cell(17, 1).Value = "звання";
        ws.Cell(17, 16).Value = "Ім'я ПРІЗВИЩЕ";
        ws.Cell(18, 1).Value = "(військове звання)";
        ws.Cell(18, 16).Value = "(ініціали та прізвище)";
        ws.Range(18, 1, 18, 22).Style.Font.FontSize = 8;

        ws.Cell(19, 1).Value = "Члени комісії:";
        ws.Cell(20, 2).Value = "посада - сапер (мінер) 1";
        ws.Cell(20, 2).Style.Font.FontSize = 9;
        ws.Cell(20, 2).Style.Font.Italic = true;
        ws.Cell(21, 1).Value = "звання";
        ws.Cell(21, 16).Value = "Ім'я ПРІЗВИЩЕ";
        ws.Cell(22, 1).Value = "(військове звання)";
        ws.Cell(22, 16).Value = "(ініціали та прізвище)";
        ws.Range(22, 1, 22, 22).Style.Font.FontSize = 8;
        ws.Cell(23, 2).Value = "посада - сапер (мінер) 2";
        ws.Cell(23, 2).Style.Font.FontSize = 9;
        ws.Cell(23, 2).Style.Font.Italic = true;
        ws.Cell(24, 1).Value = "звання";
        ws.Cell(24, 16).Value = "Ім'я ПРІЗВИЩЕ";
        ws.Cell(25, 1).Value = "(військове звання)";
        ws.Cell(25, 16).Value = "(ініціали та прізвище)";
        ws.Range(25, 1, 25, 22).Style.Font.FontSize = 8;

        ws.Range(26, 1, 26, 22).Merge();
        ws.Cell(26, 1).Value =
            "склала цей акт про списання витрачених вибухових матеріалів, отриманих за накладною";
        ws.Cell(26, 1).Style.Alignment.WrapText = true;

        // Накладні FIFO
        var invoices = ExplosiveWorksAct.InvoiceDetails(doc);
        var invStart = 27;
        for (var i = 0; i < 6; i++)
        {
            var r = invStart + i;
            ws.Cell(r, 1).Value = "№";
            if (i < invoices.Count)
            {
                ws.Cell(r, 2).Value = invoices[i].Number;
                ws.Cell(r, 3).Value = "від";
                ws.Cell(r, 4).Value = invoices[i].Date.ToString("dd");
                ws.Cell(r, 6).Value = invoices[i].Date.ToString("MM");
                ws.Cell(r, 11).Value = invoices[i].Date.ToString("yyyy") + " р.";
            }
            else
            {
                ws.Cell(r, 2).Value = i == 0 && invoices.Count == 0 ? "н/д" : "…";
                ws.Cell(r, 3).Value = "від";
                ws.Cell(r, 4).Value = "дд";
                ws.Cell(r, 6).Value = "місяць";
                ws.Cell(r, 11).Value = "рррр р.";
            }
            ws.Cell(r, 13).Value = "зі складу";
            ws.Cell(r, 16).Value = "назва складу служби";
            ws.Cell(r, 16).Style.Font.Italic = true;
            ws.Cell(r, 16).Style.Font.FontSize = 9;
        }

        // Заголовок таблиці
        const int headerRow = 34;
        ws.Cell(headerRow, 1).Value = "№ з/п";
        ws.Range(headerRow, 2, headerRow, 11).Merge();
        ws.Cell(headerRow, 2).Value = "Назви (шифр) боєприпасів";
        ws.Range(headerRow, 12, headerRow, 13).Merge();
        ws.Cell(headerRow, 12).Value = "Одиниці обліку";
        ws.Range(headerRow, 14, headerRow, 16).Merge();
        ws.Cell(headerRow, 14).Value = "Отримана кількість за накладною";
        ws.Range(headerRow, 17, headerRow, 19).Merge();
        ws.Cell(headerRow, 17).Value = "Витрати";
        ws.Range(headerRow, 20, headerRow, 22).Merge();
        ws.Cell(headerRow, 20).Value = "Підлягає здаванню на склад";
        ws.Range(headerRow, 1, headerRow, 22).Style.Font.Bold = true;
        ws.Range(headerRow, 1, headerRow, 22).Style.Alignment.WrapText = true;
        ws.Range(headerRow, 1, headerRow, 22).Style.Alignment.Horizontal =
            XLAlignmentHorizontalValues.Center;
        ws.Range(headerRow, 1, headerRow, 22).Style.Fill.BackgroundColor =
            XLColor.FromHtml("#E8EEE8");
        ws.Row(headerRow).Height = 36;

        var qtyFormat = Ua.DecimalFormat(Ua.QtyDecimals());
        var data = ExplosiveWorksAct.Build(doc);
        var rData = headerRow + 1;
        var slots = Math.Max(7, data.Count);
        for (var i = 0; i < slots; i++)
        {
            ws.Cell(rData, 1).Value = i + 1;
            ws.Cell(rData, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            if (i < data.Count)
            {
                var row = data[i];
                ws.Range(rData, 2, rData, 11).Merge();
                ws.Cell(rData, 2).Value = row.Name;
                ws.Range(rData, 12, rData, 13).Merge();
                ws.Cell(rData, 12).Value = row.Unit;
                ws.Cell(rData, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Range(rData, 14, rData, 16).Merge();
                ws.Cell(rData, 14).Value = row.ReceivedQty;
                ws.Cell(rData, 14).Style.NumberFormat.Format = qtyFormat;
                ws.Range(rData, 17, rData, 19).Merge();
                ws.Cell(rData, 17).Value = row.SpentQty;
                ws.Cell(rData, 17).Style.NumberFormat.Format = qtyFormat;
                ws.Range(rData, 20, rData, 22).Merge();
                ws.Cell(rData, 20).Value = row.ReturnQty;
                ws.Cell(rData, 20).Style.NumberFormat.Format = qtyFormat;
            }
            else
            {
                ws.Range(rData, 2, rData, 11).Merge();
                ws.Range(rData, 12, rData, 13).Merge();
                ws.Range(rData, 14, rData, 16).Merge();
                ws.Range(rData, 17, rData, 19).Merge();
                ws.Range(rData, 20, rData, 22).Merge();
            }

            for (var c = 1; c <= 22; c++)
                ws.Cell(rData, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            rData++;
        }

        // Висновок / огляд — підказки
        rData += 1;
        ws.Cell(rData, 1).Value = "Вибухові роботи проводились :";
        rData++;
        ws.Range(rData, 2, rData, 22).Merge();
        ws.Cell(rData, 2).Value =
            "Зразок висновку: В період з __.__.____ по __.__.____ в районі ________, в зоні відповідальності в/ч ____, було проведено ________. Під час виконання завдань було дотримано заходів безпеки…";
        ws.Cell(rData, 2).Style.Font.Italic = true;
        ws.Cell(rData, 2).Style.Font.FontSize = 9;
        ws.Cell(rData, 2).Style.Alignment.WrapText = true;
        ws.Row(rData).Height = 48;
        rData++;
        ws.Range(rData, 2, rData, 22).Merge();
        ws.Cell(rData, 2).Value =
            "На підставі бойового розпорядження командира в/ч ____ №__ від дд.мм.рррр р.";
        ws.Cell(rData, 2).Style.Font.Italic = true;
        ws.Cell(rData, 2).Style.Font.FontSize = 9;

        rData += 2;
        ws.Cell(rData, 1).Value = "Місце проведення підривання оглянуто";
        rData++;
        ws.Range(rData, 2, rData, 22).Merge();
        ws.Cell(rData, 2).Value =
            "звання Ім'я ПРІЗВИЩЕ (сапер, що виконав роботу), одразу після виконання завдання.";
        ws.Cell(rData, 2).Style.Font.Italic = true;
        ws.Cell(rData, 2).Style.Font.FontSize = 9;
        rData++;
        ws.Cell(rData, 2).Value = "(ким персонально і час огляду)";
        ws.Cell(rData, 2).Style.Font.FontSize = 8;

        rData += 1;
        ws.Cell(rData, 1).Value = "Залишків вибухових матеріалів";
        rData++;
        ws.Cell(rData, 1).Value = "не виявлено";
        rData++;
        ws.Cell(rData, 1).Value = "(не виявлено або виявлено (що саме))";
        ws.Cell(rData, 1).Style.Font.FontSize = 8;
        rData++;
        ws.Cell(rData, 1).Value = "Виявлені залишки";
        ws.Cell(rData, 5).Value = "відсутні";
        rData++;
        ws.Cell(rData, 5).Value = "(що зроблено або спосіб знищення)";
        ws.Cell(rData, 5).Style.Font.FontSize = 8;

        rData += 2;
        ws.Cell(rData, 1).Value = "Голова комісії:";
        rData++;
        ws.Cell(rData, 1).Value = "звання";
        ws.Cell(rData, 16).Value = "Ім'я ПРІЗВИЩЕ";
        rData++;
        ws.Cell(rData, 1).Value = "(військове звання)";
        ws.Cell(rData, 11).Value = "(підпис)";
        ws.Cell(rData, 16).Value = "(ініціали та прізвище)";
        ws.Range(rData, 1, rData, 22).Style.Font.FontSize = 8;

        rData += 1;
        ws.Cell(rData, 1).Value = "Члени комісії:";
        rData++;
        ws.Cell(rData, 1).Value = "звання";
        ws.Cell(rData, 16).Value = "Ім'я ПРІЗВИЩЕ";
        rData++;
        ws.Cell(rData, 1).Value = "(військове звання)";
        ws.Cell(rData, 11).Value = "(підпис)";
        ws.Cell(rData, 16).Value = "(ініціали та прізвище)";
        ws.Range(rData, 1, rData, 22).Style.Font.FontSize = 8;
        rData += 1;
        ws.Cell(rData, 1).Value = "звання";
        ws.Cell(rData, 16).Value = "Ім'я ПРІЗВИЩЕ";
        rData++;
        ws.Cell(rData, 1).Value = "(військове звання)";
        ws.Cell(rData, 11).Value = "(підпис)";
        ws.Cell(rData, 16).Value = "(ініціали та прізвище)";
        ws.Range(rData, 1, rData, 22).Style.Font.FontSize = 8;

        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
    }
}
