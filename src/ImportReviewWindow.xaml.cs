using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Obozny;

public partial class ImportReviewWindow : Window
{
    private readonly ImportDocGroup _group;
    private readonly bool _fromFile;
    private readonly ObservableCollection<ReviewLine> _lines = new();
    private bool _intentReady;

    public ImportReviewWindow(ImportDocGroup group, bool fromFile)
    {
        _group = group;
        _fromFile = fromFile;
        InitializeComponent();

        var title = ImportGrouping.ReviewTitle(group, fromFile);
        Title = title;
        TitleText.Text = title;
        NumberBox.Text = group.Number;
        DateBox.SelectedDate = group.Date;
        IntentPanel.Visibility = fromFile ? Visibility.Visible : Visibility.Collapsed;
        FillSupply(group.Rows.Select(r => r.SupplyService).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
                   ?? group.Rows.Select(r => r.Peredaye).FirstOrDefault() ?? "");

        var showCategory = group.Type == DocType.Inventory || group.Kind == ImportMoveKind.Inventory;
        var showPrice = showCategory
                        || group.Type == DocType.Receipt
                        || group.Kind is ImportMoveKind.Receipt or ImportMoveKind.Return;
        CategoryCol.Visibility = showCategory ? Visibility.Visible : Visibility.Collapsed;
        PriceCol.Visibility = showPrice ? Visibility.Visible : Visibility.Collapsed;

        foreach (var row in group.Rows)
        {
            _lines.Add(new ReviewLine
            {
                Name = row.Name,
                Unit = row.Unit,
                Category = row.Category,
                PriceText = row.Price > 0 ? Ua.Num(row.Price, "#,##0.####") : "",
                QtyText = Ua.Num(row.Quantity, "#,##0.###"),
                Kind = row.Kind,
                OriginalKind = row.Kind,
                Operation = row.Operation,
                OriginalOperation = row.Operation,
                Note = row.Note
            });
        }
        LinesGrid.ItemsSource = _lines;
        if (fromFile)
        {
            FillIntent(ImportPosting.Guess(group.Rows));
            // Не в ctor: MessageBox до ShowDialog вікна опиняється позаду й виглядає як «зависання».
            if (group.Rows.Any(r => r.Kind == ImportMoveKind.Unknown)
                || _lines.Any(l => l.Kind == ImportMoveKind.Unknown))
            {
                Loaded += WarnUnknownKindOnce;
            }
        }
    }

    private void WarnUnknownKindOnce(object sender, RoutedEventArgs e)
    {
        Loaded -= WarnUnknownKindOnce;
        MessageBox.Show(
            this,
            "Тип руху для цього документа невизначений.\n\n" +
            "Оберіть намір у списку нижче (отримання, повернення, переміщення, інвентаризація) — " +
            "не залишайте невизначений тип.",
            "Обозний", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public List<ImportRow> ToImportRows()
    {
        var date = DateBox.SelectedDate?.Date ?? _group.Date;
        var number = NumberBox.Text.Trim();
        var supply = SupplyServiceHelper.Normalize(SupplyBox.SelectedItem as string) ?? "";
        var result = new List<ImportRow>();
        foreach (var line in _lines)
        {
            if (string.IsNullOrWhiteSpace(line.Name)) continue;
            if (!TryParseDecimal(line.QtyText, out var qty) || qty <= 0) continue;
            TryParseDecimal(line.PriceText, out var price);
            result.Add(new ImportRow
            {
                Date = date,
                Number = number,
                Name = line.Name.Trim(),
                Unit = string.IsNullOrWhiteSpace(line.Unit) ? "шт" : line.Unit.Trim(),
                Category = (line.Category ?? "").Trim(),
                Price = price,
                Quantity = qty,
                Kind = line.Kind,
                Operation = line.Operation,
                StrongpointName = _group.StrongpointName,
                StrongpointRegNumber = _group.StrongpointRegNumber,
                Note = line.Note,
                SupplyService = supply
            });
        }
        return result;
    }

    private void FillSupply(string? hint)
    {
        SupplyBox.Items.Clear();
        SupplyBox.Items.Add("");
        foreach (var s in SupplyServiceHelper.All)
            SupplyBox.Items.Add(s);
        var mapped = SupplyServiceHelper.Normalize(hint)
                     ?? SupplyServiceHelper.MapFromPeredaye(hint)
                     ?? "";
        SupplyBox.SelectedItem = SupplyBox.Items.OfType<string>().FirstOrDefault(x => x == mapped) ?? "";
    }

    private void DeleteRowClick(object sender, RoutedEventArgs e)
    {
        if (LinesGrid.SelectedItem is not ReviewLine line) return;
        _lines.Remove(line);
    }

    private void FillIntent(string selected)
    {
        IntentBox.Items.Clear();
        foreach (var (id, title) in ImportPosting.Choices)
            IntentBox.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = title, Tag = id });
        IntentBox.SelectedItem = IntentBox.Items.OfType<System.Windows.Controls.ComboBoxItem>()
            .FirstOrDefault(i => Equals(i.Tag, selected))
            ?? IntentBox.Items.OfType<System.Windows.Controls.ComboBoxItem>()
                .First(i => Equals(i.Tag, ImportPosting.Register));
        _intentReady = true;
        ApplyIntent();
    }

