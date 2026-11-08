using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Obozny;

public partial class NomenclatureListWindow : Window
{
    private List<NomRow> _all = new();
    private bool _initialLoadStarted;

    public NomenclatureListWindow()
    {
        InitializeComponent();
        // Не блокуємо ShowDialog підрахунком LinkedCount/StockQty / Ensure — спочатку кадр вікна.
        Loaded += OnLoadedDeferInitialReload;
    }

    private void OnLoadedDeferInitialReload(object sender, RoutedEventArgs e)
    {
        if (_initialLoadStarted) return;
        _initialLoadStarted = true;
        Loaded -= OnLoadedDeferInitialReload;
        Dispatcher.BeginInvoke(ReloadDeferred, DispatcherPriority.Background);
    }

    /// <summary>Після Show: швидкий список без Ensure/LinkedCount/StockQty, потім дозаповнення.</summary>
    private void ReloadDeferred()
    {
        // Імена вже в пам’яті після AppStore.Load — без скану Properties.
        RebuildRows(stockStats: null);
        ApplyFilter();
        Dispatcher.BeginInvoke(EnsureAndFillStockStats, DispatcherPriority.Background);
    }

    private void Reload()
    {
        AppStore.Instance.EnsureNomenclatures();
        RebuildRows(stockStats: BuildStockStats());
        ApplyFilter();
    }

    private void RebuildRows(Dictionary<Guid, StockStats>? stockStats)
    {
        _all = AppStore.Instance.Data.Nomenclatures
            .OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(n => n.Unit, StringComparer.CurrentCultureIgnoreCase)
            .Select(n =>
            {
                var linked = 0;
                var qty = 0m;
                var known = false;
                if (stockStats != null)
                {
                    known = true;
                    if (stockStats.TryGetValue(n.Id, out var s))
                    {
                        linked = s.LinkedCount;
                        qty = s.StockQty;
                    }
                }
                return new NomRow(
                    n.Id,
                    n.Name,
                    FormatVydColumn(n.Vyd),
                    n.Unit,
                    NomenclatureKindNames.Of(n.Kind),
                    linked,
                    qty,
                    stockKnown: known,
                    hasDisplayPair: HasOwnDisplayConversion(n.DisplayUnit, n.DisplayFactor));
            })
            .ToList();
    }

    /// <summary>
    /// Пара «власна О.В. відображення + коефіцієнт»: DisplayUnit заданий і DisplayFactor &gt; 0
    /// (як на картці / у підсумку залишків). Порожній коеф. або без власної О.В. — false.
    /// </summary>
    internal static bool HasOwnDisplayConversion(string? displayUnit, decimal? displayFactor) =>
        !string.IsNullOrWhiteSpace(displayUnit) && displayFactor is > 0;

    /// <summary>Пріоритет: є залишок і ще немає пари О.В. відображення + коефіцієнт.</summary>
    internal static bool IsPriorityHighlight(bool stockKnown, decimal stockQty, bool hasDisplayPair) =>
        stockKnown && stockQty > NomRow.QtyEps && !hasDisplayPair;

    /// <summary>Приглушено: нульовий залишок або вже задана пара О.В. відображення + коефіцієнт.</summary>
    internal static bool IsDimmedHighlight(bool stockKnown, decimal stockQty, bool hasDisplayPair) =>
        stockKnown && (stockQty <= NomRow.QtyEps || hasDisplayPair);

    /// <summary>
    /// LinkedCount — кількість карток майна; StockQty — сума Quantity по всіх Properties
    /// цієї номенклатури (Запаси + ОЗ). Підсвітка: залишок + наявність DisplayUnit/Factor.
    /// </summary>
    private static Dictionary<Guid, StockStats> BuildStockStats()
    {
        var map = new Dictionary<Guid, StockStats>();
        foreach (var p in AppStore.Instance.Data.Properties)
        {
            if (p.NomenclatureId == null) continue;
            var id = p.NomenclatureId.Value;
            if (map.TryGetValue(id, out var have))
                map[id] = new StockStats(have.LinkedCount + 1, have.StockQty + p.Quantity);
            else
                map[id] = new StockStats(1, p.Quantity);
        }
        return map;
    }

