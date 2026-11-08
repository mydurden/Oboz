using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Obozny;

public partial class ImportBulkReviewWindow : Window
{
    private static readonly SolidColorBrush DarkOnLight = Create(0x1A, 0x1A, 0x1A);
    private static readonly SolidColorBrush UnknownBg = Create(0xFF, 0xF3, 0xCD);

    private readonly ObservableCollection<BulkLine> _lines = new();
    private bool _intentReady;

    public ImportBulkReviewWindow(IReadOnlyList<ImportDocGroup> groups)
    {
        InitializeComponent();

        foreach (var group in groups)
        {
            var number = group.Number;
            var sp = group.StrongpointName;
            var reg = group.StrongpointRegNumber;
            // якщо в номері ще лежить складений ЯС — розкласти в окремі поля
            if (string.IsNullOrWhiteSpace(sp)
                && QualityStateNumber.TryParse(number, out var act, out var name, out var r))
            {
                number = act;
                sp = name;
                reg = r;
            }

            foreach (var row in group.Rows)
            {
                var kind = row.Kind;
                // АПП у реєстрі руху: інвентаризація — помилковий fallback.
                if (ImportGrouping.LooksLikeAppNumber(number) && kind == ImportMoveKind.Inventory)
                    kind = ImportMoveKind.Unknown;
                var kindLabel = KindLabel(kind, group.Type, number);
                var supply = SupplyServiceHelper.Normalize(row.SupplyService)
                             ?? SupplyServiceHelper.MapFromPeredaye(row.Peredaye)
                             ?? "";
                _lines.Add(new BulkLine
                {
                    DateText = group.Date.ToString("dd.MM.yyyy"),
                    Kind = kind,
                    OriginalKind = kind,
                    KindText = kindLabel,
                    OriginalKindText = kindLabel,
                    Operation = row.Operation,
                    OriginalOperation = row.Operation,
                    Number = number,
                    StrongpointName = sp,
                    StrongpointReg = reg,
                    Name = row.Name,
                    Unit = row.Unit,
                    Category = row.Category,
                    PriceText = row.Price > 0 ? Ua.Num(row.Price, "#,##0.####") : "",
                    QtyText = Ua.Num(row.Quantity, "#,##0.###"),
                    Note = row.Note,
                    SupplyService = supply,
                    GroupKey = $"{group.Date:yyyyMMdd}|{number}|{group.Type}|{reg}|{sp}"
                });
            }
        }

        LinesGrid.ItemsSource = _lines;
        SummaryText.Text = "";
        SummaryText.Visibility = Visibility.Collapsed;
        FillIntent(ImportPosting.Guess(groups.SelectMany(g => g.Rows)));
        FillSupplyAll();
    }

    public List<ImportRow> ToImportRows()
    {
        var result = new List<ImportRow>();
        foreach (var line in _lines)
        {
            if (string.IsNullOrWhiteSpace(line.Name)) continue;
            if (!TryParseDecimal(line.QtyText, out var qty) || qty <= 0) continue;
            TryParseDecimal(line.PriceText, out var price);
            var date = ParseDate(line.DateText) ?? DateTime.Today;
            result.Add(new ImportRow
            {
                Date = date,
                Number = (line.Number ?? "").Trim(),
                Name = line.Name.Trim(),
                Unit = string.IsNullOrWhiteSpace(line.Unit) ? "шт" : line.Unit.Trim(),
                Category = (line.Category ?? "").Trim(),
                Price = price,
                Quantity = qty,
                Kind = line.Kind,
                Operation = line.Operation,
                StrongpointName = (line.StrongpointName ?? "").Trim(),
                StrongpointRegNumber = (line.StrongpointReg ?? "").Trim(),
                Note = (line.Note ?? "").Trim(),
                SupplyService = SupplyServiceHelper.Normalize(line.SupplyService) ?? ""
            });
        }
        return result;
    }

    private bool _supplyAllReady;

    private void FillSupplyAll()
    {
        _supplyAllReady = false;
        SupplyAllBox.Items.Clear();
        SupplyAllBox.Items.Add("");
        foreach (var s in SupplyServiceHelper.All)
            SupplyAllBox.Items.Add(s);
        var common = _lines
            .Select(l => SupplyServiceHelper.Normalize(l.SupplyService) ?? "")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var pick = common.Count == 1 ? common[0] : "";
        SupplyAllBox.SelectedItem = SupplyAllBox.Items.OfType<string>().FirstOrDefault(x => x == pick) ?? "";
        _supplyAllReady = true;
    }

