using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace Obozny;

public partial class BulkSupplyServiceWindow : Window
{
    private readonly List<SupplyRow> _all = new();
    private readonly ObservableCollection<SupplyRow> _visible = new();
    private readonly List<string> _choices;
    private bool _ready;
    private bool _applyingFilter;
    private bool _filterSyncQueued;

    public BulkSupplyServiceWindow()
    {
        InitializeComponent();
        _choices = BuildChoices();
        AppStore.Instance.EnsureNomenclatures();
        foreach (var n in AppStore.Instance.Data.Nomenclatures
                     .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(x => x.Unit, StringComparer.CurrentCultureIgnoreCase))
        {
            var current = AppStore.Instance.ResolveNomenclatureSupply(n);
            _all.Add(SupplyRow.FromItem(n, current, _choices));
        }

        SupplyForAllBox.Items.Clear();
        foreach (var c in _choices)
            SupplyForAllBox.Items.Add(c);
        SupplyForAllBox.SelectedIndex = 0;

        RowsGrid.ItemsSource = _visible;
        Loaded += (_, _) => QueueFilterColumnSync();
        SizeChanged += (_, _) => QueueFilterColumnSync();
        RowsGrid.SizeChanged += (_, _) => QueueFilterColumnSync();
        RowsGrid.LayoutUpdated += (_, _) => QueueFilterColumnSync();
        _ready = true;
        ApplyFilter();
    }

    private static List<string> BuildChoices()
    {
        var list = new List<string> { "" };
        list.AddRange(SupplyServiceHelper.All);
        return list;
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

    private void SyncFilterColumnWidths()
    {
        var cols = RowsGrid.Columns;
        if (cols.Count == 0 || FilterGrid.ColumnDefinitions.Count < cols.Count + 1) return;
        var widths = new double[cols.Count];
        for (var i = 0; i < cols.Count; i++)
            widths[i] = cols[i].ActualWidth;
        var gutter = BulkDisplayUomWindow.ComputeFilterGutter(RowsGrid.ActualWidth, widths);
        BulkDisplayUomWindow.ApplyFilterColumnPixels(FilterGrid, widths, gutter);
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
            var supplyQ = (FilterSupply.Text ?? "").Trim().ToLowerInvariant();

            IEnumerable<SupplyRow> src = _all;
            if (nameQ.Length > 0)
                src = src.Where(r => r.Name.ToLowerInvariant().Contains(nameQ));
            if (vydQ.Length > 0)
                src = src.Where(r => r.VydText.ToLowerInvariant().Contains(vydQ));
            if (unitQ.Length > 0)
                src = src.Where(r => r.Unit.ToLowerInvariant().Contains(unitQ));
            if (supplyQ.Length > 0)
                src = src.Where(r => r.SelectedSupply.ToLowerInvariant().Contains(supplyQ));

            _visible.Clear();
            foreach (var row in src)
                _visible.Add(row);

            RestoreSortDescriptions(sorts);
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
        PaintOne(FilterSupply);
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

    private void ApplyToSelectedClick(object sender, RoutedEventArgs e)
    {
        var chosen = SupplyForAllBox.SelectedItem as string ?? "";
        var targets = RowsGrid.SelectedItems.OfType<SupplyRow>().ToList();
        if (targets.Count == 0)
            targets = _visible.ToList();
        foreach (var row in targets)
            row.SelectedSupply = chosen;
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var updates = _all
                .Select(r => (r.Id, (string?)r.SelectedSupply))
                .ToList();
            AppStore.Instance.UpdateNomenclatureSupplyServices(updates);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не вдалося зберегти:\n" + ex.Message, "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed class SupplyRow : INotifyPropertyChanged
    {
        private string _selectedSupply;

        private SupplyRow(
            Guid id,
            string name,
            string vydText,
            string unit,
            string selectedSupply,
            IReadOnlyList<string> choices)
        {
            Id = id;
            Name = name;
            VydText = vydText;
            Unit = unit;
            _selectedSupply = selectedSupply;
            SupplyChoices = choices;
        }

        public static SupplyRow FromItem(NomenclatureItem n, string current, IReadOnlyList<string> choices)
        {
            var sel = SupplyServiceHelper.Normalize(current) ?? "";
            return new SupplyRow(
                n.Id,
                n.Name,
                NomenclatureListWindow.FormatVydColumn(n.Vyd),
                n.Unit,
                sel,
                choices);
        }

        public Guid Id { get; }
        public string Name { get; }
        public string VydText { get; }
        public string Unit { get; }
        public IReadOnlyList<string> SupplyChoices { get; }

        public string SelectedSupply
        {
            get => _selectedSupply;
            set
            {
                var v = SupplyServiceHelper.Normalize(value) ?? (value ?? "").Trim();
                if (!SupplyServiceHelper.IsKnown(v)) v = "";
                if (_selectedSupply == v) return;
                _selectedSupply = v;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedSupply)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