    private void EnsureAndFillStockStats()
    {
        var before = AppStore.Instance.Data.Nomenclatures.Count;
        AppStore.Instance.EnsureNomenclatures();
        // Рідко: Ensure додав паспорти — повний Reload зі змістом.
        if (AppStore.Instance.Data.Nomenclatures.Count != before)
        {
            Reload();
            return;
        }

        if (_all.Count == 0) return;
        var stats = BuildStockStats();
        foreach (var row in _all)
        {
            if (stats.TryGetValue(row.Id, out var s))
                row.ApplyStockStats(s.LinkedCount, s.StockQty);
            else
                row.ApplyStockStats(0, 0m);
        }

        // Якщо користувач уже встиг відсортувати за «Позицій на складі» — оновити порядок.
        var view = CollectionViewSource.GetDefaultView(Grid.ItemsSource);
        if (view != null
            && view.SortDescriptions.Any(s =>
                string.Equals(s.PropertyName, nameof(NomRow.LinkedCount), StringComparison.Ordinal)))
        {
            view.Refresh();
        }
    }

    /// <summary>Порожній Вид у списку — тире «—».</summary>
    internal static string FormatVydColumn(string? vyd)
    {
        var t = (vyd ?? "").Trim();
        return t.Length == 0 ? "—" : t;
    }

    private void ApplyFilter()
    {
        var sorts = CaptureSortDescriptions();
        var selectedId = (Grid.SelectedItem as NomRow)?.Id;
        var scrollOffset = CaptureVerticalOffset();

        var q = (SearchBox.Text ?? "").Trim().ToLowerInvariant();
        IEnumerable<NomRow> list = _all;
        if (q.Length > 0)
        {
            list = _all.Where(r =>
                r.Name.ToLowerInvariant().Contains(q)
                || r.VydText.ToLowerInvariant().Contains(q)
                || r.Unit.ToLowerInvariant().Contains(q)
                || r.KindText.ToLowerInvariant().Contains(q));
        }
        Grid.ItemsSource = list.ToList();

        RestoreSortDescriptions(sorts);
        // Selection may ScrollIntoView; restore scroll after layout so offset wins.
        Dispatcher.BeginInvoke(() =>
        {
            RestoreSelection(selectedId);
            if (scrollOffset is double offset)
                RestoreVerticalOffset(offset);
        }, DispatcherPriority.Loaded);
    }

    private List<SortDescription> CaptureSortDescriptions()
    {
        var view = CollectionViewSource.GetDefaultView(Grid.ItemsSource);
        if (view == null || view.SortDescriptions.Count == 0)
            return new List<SortDescription>();
        return view.SortDescriptions.ToList();
    }

