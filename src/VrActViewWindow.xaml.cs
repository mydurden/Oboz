using System.Windows;

namespace Obozny;

/// <summary>
/// Акт ВР: лише перегляд / друк (номер і дату не редагуємо — лише з форми ЯС або ЗЯС).
/// </summary>
public partial class VrActViewWindow : Window
{
    private readonly StrongpointRow? _row;
    private readonly Document _source;
    private readonly List<VrActLineRow> _lines;

    public VrActViewWindow(Document sourceDoc, StrongpointRow? row = null)
    {
        InitializeComponent();
        _source = sourceDoc;
        _row = row ?? (sourceDoc.Type == DocType.QualityState
            ? AppStore.Instance.StrongpointRowFor(sourceDoc)
            : null);
        _lines = VrActBuilder.AggregateLines(_source);

        var num = VrActBuilder.DisplayVrNumber(_source);
        Title = $"Акт ВР № {num}";
        TitleText.Text = $"Акт виконаних робіт № {num}";
        InfoText.Text = string.Join("\n", VrActBuilder.HeaderLines(_source, _row));
        LinesGrid.ItemsSource = _lines;
    }

    /// <summary>З картки ОП — Акт ВР першого/основного ЯС.</summary>
    public VrActViewWindow(StrongpointRow row)
        : this(RequirePrimary(row), row)
    {
    }

    private static Document RequirePrimary(StrongpointRow row)
    {
        var doc = VrActBuilder.PrimaryDocument(row);
        if (doc == null)
            throw new InvalidOperationException("Немає акта ЯС для цього опорного пункту.");
        return doc;
    }

    private void PrintClick(object sender, RoutedEventArgs e) =>
        VrActExport.Print(this, _source, _lines, _row);

    private void ExcelClick(object sender, RoutedEventArgs e) =>
        VrActExport.SaveExcel(this, _source, _lines, _row);

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
