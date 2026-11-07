using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Obozny;

public partial class AddPropertyWindow : Window
{
    private readonly ObservableCollection<EntryRow> _rows = new();
    private bool _growing;

    public AddPropertyWindow()
    {
        InitializeComponent();
        DocDateBox.SelectedDate = DateTime.Today;
        for (var i = 0; i < 3; i++)
            _rows.Add(new EntryRow());
        LinesGrid.ItemsSource = _rows;
        Loaded += (_, _) =>
        {
            LinesGrid.Focus();
            LinesGrid.SelectedCells.Clear();
            LinesGrid.CurrentCell = new DataGridCellInfo(_rows[0], LinesGrid.Columns[0]);
        };
    }

    private void Save(object sender, RoutedEventArgs e)
    {
        var filled = _rows.Where(r => r.HasName).ToList();
        if (filled.Count == 0)
        {
            MessageBox.Show("Додайте хоча б один рядок з найменуванням.", "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var date = DocDateBox.SelectedDate?.Date ?? DateTime.Today;
        var number = DocNumberBox.Text.Trim();
        var importRows = new List<ImportRow>();
        foreach (var row in filled)
        {
            if (string.IsNullOrWhiteSpace(row.Unit))
            {
                MessageBox.Show($"Вкажіть одиницю виміру для «{row.Name}».", "Обозний",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(row.PriceText) || !TryParseDecimal(row.PriceText, out var price))
            {
                MessageBox.Show($"Вкажіть ціну для «{row.Name}».", "Обозний",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(row.QtyText) || !TryParseDecimal(row.QtyText, out var qty) || qty <= 0)
            {
                MessageBox.Show($"Вкажіть кількість для «{row.Name}».", "Обозний",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            importRows.Add(new ImportRow
            {
                Name = row.Name.Trim(),
                Unit = row.Unit.Trim(),
                Category = (row.Category ?? "").Trim(),
                Price = price,
                Quantity = qty,
                Date = date,
                Number = number,
                Kind = ImportMoveKind.Inventory
            });
        }

        if (!ReviewAndImport(importRows, fromFile: false)) return;
        DialogResult = true;
    }

    private void ContextPaste(object sender, RoutedEventArgs e) => TryPasteFromClipboard();

    private void ContextCopy(object sender, RoutedEventArgs e)
    {
        ApplicationCommands.Copy.Execute(null, LinesGrid);
    }

    private void ContextDelete(object sender, RoutedEventArgs e) => ClearSelectedCells();

    private void ContextClearRow(object sender, RoutedEventArgs e)
    {
        var rows = LinesGrid.SelectedCells
            .Select(c => c.Item as EntryRow)
            .Where(r => r != null)
            .Distinct()
            .Cast<EntryRow>()
            .ToList();
        if (rows.Count == 0 && LinesGrid.CurrentItem is EntryRow cur)
            rows.Add(cur);
        foreach (var row in rows)
        {
            row.Name = "";
            row.Unit = "";
            row.Category = "";
            row.PriceText = "";
            row.QtyText = "";
        }
    }

    private void LinesPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Під час редагування TextBox — Delete/Back лишаємо для тексту
        var editing = e.OriginalSource is TextBox;

        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (TryPasteFromClipboard())
                e.Handled = true;
            return;
        }

        if (!editing && (e.Key == Key.Delete || e.Key == Key.Back))
        {
            ClearSelectedCells();
            e.Handled = true;
        }
    }

    private void ClearSelectedCells()
    {
        var cells = LinesGrid.SelectedCells.ToList();
        if (cells.Count == 0 && LinesGrid.CurrentCell.IsValid)
            cells.Add(LinesGrid.CurrentCell);

        foreach (var cell in cells)
        {
            if (cell.Item is not EntryRow row || cell.Column == null) continue;
            switch (cell.Column.DisplayIndex)
            {
                case 0: row.Name = ""; break;
                case 1: row.Unit = ""; break;
                case 2: row.Category = ""; break;
                case 3: row.PriceText = ""; break;
                case 4: row.QtyText = ""; break;
            }
        }
    }

    /// <summary>
    /// Якщо в буфері кілька клітинок/рядків — розкладає в таблицю.
    /// Інакше повертає false, щоб спрацювала звичайна вставка в клітинку.
    /// </summary>
    private bool TryPasteFromClipboard()
    {
        if (!Clipboard.ContainsText()) return false;
        var text = Clipboard.GetText();
        if (string.IsNullOrWhiteSpace(text)) return false;

        var hasGrid = text.Contains('\t') || text.Contains('\n') || text.Contains('\r') || text.Contains(';');
        if (!hasGrid)
            return false; // одна клітинка — хай вставить DataGrid/TextBox сам

        var parsed = ParseClipboard(text);
        if (parsed.Count == 0) return false;

        var startIndex = 0;
        if (LinesGrid.CurrentItem is EntryRow cur)
            startIndex = Math.Max(0, _rows.IndexOf(cur));

        for (var i = 0; i < parsed.Count; i++)
        {
            var targetIndex = startIndex + i;
            while (targetIndex >= _rows.Count)
                _rows.Add(new EntryRow());
            var src = parsed[i];
            var dst = _rows[targetIndex];
            dst.Name = src.Name;
            dst.Unit = src.Unit;
            dst.Category = src.Category;
            dst.PriceText = src.PriceText;
            dst.QtyText = src.QtyText;
        }
        EnsureEmptyTail();
        return true;
    }

    private static List<EntryRow> ParseClipboard(string text)
    {
        var result = new List<EntryRow>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            if (LooksLikeHeader(line)) continue;
            var parts = SplitColumns(line);
            if (parts.Length == 0 || parts.All(string.IsNullOrWhiteSpace)) continue;

            var row = new EntryRow();
            // Кладемо лише те, що скопійовано — без автозаповнення шт / 0 / 1
            if (parts.Length >= 1) row.Name = parts[0].Trim();
            if (parts.Length >= 2) row.Unit = parts[1].Trim();
            if (parts.Length >= 3) row.Category = parts[2].Trim();
            if (parts.Length >= 4) row.PriceText = parts[3].Trim();
            if (parts.Length >= 5) row.QtyText = parts[4].Trim();
            // 2 колонки: назва + кількість або назва + од.
            if (parts.Length == 2 && TryParseDecimal(parts[1], out _))
            {
                row.Unit = "";
                row.QtyText = parts[1].Trim();
            }
            // 3 колонки: назва, од., кількість
            if (parts.Length == 3 && TryParseDecimal(parts[2], out _) && !TryParseDecimal(parts[1], out _))
            {
                row.Category = "";
                row.QtyText = parts[2].Trim();
            }
            // 4 колонки: назва, од., категорія/ціна, кількість
            if (parts.Length == 4 && TryParseDecimal(parts[3], out _))
            {
                if (TryParseDecimal(parts[2], out _))
                {
                    row.Category = "";
                    row.PriceText = parts[2].Trim();
                    row.QtyText = parts[3].Trim();
                }
                else
                    row.QtyText = parts[3].Trim();
            }

            if (row.HasName) result.Add(row);
        }
        return result;
    }

    private static string[] SplitColumns(string line)
    {
        if (line.Contains('\t'))
            return line.Split('\t');
        if (line.Contains(';'))
            return line.Split(';');
        return System.Text.RegularExpressions.Regex.Split(line.Trim(), @"\s{2,}");
    }

    private static bool LooksLikeHeader(string line)
    {
        var n = line.Trim().ToLowerInvariant();
        return n.Contains("наймен") || n.Contains("назва")
               || (n.Contains("кільк") && n.Contains("ціна"))
               || n.StartsWith("name");
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

    private void LinesCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        Dispatcher.BeginInvoke(EnsureEmptyTail);
    }

    private void EnsureEmptyTail()
    {
        if (_growing) return;
        _growing = true;
        try
        {
            while (_rows.Count < 3)
                _rows.Add(new EntryRow());
            if (_rows.Count == 0 || _rows[^1].HasAny)
                _rows.Add(new EntryRow());
        }
        finally { _growing = false; }
    }

    private void ImportFile(object sender, RoutedEventArgs e)
    {
        if (!BetaLicense.TryAllowDocumentImportExport(this)) return;
        var dlg = new OpenFileDialog
        {
            Filter = "Excel і таблиці|*.xlsx;*.xlsm;*.xls;*.csv;*.txt|Excel|*.xlsx;*.xlsm;*.xls|CSV|*.csv;*.txt|Усі файли|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        List<ImportRow> rows;
        try
        {
            rows = ExcelImporter.Read(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не вдалося прочитати файл:\n" + ex.Message, "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (rows.Count == 0)
        {
            MessageBox.Show("У файлі немає рядків майна. Потрібен рядок заголовків зі стовпцями «Назва» і «Кількість». Для накладної підійде і таблиця «Назва військового майна».",
                "Обозний", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!ReviewAndImport(rows, fromFile: true)) return;
        DialogResult = true;
    }

    /// <summary>
    /// Файл з багатьма документами — одне спільне вікно; одна накладна / ручне додавання — як раніше.
    /// </summary>
    private bool ReviewAndImport(List<ImportRow> rows, bool fromFile)
    {
        var groups = ImportGrouping.Group(rows);
        if (groups.Count == 0)
        {
            MessageBox.Show("Немає рядків для збереження.", "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var confirmed = new List<ImportRow>();
        if (fromFile && groups.Count > 1)
        {
            var bulk = new ImportBulkReviewWindow(groups) { Owner = this };
            if (bulk.ShowDialog() != true)
                return false;
            confirmed.AddRange(bulk.ToImportRows());
        }
        else
        {
            foreach (var group in groups)
            {
                var dlg = new ImportReviewWindow(group, fromFile) { Owner = this };
                if (dlg.ShowDialog() != true)
                    return false;
                confirmed.AddRange(dlg.ToImportRows());
            }
        }

        if (confirmed.Count == 0)
        {
            MessageBox.Show("Немає рядків для збереження.", "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // Приймання: перед збереженням — попередження про збіг з уже прийнятим за тим самим документом.
        var dupHits = ReceiptDuplicateCheck.Find(confirmed, AppStore.Instance.Data.Documents);
        if (dupHits.Count > 0)
        {
            var ask = MessageBox.Show(
                ReceiptDuplicateCheck.BuildConfirmText(dupHits),
                "Обозний", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (ask != MessageBoxResult.Yes)
                return false;
        }

        var note = ImportGrouping.ReviewSourceNote(fromFile);
        var summary = AppStore.Instance.ImportRows(confirmed, note);
        var warn = "";
        if (summary.Warnings.Count > 0)
        {
            warn = $"\n\nУвага, списання більше залишку: {summary.Warnings.Count} раз(и).\n• " +
                   string.Join("\n• ", summary.Warnings);
            if (summary.NegativeStock > 0)
            {
                warn += $"\n\nЗараз на складі з МІНУСОМ: {summary.NegativeStock} поз. " +
                        "(червоні рядки в таблиці складу).";
            }
            else
            {
                warn += "\n\nПізніші надходження перекрили мінус — зараз від’ємних залишків немає.";
            }
        }

        if (fromFile)
        {
            var report = new TextReportWindow(
                $"Збережено рядків: {summary.Rows}\n" +
                $"Отримано: {summary.Receipts}\n" +
                $"Повернення: {summary.Returns}\n" +
                $"Переміщення: {summary.Transfers}\n" +
                $"Списання: {summary.WriteOffs}\n" +
                $"Втрати: {summary.Losses}\n" +
                $"Видача: {summary.Issues}\n" +
                $"Інвентаризація: {summary.Inventory}\n\n" +
                $"Зараз на складі позицій: {summary.OnStock}\n" +
                $"Повністю списаних: {summary.WrittenOff}" + warn,
                "Обозний")
            { Owner = this };
            report.ShowDialog();
        }
        else
        {
            MessageBox.Show(
                $"Додано позицій: {summary.Inventory}\nЗараз на складі: {summary.OnStock}",
                "Обозний", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        return true;
    }

    private void Cancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed class EntryRow : INotifyPropertyChanged
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

        public bool HasName => !string.IsNullOrWhiteSpace(Name);
        public bool HasAny =>
            HasName
            || !string.IsNullOrWhiteSpace(Unit)
            || !string.IsNullOrWhiteSpace(Category)
            || !string.IsNullOrWhiteSpace(PriceText)
            || !string.IsNullOrWhiteSpace(QtyText);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