    private void RestoreSortDescriptions(IReadOnlyList<SortDescription> sorts)
    {
        foreach (var col in Grid.Columns)
            col.SortDirection = null;

        if (sorts.Count == 0) return;

        var view = CollectionViewSource.GetDefaultView(Grid.ItemsSource);
        if (view == null) return;

        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            foreach (var s in sorts)
            {
                view.SortDescriptions.Add(s);
                var col = Grid.Columns.FirstOrDefault(c =>
                    string.Equals(c.SortMemberPath, s.PropertyName, StringComparison.Ordinal));
                if (col != null)
                    col.SortDirection = s.Direction;
            }
        }
    }

    private void RestoreSelection(Guid? selectedId)
    {
        if (selectedId == null) return;
        if (Grid.ItemsSource is not IEnumerable<NomRow> rows) return;
        var match = rows.FirstOrDefault(r => r.Id == selectedId.Value);
        if (match != null)
            Grid.SelectedItem = match;
    }

    private double? CaptureVerticalOffset()
    {
        var sv = FindChildScrollViewer(Grid);
        return sv == null ? null : sv.VerticalOffset;
    }

    private void RestoreVerticalOffset(double offset)
    {
        var sv = FindChildScrollViewer(Grid);
        if (sv == null) return;
        var max = Math.Max(0, sv.ExtentHeight - sv.ViewportHeight);
        sv.ScrollToVerticalOffset(Math.Clamp(offset, 0, max));
    }

    private static ScrollViewer? FindChildScrollViewer(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            var nested = FindChildScrollViewer(child);
            if (nested != null) return nested;
        }
        return null;
    }

    private void SearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void AddClick(object sender, RoutedEventArgs e)
    {
        var card = new NomenclatureCardWindow(null) { Owner = this };
        if (card.ShowDialog() == true) Reload();
    }

    private void BulkDisplayUomClick(object sender, RoutedEventArgs e)
    {
        var dlg = new BulkDisplayUomWindow { Owner = this };
        if (dlg.ShowDialog() == true) Reload();
    }

    private void BulkSupplyServiceClick(object sender, RoutedEventArgs e)
    {
        var dlg = new BulkSupplyServiceWindow { Owner = this };
        if (dlg.ShowDialog() == true) Reload();
    }

    private void OpenClick(object sender, RoutedEventArgs e) => OpenSelected();

    private void GridDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();

    private void OpenSelected()
    {
        if (Grid.SelectedItem is not NomRow row) return;
        var nom = AppStore.Instance.Data.Nomenclatures.FirstOrDefault(n => n.Id == row.Id);
        if (nom == null) return;
        var card = new NomenclatureCardWindow(nom) { Owner = this };
        if (card.ShowDialog() == true) Reload();
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private readonly record struct StockStats(int LinkedCount, decimal StockQty);

    private sealed class NomRow : INotifyPropertyChanged
    {
        internal const decimal QtyEps = 0.0001m;
        private int _linkedCount;
        private decimal _stockQty;
        private bool _stockKnown;
        private readonly bool _hasDisplayPair;

        public NomRow(
            Guid id,
            string name,
            string vydText,
            string unit,
            string kindText,
            int linkedCount,
            decimal stockQty,
            bool stockKnown,
            bool hasDisplayPair)
        {
            Id = id;
            Name = name;
            VydText = vydText;
            Unit = unit;
            KindText = kindText;
            _linkedCount = linkedCount;
            _stockQty = stockQty;
            _stockKnown = stockKnown;
            _hasDisplayPair = hasDisplayPair;
        }

        public Guid Id { get; }
        public string Name { get; }
        public string VydText { get; }
        public string Unit { get; }
        public string KindText { get; }

        /// <summary>Кількість карток майна (колонка «Позицій на складі»).</summary>
        public int LinkedCount => _linkedCount;

        /// <summary>Сумарний залишок Quantity по всіх Properties цієї номенклатури.</summary>
        public decimal StockQty => _stockQty;

        /// <summary>
        /// Пріоритет для користувача: залишок &gt; 0 і ще немає пари власна О.В. + коефіцієнт.
        /// Поки StockQty не дозаповнено — false (без мигання).
        /// </summary>
        public bool IsPriority => IsPriorityHighlight(_stockKnown, _stockQty, _hasDisplayPair);

        /// <summary>
        /// Приглушено: StockQty = 0 або вже задані власна О.В. відображення і коефіцієнт.
        /// Поки StockQty не дозаповнено — false (без мигання сірим).
        /// </summary>
        public bool IsDimmed => IsDimmedHighlight(_stockKnown, _stockQty, _hasDisplayPair);

        public void ApplyStockStats(int linkedCount, decimal stockQty)
        {
            if (_stockKnown && _linkedCount == linkedCount && _stockQty == stockQty) return;
            _linkedCount = linkedCount;
            _stockQty = stockQty;
            _stockKnown = true;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LinkedCount)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StockQty)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPriority)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDimmed)));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
