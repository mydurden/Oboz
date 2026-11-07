using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Obozny;

public partial class PropertyCardWindow : Window
{
    private readonly Guid _propertyId;
    private bool _supplyReady;
    public Document? DraftToOpen { get; private set; }

    public PropertyCardWindow(PropertyItem item)
    {
        InitializeComponent();
        _propertyId = item.Id;
        Bind(item);
    }

    private void Bind(PropertyItem item)
    {
        var missingNom = item.NomenclatureId == null
                         || AppStore.Instance.GetNomenclature(item.NomenclatureId) == null;
        var nom = AppStore.Instance.EnsurePropertyNomenclature(item);
        if (missingNom) AppStore.Instance.Save(false);
        TitleText.Text = item.Name;
        var kind = NomenclatureKindNames.Of(nom.Kind);
        MetaText.Text =
            $"Одиниця: {item.Unit}\nКатегорія: {item.Category}\nТип майна: {kind}\nЦіна: {Ua.FormatPrice(item.UnitPrice)}";
        StockText.Text = $"На складі: {Ua.FormatQty(item.Quantity)}";
        ReservedText.Text = $"Зарезервовано: {Ua.FormatQty(item.ReservedQuantity)}";
        if (item.ReservedQuantity > 0.0001m)
        {
            ReservedText.Foreground = item.Quantity <= 0.0001m
                ? new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28))
                : new SolidColorBrush(Color.FromRgb(0xEF, 0x6C, 0x00));
        }
        else
            ReservedText.Foreground = (Brush)FindResource("TextPrimary");

        FillSupply(item.SupplyService);
        DocsGrid.ItemsSource = BuildRows(item.Id);
    }

    private void FillSupply(string? current)
    {
        _supplyReady = false;
        SupplyBox.Items.Clear();
        SupplyBox.Items.Add("");
        foreach (var s in SupplyServiceHelper.All)
            SupplyBox.Items.Add(s);
        var norm = SupplyServiceHelper.Normalize(current) ?? "";
        SupplyBox.SelectedItem = SupplyBox.Items.OfType<string>().FirstOrDefault(x => x == norm) ?? "";
        _supplyReady = true;
    }

    private void SupplyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_supplyReady) return;
        var item = AppStore.Instance.Data.Properties.FirstOrDefault(p => p.Id == _propertyId);
        if (item == null) return;
        var chosen = SupplyBox.SelectedItem as string ?? "";
        var norm = SupplyServiceHelper.Normalize(chosen) ?? "";
        if (string.Equals(item.SupplyService ?? "", norm, StringComparison.Ordinal)) return;
        item.SupplyService = norm;
        AppStore.Instance.Save(false);
    }

    private void OpenNomenclatureClick(object sender, RoutedEventArgs e)
    {
        var item = AppStore.Instance.Data.Properties.FirstOrDefault(p => p.Id == _propertyId);
        if (item == null) return;
        var nom = AppStore.Instance.EnsurePropertyNomenclature(item);
        AppStore.Instance.Save(false);
        var card = new NomenclatureCardWindow(nom) { Owner = this };
        if (card.ShowDialog() != true) return;
        item = AppStore.Instance.Data.Properties.FirstOrDefault(p => p.Id == _propertyId);
        if (item != null) Bind(item);
    }

    private static List<MovementDocRow> BuildRows(Guid propertyId)
    {
        var posted = AppStore.Instance.Data.Movements
            .Where(m => m.PropertyId == propertyId)
            .Select(m => new MovementDocRow(
                m.DocumentId,
                m.DocumentDate,
                m.DocumentDate.ToString("dd.MM.yyyy"),
                MoveKindNames.Of(m),
                m.DocumentNumber,
                Ua.FormatQty(Math.Abs(m.QuantityDelta)),
                IsDraft: false));

        var drafts = AppStore.Instance.Data.Documents
            .Where(d => d.Status == DocStatus.Draft)
            .SelectMany(d => d.Lines
                .Where(l => l.PropertyId == propertyId)
                .Select(l => new MovementDocRow(
                        d.Id,
                        d.Date,
                        d.Date.ToString("dd.MM.yyyy"),
                        $"{MoveKindNames.Of(d.Type)} (чернетка)",
                        d.Number,
                        Ua.FormatQty(l.Quantity),
                        IsDraft: true)));

        return posted.Concat(drafts)
            .OrderByDescending(r => r.SortDate)
            .ThenBy(r => r.IsDraft ? 0 : 1)
            .ToList();
    }

    private void OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is MovementDocRow { IsDraft: true })
            e.Row.Foreground = (Brush)Application.Current.Resources["Link"]!;
        else
            e.Row.ClearValue(Control.ForegroundProperty);
    }

    private void OpenSelectedDocument(object sender, MouseButtonEventArgs e)
    {
        if (DocsGrid.SelectedItem is MovementDocRow row)
            OpenDocument(row.DocumentId);
    }

    private void OpenDocument(Guid documentId)
    {
        var doc = AppStore.Instance.Data.Documents.FirstOrDefault(d => d.Id == documentId);
        if (doc == null)
        {
            MessageBox.Show("Документ не знайдено.", "Обозний", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (doc.Status == DocStatus.Draft)
        {
            DraftToOpen = doc;
            DialogResult = true;
            return;
        }

        var view = new DocumentViewWindow(doc, _propertyId) { Owner = this };
        if (view.ShowDialog() != true) return;
        if (view.Deleted)
        {
            DialogResult = true;
            return;
        }
        if (view.OpenAsDraft)
        {
            DraftToOpen = doc;
            DialogResult = true;
            return;
        }

        var item = AppStore.Instance.Data.Properties.FirstOrDefault(p => p.Id == _propertyId);
        if (item != null) Bind(item);
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private sealed record MovementDocRow(
        Guid DocumentId,
        DateTime SortDate,
        string DateText,
        string MoveKind,
        string DocumentNumber,
        string QuantityText,
        bool IsDraft);
}