    private void SupplyAllChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_supplyAllReady) return;
        var chosen = SupplyServiceHelper.Normalize(SupplyAllBox.SelectedItem as string) ?? "";
        foreach (var line in _lines)
            line.SupplyService = chosen;
    }

    private static SolidColorBrush Create(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>Підпис типу руху (не назва документа).</summary>
    private static string KindLabel(ImportMoveKind kind, DocType type, string? documentNumber = null)
    {
        if (ImportGrouping.LooksLikeAppNumber(documentNumber) && kind == ImportMoveKind.Inventory)
            return MoveKindNames.Unknown;
        return MoveKindNames.ForUi(kind, type);
    }

    private void OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is not BulkLine line)
        {
            e.Row.Background = Brushes.Transparent;
            return;
        }

        if (line.Kind == ImportMoveKind.Unknown || MoveKindNames.IsUnknown(line.KindText))
        {
            e.Row.Background = UnknownBg;
            e.Row.Foreground = DarkOnLight;
            return;
        }

        var idx = _lines.IndexOf(line);
        var prevKey = idx > 0 ? _lines[idx - 1].GroupKey : null;
        if (idx == 0 || prevKey != line.GroupKey)
        {
            // Світла смуга документа: текст завжди темний, інакше в темній темі його не видно.
            e.Row.Background = new SolidColorBrush(Color.FromRgb(0xE3, 0xF2, 0xE8));
            e.Row.Foreground = DarkOnLight;
        }
        else
        {
            e.Row.Background = Brushes.Transparent;
            e.Row.Foreground = (Brush)FindResource("TextPrimary");
        }
    }

    private void KindComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { DataContext: BulkLine line } box) return;
        if (box.SelectedItem is not string label) return;
        line.KindText = label;
        line.Kind = MoveKindNames.ToImportKind(label);
        // Перемалювати підсвітку після зміни типу.
        var idx = _lines.IndexOf(line);
        if (idx >= 0 && LinesGrid.ItemContainerGenerator.ContainerFromIndex(idx) is DataGridRow row)
            OnLoadingRow(LinesGrid, new DataGridRowEventArgs(row));
    }

    private void DeleteRowClick(object sender, RoutedEventArgs e)
    {
        if (LinesGrid.SelectedItem is not BulkLine line) return;
        _lines.Remove(line);
        SummaryText.Text = "";
        SummaryText.Visibility = Visibility.Collapsed;
    }

    private void FillIntent(string selected)
    {
        IntentBox.Items.Clear();
        foreach (var (id, title) in ImportPosting.Choices)
            IntentBox.Items.Add(new ComboBoxItem { Content = title, Tag = id });
        IntentBox.SelectedItem = IntentBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => Equals(i.Tag, selected))
            ?? IntentBox.Items.OfType<ComboBoxItem>()
                .First(i => Equals(i.Tag, ImportPosting.Register));
        _intentReady = true;
        ApplyIntent();
    }

    private void IntentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_intentReady) ApplyIntent();
    }

    private void ApplyIntent()
    {
        if (IntentBox.SelectedItem is not ComboBoxItem item) return;
        var intent = item.Tag as string ?? ImportPosting.Register;
        foreach (var line in _lines)
        {
            var probe = new ImportRow();
            ImportPosting.Apply(probe, intent, line.OriginalKind, line.OriginalOperation);
            line.Kind = probe.Kind;
            line.Operation = probe.Operation;
            line.KindText = MoveKindNames.Sanitize(
                ImportPosting.Label(intent, line.OriginalKind, line.OriginalOperation, line.OriginalKindText));
            // «Реєстр» лишає оригінал: якщо був Unknown — лишається Unknown.
            if (intent == ImportPosting.Register && line.OriginalKind == ImportMoveKind.Unknown)
            {
                line.Kind = ImportMoveKind.Unknown;
                line.KindText = MoveKindNames.Unknown;
            }
        }
        LinesGrid.Items.Refresh();
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

        var unknown = rows.Count(r => r.Kind == ImportMoveKind.Unknown);
        if (unknown > 0)
        {
            MessageBox.Show(
                $"Оберіть тип руху ({unknown}).",
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

    private static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateTime.TryParseExact(text.Trim(), new[] { "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.Date;
        if (DateTime.TryParse(text, new CultureInfo("uk-UA"), DateTimeStyles.None, out d))
            return d.Date;
        return null;
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

    private sealed class BulkLine : INotifyPropertyChanged
    {
        private string _dateText = "";
        private string _number = "";
        private string _strongpointName = "";
        private string _strongpointReg = "";
        private string _name = "";
        private string _unit = "";
        private string _category = "";
        private string _priceText = "";
        private string _qtyText = "";
        private string _kindText = "";
        private string _supplyService = "";

        public string DateText
        {
            get => _dateText;
            set { if (_dateText == value) return; _dateText = value; OnChanged(); }
        }

        public string KindText
        {
            get => _kindText;
            set
            {
                var sanitized = MoveKindNames.Sanitize(value);
                if (_kindText == sanitized) return;
                _kindText = sanitized;
                Kind = MoveKindNames.ToImportKind(sanitized);
                OnChanged();
            }
        }

        public IReadOnlyList<string> KindChoices { get; } = BuildKindChoices();

        private static IReadOnlyList<string> BuildKindChoices()
        {
            var list = new List<string> { MoveKindNames.Unknown };
            list.AddRange(MoveKindNames.PickableKinds);
            return list;
        }

        public string OriginalKindText { get; set; } = "";
        public ImportMoveKind Kind { get; set; }
        public ImportMoveKind OriginalKind { get; set; }
        public string Operation { get; set; } = "";
        public string OriginalOperation { get; set; } = "";
        public string Number
        {
            get => _number;
            set { if (_number == value) return; _number = value; OnChanged(); }
        }
        public string StrongpointName
        {
            get => _strongpointName;
            set { if (_strongpointName == value) return; _strongpointName = value; OnChanged(); }
        }
        public string StrongpointReg
        {
            get => _strongpointReg;
            set { if (_strongpointReg == value) return; _strongpointReg = value; OnChanged(); }
        }
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
        public string Note { get; set; } = "";
        public string GroupKey { get; set; } = "";

        public IReadOnlyList<string> SupplyChoices { get; } = BuildSupplyChoices();

        private static IReadOnlyList<string> BuildSupplyChoices()
        {
            var list = new List<string> { "" };
            list.AddRange(SupplyServiceHelper.All);
            return list;
        }

        public string SupplyService
        {
            get => _supplyService;
            set
            {
                var v = SupplyServiceHelper.Normalize(value) ?? (value ?? "").Trim();
                if (!SupplyServiceHelper.IsKnown(v)) v = "";
                if (_supplyService == v) return;
                _supplyService = v;
                OnChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
