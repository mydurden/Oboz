using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Obozny;

/// <summary>
/// Перегляд Узагальнюючої відомості (Додаток 1) на екрані.
/// ClosedXML/OpenXml тут не підвантажуються — лише після «Зберегти в Excel».
/// </summary>
public partial class SummaryStatementViewWindow : Window
{
    private readonly SummaryStatementReport _report;

    internal SummaryStatementViewWindow(SummaryStatementReport report)
    {
        InitializeComponent();
        _report = report ?? throw new ArgumentNullException(nameof(report));
        TitleText.Text = BuildTitle(_report);
        InfoText.Text = BuildInfo(_report);
        BuildColumns();
        LinesGrid.ItemsSource = BuildRows(_report);
    }

    private void BuildColumns()
    {
        LinesGrid.Columns.Clear();
        var left = (Style)FindResource("CellLeft");
        var center = (Style)FindResource("CellCenter");
        var right = (Style)FindResource("CellRight");

        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "№ з/п",
            Binding = new Binding(nameof(ViewRow.Number)),
            Width = new DataGridLength(64),
            ElementStyle = center
        });
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Найменування військового майна",
            Binding = new Binding(nameof(ViewRow.Name)),
            Width = new DataGridLength(3, DataGridLengthUnitType.Star),
            ElementStyle = left
        });
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "О.В.",
            Binding = new Binding(nameof(ViewRow.Unit)),
            Width = new DataGridLength(90),
            ElementStyle = center
        });
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Надійшло",
            Binding = new Binding(nameof(ViewRow.InQtyText)),
            Width = new DataGridLength(120),
            ElementStyle = right
        });
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Вибуло",
            Binding = new Binding(nameof(ViewRow.OutQtyText)),
            Width = new DataGridLength(120),
            ElementStyle = right
        });
        LinesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Наявне",
            Binding = new Binding(nameof(ViewRow.BalanceText)),
            Width = new DataGridLength(120),
            ElementStyle = right
        });
    }

    private static List<ViewRow> BuildRows(SummaryStatementReport report)
    {
        var qtyFormat = "#,##0.###";
        var list = new List<ViewRow>(report.Rows.Count);
        var n = 1;
        foreach (var row in report.Rows)
        {
            list.Add(new ViewRow(
                n.ToString(),
                row.Name,
                row.Unit,
                Ua.Num(row.InQty, qtyFormat),
                Ua.Num(row.OutQty, qtyFormat),
                Ua.Num(row.Balance, qtyFormat)));
            n++;
        }
        return list;
    }

    private static string BuildTitle(SummaryStatementReport report)
    {
        var numberPart = string.IsNullOrWhiteSpace(report.Number)
            ? "______"
            : report.Number.Trim();
        return $"Узагальнююча відомість № {numberPart}";
    }

    private static string BuildInfo(SummaryStatementReport report)
    {
        var lines = new List<string>
        {
            "Додаток 1 до Інструкції з обліку військового майна у Збройних Силах України",
            $"Звітний період: {report.PeriodFrom:dd.MM.yyyy} – {report.PeriodTo:dd.MM.yyyy}",
            $"Дата складання: {report.CompiledAt:dd.MM.yyyy}",
            $"Рядків: {report.Rows.Count}"
        };
        if (!string.IsNullOrWhiteSpace(report.SupplyService))
            lines.Insert(1, $"Служба забезпечення: {report.SupplyService.Trim()}");
        if (!string.IsNullOrWhiteSpace(report.UnitName))
            lines.Insert(1, $"Підрозділ: {report.UnitName.Trim()}");
        return string.Join("\n", lines);
    }

    private void ExcelClick(object sender, RoutedEventArgs e) =>
        SummaryStatementExport.Save(this, _report);

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private sealed record ViewRow(
        string Number,
        string Name,
        string Unit,
        string InQtyText,
        string OutQtyText,
        string BalanceText);
}
