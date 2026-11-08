using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Obozny;

public partial class BulkDisplayUomWindow : Window
{
    private readonly List<DisplayUomRow> _all = new();
    private readonly ObservableCollection<DisplayUomRow> _visible = new();
    private bool _ready;
    private bool _applyingFilter;
    private bool _filterSyncQueued;

    public BulkDisplayUomWindow()
    {
        InitializeComponent();
        AppStore.Instance.EnsureNomenclatures();
        foreach (var n in AppStore.Instance.Data.Nomenclatures
                     .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(x => x.Unit, StringComparer.CurrentCultureIgnoreCase))
        {
            _all.Add(DisplayUomRow.FromItem(n));
        }
        RowsGrid.ItemsSource = _visible;
        Loaded += (_, _) => QueueFilterColumnSync();
        SizeChanged += (_, _) => QueueFilterColumnSync();
        RowsGrid.SizeChanged += (_, _) => QueueFilterColumnSync();
        // ActualWidth колонок змінюється при resize / появі V-скролу без SizeChanged гріда.
        RowsGrid.LayoutUpdated += (_, _) => QueueFilterColumnSync();
        _ready = true;
        ApplyFilter();
    }

    private void QueueFilterColumnSync()
    {
        if (_filterSyncQueued) return;
        _filterSyncQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _filterSyncQueued = false;
            SyncFilterColumnWidths();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Кожне поле фільтра = ActualWidth відповідної колонки; залишок ширини DataGrid — жолоб під V-скрол.
    /// </summary>
    private void SyncFilterColumnWidths()
    {
        var cols = RowsGrid.Columns;
        if (cols.Count == 0 || FilterGrid.ColumnDefinitions.Count < cols.Count + 1) return;

        var widths = new double[cols.Count];
        for (var i = 0; i < cols.Count; i++)
            widths[i] = cols[i].ActualWidth;

        var gutter = ComputeFilterGutter(RowsGrid.ActualWidth, widths);
        ApplyFilterColumnPixels(FilterGrid, widths, gutter);
    }

    /// <summary>Жолоб = ширина гріда мінус сума ActualWidth колонок (0, якщо ще немає layout).</summary>
    internal static double ComputeFilterGutter(double gridActualWidth, IReadOnlyList<double> columnActualWidths)
    {
        if (gridActualWidth < 1) return 0;
        double sum = 0;
        for (var i = 0; i < columnActualWidths.Count; i++)
        {
            var w = columnActualWidths[i];
            if (w > 0) sum += w;
        }
        return Math.Max(0, gridActualWidth - sum);
    }

    /// <summary>Виставляє абсолютні ширини колонок фільтра + останню колонку-жолоб.</summary>
    internal static void ApplyFilterColumnPixels(Grid filterGrid, IReadOnlyList<double> columnActualWidths, double gutter)
    {
        if (filterGrid.ColumnDefinitions.Count < columnActualWidths.Count + 1) return;
        for (var i = 0; i < columnActualWidths.Count; i++)
        {
            var w = columnActualWidths[i];
            if (w < 1) continue;
            SetAbsoluteColumnWidth(filterGrid.ColumnDefinitions[i], w);
        }
        SetAbsoluteColumnWidth(filterGrid.ColumnDefinitions[columnActualWidths.Count], gutter);
    }

    private static void SetAbsoluteColumnWidth(ColumnDefinition def, double pixels)
    {
        var cur = def.Width;
        if (cur.IsAbsolute && Math.Abs(cur.Value - pixels) < 0.5) return;
        def.Width = new GridLength(pixels);
    }

    private void FilterChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _applyingFilter) return;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _applyingFilter = true;
        try
        {
            var sorts = CaptureSortDescriptions();
            var nameQ = (FilterName.Text ?? "").Trim().ToLowerInvariant();
            var vydQ = (FilterVyd.Text ?? "").Trim().ToLowerInvariant();
            var unitQ = (FilterUnit.Text ?? "").Trim().ToLowerInvariant();
            var dispQ = (FilterDisplay.Text ?? "").Trim().ToLowerInvariant();
            var factorQ = (FilterFactor.Text ?? "").Trim().ToLowerInvariant();

            IEnumerable<DisplayUomRow> src = _all;
            if (nameQ.Length > 0)
                src = src.Where(r => r.Name.ToLowerInvariant().Contains(nameQ));
            if (vydQ.Length > 0)
                src = src.Where(r => r.VydText.ToLowerInvariant().Contains(vydQ));
            if (unitQ.Length > 0)
                src = src.Where(r => r.Unit.ToLowerInvariant().Contains(unitQ));
            if (dispQ.Length > 0)
                src = src.Where(r => r.SelectedDisplayUnit.ToLowerInvariant().Contains(dispQ));
            if (factorQ.Length > 0)
                src = src.Where(r => r.FactorText.ToLowerInvariant().Contains(factorQ));

            _visible.Clear();
            foreach (var row in src)
                _visible.Add(row);

            RestoreSortDescriptions(sorts);
            SummaryText.Text = $"Показано {_visible.Count} з {_all.Count}";
            PaintFilterBoxes();
        }
        finally
        {
            _applyingFilter = false;
        }
    }

    private void PaintFilterBoxes()
    {
        PaintOne(FilterName);
        PaintOne(FilterVyd);
        PaintOne(FilterUnit);
        PaintOne(FilterDisplay);
        PaintOne(FilterFactor);
    }

    private static void PaintOne(TextBox box)
    {
        if (string.IsNullOrWhiteSpace(box.Text))
            box.ClearValue(Control.BackgroundProperty);
        else
            box.Background = (System.Windows.Media.Brush)box.FindResource("FilterActive");
    }

    private List<SortDescription> CaptureSortDescriptions()
    {
        var view = CollectionViewSource.GetDefaultView(RowsGrid.ItemsSource);
        if (view == null || view.SortDescriptions.Count == 0)
            return new List<SortDescription>();
        return view.SortDescriptions.ToList();
    }

    private void RestoreSortDescriptions(IReadOnlyList<SortDescription> sorts)
    {
        foreach (var col in RowsGrid.Columns)
            col.SortDirection = null;
        if (sorts.Count == 0) return;
        var view = CollectionViewSource.GetDefaultView(RowsGrid.ItemsSource);
        if (view == null) return;
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            foreach (var s in sorts)
            {
                view.SortDescriptions.Add(s);
                var col = RowsGrid.Columns.FirstOrDefault(c =>
                    string.Equals(c.SortMemberPath, s.PropertyName, StringComparison.Ordinal));
                if (col != null)
                    col.SortDirection = s.Direction;
            }
        }
    }

    private void RowsPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_ready) return;
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Tab))
            return;

        // Відкритий список Combo — стрілки лишаємо йому.
        if (FindAncestor<ComboBox>(e.OriginalSource as DependencyObject) is { IsDropDownOpen: true })
            return;

        if (!TryResolveCell(e.OriginalSource as DependencyObject, out var rowIndex, out var colIndex))
            return;

        if (e.OriginalSource is TextBox tb
            && !ShouldNavigateFromTextBox(e.Key, tb.Text?.Length ?? 0, tb.CaretIndex, tb.SelectionLength))
            return;

        var colCount = RowsGrid.Columns.Count;
        var rowCount = RowsGrid.Items.Count;
        if (colCount == 0 || rowCount == 0) return;

        int dRow = 0, dCol = 0;
        var wrap = false;
        switch (e.Key)
        {
            case Key.Left: dCol = -1; break;
            case Key.Right: dCol = 1; break;
            case Key.Up: dRow = -1; break;
            case Key.Down: dRow = 1; break;
            case Key.Tab:
                wrap = true;
                dCol = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? -1 : 1;
                break;
            default:
                return;
        }

        if (!TryComputeNextCell(rowIndex, colIndex, dRow, dCol, rowCount, colCount, wrap,
                out var nextRow, out var nextCol))
            return;

        if (FocusCell(nextRow, nextCol))
            e.Handled = true;
    }

    /// <summary>
    /// Стрілки в TextBox: усередині тексту / при виділенні — caret; лише caret на краю без виділення — вихід.
    /// Up/Down/Tab завжди навігація.
    /// </summary>
    internal static bool ShouldNavigateFromTextBox(
        Key key, int textLength, int caretIndex, int selectionLength)
    {
        if (key is Key.Up or Key.Down or Key.Tab)
            return true;
        if (key is not (Key.Left or Key.Right))
            return false;

        // Є виділення — спочатку згорнути caret у TextBox, не стрибати в іншу клітинку.
        if (selectionLength > 0)
            return false;

        textLength = Math.Max(0, textLength);
        var caret = Math.Clamp(caretIndex, 0, textLength);
        if (key == Key.Left)
            return caret <= 0;
        return caret >= textLength;
    }

    /// <summary>
    /// Наступна клітинка. Без wrap — стоп на краю. З wrap (Tab) — перехід на сусідній рядок.
    /// </summary>
    internal static bool TryComputeNextCell(
        int row, int col, int dRow, int dCol,
        int rowCount, int colCount, bool wrap,
        out int nextRow, out int nextCol)
    {
        nextRow = row;
        nextCol = col;
        if (rowCount <= 0 || colCount <= 0) return false;
        if (row < 0 || row >= rowCount || col < 0 || col >= colCount) return false;

        if (wrap && dCol != 0 && dRow == 0)
        {
            var linear = row * colCount + col + dCol;
            if (linear < 0 || linear >= rowCount * colCount) return false;
            nextRow = linear / colCount;
            nextCol = linear % colCount;
            return nextRow != row || nextCol != col;
        }

        nextRow = row + dRow;
        nextCol = col + dCol;
        if (nextRow < 0 || nextRow >= rowCount || nextCol < 0 || nextCol >= colCount)
            return false;
        return true;
    }

    private bool TryResolveCell(DependencyObject? source, out int rowIndex, out int colIndex)
    {
        rowIndex = -1;
        colIndex = -1;
        var cell = FindAncestor<DataGridCell>(source);
        if (cell == null) return false;

        colIndex = cell.Column?.DisplayIndex ?? -1;
        if (colIndex < 0) return false;

        var row = FindAncestor<DataGridRow>(cell);
        if (row?.Item == null) return false;
        rowIndex = RowsGrid.Items.IndexOf(row.Item);
        return rowIndex >= 0;
    }

    private bool FocusCell(int rowIndex, int colIndex)
    {
        if (rowIndex < 0 || rowIndex >= RowsGrid.Items.Count) return false;
        if (colIndex < 0 || colIndex >= RowsGrid.Columns.Count) return false;

        var item = RowsGrid.Items[rowIndex];
        var column = RowsGrid.Columns[colIndex];
        RowsGrid.ScrollIntoView(item, column);
        RowsGrid.UpdateLayout();

        var rowContainer = RowsGrid.ItemContainerGenerator.ContainerFromIndex(rowIndex) as DataGridRow
            ?? RowsGrid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
        if (rowContainer == null)
        {
            RowsGrid.ScrollIntoView(item, column);
            RowsGrid.UpdateLayout();
            rowContainer = RowsGrid.ItemContainerGenerator.ContainerFromIndex(rowIndex) as DataGridRow
                ?? RowsGrid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
        }
        if (rowContainer == null) return false;

        rowContainer.UpdateLayout();
        var cell = GetCell(rowContainer, colIndex);
        if (cell == null) return false;

        RowsGrid.CurrentCell = new DataGridCellInfo(item, column);
        RowsGrid.SelectedItem = item;

        var focusTarget = FindFocusableInCell(cell) ?? (UIElement)cell;
        return focusTarget.Focus();
    }

    private static DataGridCell? GetCell(DataGridRow row, int columnIndex)
    {
        var presenter = FindVisualChild<DataGridCellsPresenter>(row);
        if (presenter == null)
        {
            row.ApplyTemplate();
            presenter = FindVisualChild<DataGridCellsPresenter>(row);
        }
        if (presenter == null) return null;
        presenter.UpdateLayout();
        return presenter.ItemContainerGenerator.ContainerFromIndex(columnIndex) as DataGridCell;
    }

    private static UIElement? FindFocusableInCell(DataGridCell cell)
    {
        if (cell.Content is UIElement direct
            && (direct is TextBox or ComboBox))
            return direct;
        return FindVisualChild<TextBox>(cell) as UIElement
            ?? FindVisualChild<ComboBox>(cell);
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        for (var d = start; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is T match) return match;
        }
        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var nested = FindVisualChild<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }

    private void DisplayUnitComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (sender is not ComboBox box) return;
        if (box.DataContext is not DisplayUomRow row) return;
        if (box.SelectedItem is not string s) return;
        if (!VydDisplayUnitHelper.IsAddItem(s)) return;

        box.IsDropDownOpen = false;
        Dispatcher.BeginInvoke(new Action(() => HandleAddDisplayUnit(row)));
    }

    private void HandleAddDisplayUnit(DisplayUomRow row)
    {
        var vyd = (row.VydRaw ?? "").Trim();
        if (NomenclatureVydCatalog.IsNewItem(vyd)) vyd = "";
        // Без Виду — те саме, що на картці: діалог дозволений; у каталог виду
        // не пишемо (AddDisplayUnitForVyd → null); О.В. лишається на рядку.
        var already = AppStore.Instance.GetDisplayUnitsForVyd(vyd);
        var dlg = new VydDisplayUnitPickWindow(vyd, AppStore.Instance.Data.Nomenclatures, already)
        {
            Owner = this
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ChosenUnit))
        {
            var unit = AppStore.Instance.AddDisplayUnitForVyd(vyd, dlg.ChosenUnit);
            var chosen = unit ?? dlg.ChosenUnit.Trim();
            if (vyd.Length > 0)
                RefreshChoicesForVyd(vyd, preferSelectId: row.Id, preferUnit: chosen);
            else
                row.RebuildChoices(chosen);
        }
        else
            row.SelectedDisplayUnit = "";
    }

    private void RefreshChoicesForVyd(string vyd, Guid preferSelectId, string preferUnit)
    {
        foreach (var r in _all)
        {
            if (!string.Equals(r.VydRaw, vyd, StringComparison.Ordinal)) continue;
            var keep = r.Id == preferSelectId ? preferUnit : r.SelectedDisplayUnit;
            r.RebuildChoices(keep);
        }
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var updates = new List<(Guid Id, string? DisplayUnit, decimal? DisplayFactor)>();
            foreach (var row in _all)
            {
                if (!NomenclatureCardWindow.TryParseFactor(row.FactorText, out var factor))
                {
                    MessageBox.Show(
                        $"Коефіцієнт має бути додатним числом або порожнім.\nРядок: {row.Name}",
                        "Обозний", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var displayUnit = (row.SelectedDisplayUnit ?? "").Trim();
                if (VydDisplayUnitHelper.IsAddItem(displayUnit)) displayUnit = "";
                updates.Add((row.Id, displayUnit, factor));
            }

            AppStore.Instance.UpdateNomenclatureDisplayFields(updates);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            CrashLog.Write("BulkDisplayUomWindow.Save", ex);
            MessageBox.Show("Не вдалося зберегти:\n" + ex.Message, "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseClick(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Побудова списку вибору О.В. (копія на рядок — без спільного CollectionView).</summary>
    internal static List<string> BuildDisplayChoices(string? vyd, string? selected)
    {
        var list = new List<string> { "" };
        foreach (var u in AppStore.Instance.GetDisplayUnitsForVyd(vyd))
            list.Add(u);
        var sel = (selected ?? "").Trim();
        if (sel.Length > 0 && !VydDisplayUnitHelper.IsAddItem(sel)
            && !list.Contains(sel, StringComparer.Ordinal))
            list.Add(sel);
        list.Add(VydDisplayUnitHelper.AddUnitItem);
        return list;
    }

    /// <summary>Ключ сортування коефіцієнта: порожньо → null (внизу при ASC).</summary>
    internal static decimal? ParseFactorSortKey(string? text)
    {
        if (!NomenclatureCardWindow.TryParseFactor(text, out var f)) return null;
        return f;
    }

    private sealed class DisplayUomRow : INotifyPropertyChanged
    {
        private string _selectedDisplayUnit;
        private string _factorText;
        private ObservableCollection<string> _displayChoices;

        private DisplayUomRow(
            Guid id,
            string name,
            string vydRaw,
            string vydText,
            string unit,
            string selectedDisplayUnit,
            string factorText,
            ObservableCollection<string> choices)
        {
            Id = id;
            Name = name;
            VydRaw = vydRaw;
            VydText = vydText;
            Unit = unit;
            _selectedDisplayUnit = selectedDisplayUnit;
            _factorText = factorText;
            _displayChoices = choices;
        }

        public static DisplayUomRow FromItem(NomenclatureItem n)
        {
            var vydRaw = (n.Vyd ?? "").Trim();
            var selected = (n.DisplayUnit ?? "").Trim();
            if (VydDisplayUnitHelper.IsAddItem(selected)) selected = "";
            var factorText = n.DisplayFactor is decimal f && f > 0
                ? Ua.Num(f, "0.####")
                : "";
            var choices = new ObservableCollection<string>(BuildDisplayChoices(vydRaw, selected));
            return new DisplayUomRow(
                n.Id,
                n.Name,
                vydRaw,
                NomenclatureListWindow.FormatVydColumn(vydRaw),
                n.Unit,
                selected,
                factorText,
                choices);
        }

        public Guid Id { get; }
        public string Name { get; }
        public string VydRaw { get; }
        public string VydText { get; }
        public string Unit { get; }

        public ObservableCollection<string> DisplayChoices => _displayChoices;

        public string SelectedDisplayUnit
        {
            get => _selectedDisplayUnit;
            set
            {
                var v = value ?? "";
                if (_selectedDisplayUnit == v) return;
                _selectedDisplayUnit = v;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedDisplayUnit)));
            }
        }

        public string FactorText
        {
            get => _factorText;
            set
            {
                var v = value ?? "";
                if (_factorText == v) return;
                _factorText = v;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FactorText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FactorSortKey)));
            }
        }

        public decimal? FactorSortKey => ParseFactorSortKey(_factorText);

        public void RebuildChoices(string? preferSelected)
        {
            var prefer = (preferSelected ?? "").Trim();
            if (VydDisplayUnitHelper.IsAddItem(prefer)) prefer = "";
            var keep = prefer.Length > 0 ? prefer : _selectedDisplayUnit;
            if (VydDisplayUnitHelper.IsAddItem(keep)) keep = "";
            var next = BuildDisplayChoices(VydRaw, keep);
            _displayChoices = new ObservableCollection<string>(next);
            if (keep.Length > 0 && !_displayChoices.Contains(keep))
                keep = "";
            _selectedDisplayUnit = keep;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayChoices)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedDisplayUnit)));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