    private void IntentChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_intentReady) ApplyIntent();
    }

    private void ApplyIntent()
    {
        if (IntentBox.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
        var intent = item.Tag as string ?? ImportPosting.Register;
        foreach (var line in _lines)
        {
            var probe = new ImportRow();
            ImportPosting.Apply(probe, intent, line.OriginalKind, line.OriginalOperation);
            line.Kind = probe.Kind;
            line.Operation = probe.Operation;
        }

        if (intent is ImportPosting.Accept or ImportPosting.Surrender or ImportPosting.Transfer)
        {
            _group.Kind = intent switch
            {
                ImportPosting.Accept => ImportMoveKind.Receipt,
                ImportPosting.Surrender => ImportMoveKind.Return,
                _ => ImportMoveKind.Transfer
            };
            _group.Type = ImportGrouping.TypeOf(_group.Kind);
        }

        var title = ImportGrouping.ReviewTitleForIntent(intent, _group, _fromFile);
        Title = title;
        TitleText.Text = title;
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ConfirmClick(object sender, RoutedEventArgs e)
    {
        var rows = ToImportRows();
        if (rows.Count == 0)
        {
            MessageBox.Show("Немає жодного коректного рядка з кількістю.", "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (rows.Any(r => r.Kind == ImportMoveKind.Unknown))
        {
            MessageBox.Show(
                "Тип руху невизначений.\n\n" +
                "Оберіть намір файлу (отримання, повернення, переміщення або інвентаризація). " +
                "Не можна проводити навмання.",
                "Обозний", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Unit))
            {
                MessageBox.Show($"Вкажіть одиницю виміру для «{row.Name}».", "Обозний",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        DialogResult = true;
    }

    private static bool TryParseDecimal(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().Replace('\u00A0', ' ').Replace(" ", "");
        if (decimal.TryParse(t, NumberStyles.Any, Ua.Culture, out value)) return true;
        t = t.Replace(',', '.');
        return decimal.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private sealed class ReviewLine : INotifyPropertyChanged
    {
        private string _name = "";
        private string _unit = "";
        private string _category = "";
        private string _priceText = "";
        private string _qtyText = "";

        public string Name
        {
            get => _name;
            set { if (_name == value) return; _name = value; OnChanged(); }
        }
        public string Unit
        {
            get => _unit;
            set { if (_unit == value) return; _unit = value; OnChanged(); }
        }
        public string Category
        {
            get => _category;
            set { if (_category == value) return; _category = value; OnChanged(); }
        }
        public string PriceText
        {
            get => _priceText;
            set { if (_priceText == value) return; _priceText = value; OnChanged(); }
        }
        public string QtyText
        {
            get => _qtyText;
            set { if (_qtyText == value) return; _qtyText = value; OnChanged(); }
        }
        public ImportMoveKind Kind { get; set; }
        public ImportMoveKind OriginalKind { get; set; }
        public string Operation { get; set; } = "";
        public string OriginalOperation { get; set; } = "";
        public string Note { get; set; } = "";

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
