using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Obozny;

/// <summary>Діалог вибору видів та О.В. відображення для нотатки залишків.</summary>
public partial class StockSummaryVydFilterWindow : Window
{
    private readonly ObservableCollection<VydFilterRow> _rows = new();
    private bool _loading;

    /// <summary>Обрані види після «Зберегти» (може бути порожнім — це валідний результат).</summary>
    public IReadOnlyList<string> SelectedVyds { get; private set; } = Array.Empty<string>();

    /// <summary>Обрані О.В. відображення по Виду (лише для увімкнених із непорожнім вибором).</summary>
    public IReadOnlyDictionary<string, string> SelectedDisplayUnits { get; private set; } =
        new Dictionary<string, string>();

    public StockSummaryVydFilterWindow(
        IReadOnlyList<string>? enabledVyds,
        IReadOnlyDictionary<string, string>? displayUnitByVyd = null)
    {
        InitializeComponent();
        var custom = AppStore.Instance.Data.CustomVyds;
        var enabled = StockSummaryByVyd.ResolveEnabledVyds(enabledVyds, custom);
        var displayMap = displayUnitByVyd ?? new Dictionary<string, string>();
        _loading = true;
        foreach (var vyd in NomenclatureVydCatalog.Combined(custom))
        {
            displayMap.TryGetValue(vyd, out var chosen);
            _rows.Add(CreateRow(vyd, enabled.Contains(vyd), chosen));
        }
        RowsGrid.ItemsSource = _rows;
        _loading = false;
    }

    private VydFilterRow CreateRow(string vyd, bool selected, string? chosenDisplay)
    {
        var choices = BuildDisplayChoices(vyd);
        var row = new VydFilterRow(vyd, selected, choices);
        if (!string.IsNullOrWhiteSpace(chosenDisplay) && choices.Contains(chosenDisplay))
            row.SelectedDisplayUnit = chosenDisplay;
        else
            row.SelectedDisplayUnit = "";
        return row;
    }

    private static ObservableCollection<string> BuildDisplayChoices(string vyd)
    {
        var list = new ObservableCollection<string> { "" };
        foreach (var u in AppStore.Instance.GetDisplayUnitsForVyd(vyd))
            list.Add(u);
        list.Add(VydDisplayUnitHelper.AddUnitItem);
        return list;
    }

    /// <summary>
    /// Зібрати обрані види. Порожній список дозволений (немає мінімуму «хоча б один»).
    /// </summary>
    internal static IReadOnlyList<string> CollectSelectedVyds(
        IEnumerable<(string Name, bool IsSelected)> rows) =>
        rows.Where(r => r.IsSelected).Select(r => r.Name).ToList();

    internal static IReadOnlyDictionary<string, string> CollectDisplayUnits(
        IEnumerable<(string Name, bool IsSelected, string DisplayUnit)> rows)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            if (!r.IsSelected) continue;
            var u = (r.DisplayUnit ?? "").Trim();
            if (u.Length == 0 || VydDisplayUnitHelper.IsAddItem(u)) continue;
            map[r.Name] = u;
        }
        return map;
    }

    /// <summary>«Зберегти» завжди дозволене, зокрема при 0 галочках (немає «оберіть хоча б один»).</summary>
    internal static bool IsSaveAllowed(int _) => true;

    /// <summary>
    /// Клік по галочці / комірці галочки (не заголовок).
    /// Не плутати з ComboBox: його шаблон містить ToggleButton — інакше Preview з’їдає відкриття списку.
    /// </summary>
    internal static bool IsCheckBoxHit(DependencyObject? src)
    {
        if (src == null) return false;
        if (FindParent<DataGridColumnHeader>(src) != null) return false;
        if (FindParent<ComboBox>(src) != null) return false;
        if (FindParent<CheckBox>(src) != null) return true;
        var cell = FindParent<DataGridCell>(src);
        return cell?.Column is DataGridCheckBoxColumn;
    }

    /// <summary>Клік по рядку (не галочка, не combo, не заголовок) — перемикає обрання.</summary>
    internal static bool ShouldToggleFromRowClick(DependencyObject? src)
    {
        if (src == null) return false;
        if (FindParent<ComboBox>(src) != null) return false;
        if (IsCheckBoxHit(src)) return false;
        if (FindParent<DataGridColumnHeader>(src) != null) return false;
        return FindParent<DataGridRow>(src) != null;
    }

    private void RowsGridRowClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src) return;
        if (!TryToggleSelection(src)) return;
        e.Handled = true;
    }

    private void RowsGridTouchDown(object sender, TouchEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src) return;
        if (!TryToggleSelection(src)) return;
        e.Handled = true;
    }

    private bool TryToggleSelection(DependencyObject src)
    {
        if (FindParent<ComboBox>(src) != null) return false;
        if (!IsCheckBoxHit(src) && !ShouldToggleFromRowClick(src)) return false;
        var row = FindParent<DataGridRow>(src);
        if (row?.Item is not VydFilterRow item) return false;
        item.IsSelected = !item.IsSelected;
        return true;
    }

    private static T? FindParent<T>(DependencyObject? src) where T : DependencyObject
    {
        while (src != null)
        {
            if (src is T match) return match;
            src = VisualTreeHelper.GetParent(src);
        }
        return null;
    }

    private void DisplayUnitComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (sender is not ComboBox box) return;
        if (box.DataContext is not VydFilterRow row) return;
        if (box.SelectedItem is not string s || !VydDisplayUnitHelper.IsAddItem(s)) return;
        box.IsDropDownOpen = false;
        Dispatcher.BeginInvoke(new Action(() => HandleAddDisplayUnit(row)));
    }

    private void HandleAddDisplayUnit(VydFilterRow row)
    {
        var already = AppStore.Instance.GetDisplayUnitsForVyd(row.Name);
        var dlg = new VydDisplayUnitPickWindow(
            row.Name, AppStore.Instance.Data.Nomenclatures, already)
        {
            Owner = this
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ChosenUnit))
        {
            var unit = AppStore.Instance.AddDisplayUnitForVyd(row.Name, dlg.ChosenUnit);
            _loading = true;
            row.RefreshChoices(BuildDisplayChoices(row.Name), unit ?? dlg.ChosenUnit);
            _loading = false;
        }
        else
        {
            _loading = true;
            row.SelectedDisplayUnit = "";
            _loading = false;
        }
    }

    private void SelectAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
            row.IsSelected = true;
    }

    private void ClearAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
            row.IsSelected = false;
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            RowsGrid.CancelEdit(DataGridEditingUnit.Cell);
            RowsGrid.CancelEdit(DataGridEditingUnit.Row);
        }
        catch
        {
            // ігнор — далі читаємо модель рядків
        }

        SelectedVyds = CollectSelectedVyds(_rows.Select(r => (r.Name, r.IsSelected)));
        SelectedDisplayUnits = CollectDisplayUnits(
            _rows.Select(r => (r.Name, r.IsSelected, r.SelectedDisplayUnit)));
        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed class VydFilterRow : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _selectedDisplayUnit = "";
        private ObservableCollection<string> _displayChoices;

        public VydFilterRow(
            string name,
            bool isSelected,
            ObservableCollection<string> displayChoices)
        {
            Name = name;
            _isSelected = isSelected;
            _displayChoices = displayChoices;
        }

        public string Name { get; }

        public ObservableCollection<string> DisplayChoices
        {
            get => _displayChoices;
            private set
            {
                _displayChoices = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayChoices)));
            }
        }

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

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public void RefreshChoices(ObservableCollection<string> choices, string? select)
        {
            DisplayChoices = choices;
            SelectedDisplayUnit = select ?? "";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
