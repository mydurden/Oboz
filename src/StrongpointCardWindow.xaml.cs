using System.Windows;
using System.Windows.Input;

namespace Obozny;

public partial class StrongpointCardWindow : Window
{
    private readonly StrongpointRow _row;

    public Document? DraftToOpen { get; private set; }

    public StrongpointCardWindow(StrongpointRow row)
    {
        _row = row;
        InitializeComponent();
        StrongpointNameUi.Apply(TitleText, row.Name, row.IsDraft);
        Title = StrongpointNameUi.BaseName(row.Name) + (row.IsDraft ? " " + StrongpointNameUi.DraftSuffix : "");
        InfoText.Text =
            $"Реєстраційний номер: {(string.IsNullOrWhiteSpace(row.RegNumber) ? "—" : row.RegNumber)}\n" +
            $"Дата введення в експлуатацію: {(row.CommissioningDate?.ToString("dd.MM.yyyy") ?? "—")}\n" +
            $"Вартість опорного пункту: {Ua.Num(row.TotalValue, "#,##0.00")}";

        var detail = AppStore.Instance.StrongpointDetail(row);
        PropertyGrid.ItemsSource = detail.Lines;
        DocsGrid.ItemsSource = VrActBuilder.DocumentLinks(row);
        var anyStructure = detail.Lines.Any(l => !string.IsNullOrWhiteSpace(l.StructureName));
        PropertyGrid.Columns[0].Visibility = anyStructure ? Visibility.Visible : Visibility.Collapsed;

        var yasDocs = VrActBuilder.SourceDocuments(row);
        OpenYasBtn.IsEnabled = yasDocs.Count > 0;
        if (yasDocs.Count == 1)
            OpenYasBtn.Content = $"Акт ЯС № {DocTypeNames.FormatNumber(yasDocs[0].Number)}";
    }

    private void OpenPrimaryYasClick(object sender, RoutedEventArgs e)
    {
        var primary = VrActBuilder.PrimaryDocument(_row);
        if (primary == null)
        {
            MessageBox.Show("Акт ЯС ще не збережено.", "Обозний", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenDocument(primary.Id);
    }

    private void OpenVrClick(object sender, RoutedEventArgs e)
    {
        var primary = VrActBuilder.PrimaryDocument(_row);
        if (primary == null)
        {
            MessageBox.Show("Акт ЯС ще не збережено.", "Обозний", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenVrFor(primary);
    }

    private void OpenSelectedDocument(object sender, MouseButtonEventArgs e)
    {
        if (DocsGrid.SelectedItem is not StrongpointDocLinkRow row) return;
        if (row.IsVrAct)
        {
            if (row.DocumentId is Guid vrYasId)
            {
                var yas = AppStore.Instance.Data.Documents.FirstOrDefault(d => d.Id == vrYasId);
                if (yas != null) OpenVrFor(yas);
            }
            return;
        }
        if (row.DocumentId is Guid id)
            OpenDocument(id);
    }

    private void OpenVrFor(Document yas)
    {
        // Лише перегляд: номер/дату ВР звідси не міняємо.
        new VrActViewWindow(yas, _row) { Owner = this }.ShowDialog();
    }

    private void OpenDocument(Guid documentId)
    {
        var doc = AppStore.Instance.Data.Documents.FirstOrDefault(d => d.Id == documentId);
        if (doc == null)
        {
            MessageBox.Show("Документ не знайдено.", "Обозний", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var view = new DocumentViewWindow(doc) { Owner = this };
        if (view.ShowDialog() != true)
        {
            // Після можливих правок № ВР / дати на картці ЯС — оновити перелік документів.
            DocsGrid.ItemsSource = VrActBuilder.DocumentLinks(_row);
            return;
        }
        if (view.Deleted)
        {
            DialogResult = true;
            return;
        }
        if (view.OpenAsDraft)
        {
            DraftToOpen = view.DraftToOpen ?? doc;
            DialogResult = true;
        }
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
