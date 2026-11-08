using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Obozny;

public partial class AssignVydBulkWindow : Window
{
    private readonly List<AssignVydRow> _allRows = new();
    private readonly HashSet<Guid> _nameMatchIds = new();
    private readonly ObservableCollection<AssignVydRow> _visible = new();
    private bool _ready;
    private string _lastRealVyd = "";

    /// <param name="allWithoutVyd">Усі без Виду (для перемикача «Усі без Виду»).</param>
    /// <param name="nameMatched">Підмножина за назвою — початковий список.</param>
    /// <param name="suggestedVyd">Вид з картки.</param>
    public AssignVydBulkWindow(
        IReadOnlyList<NomenclatureItem> allWithoutVyd,
        IReadOnlyList<NomenclatureItem> nameMatched,
        string suggestedVyd)
    {
        InitializeComponent();
        var custom = AppStore.Instance.Data.CustomVyds;
        var catalog = NomenclatureVydCatalog.Combined(custom);
        var suggested = (suggestedVyd ?? "").Trim();
        if (suggested.Length == 0
            || NomenclatureVydCatalog.IsNewItem(suggested)
            || !catalog.Contains(suggested, StringComparer.Ordinal))
            suggested = catalog.Count > 0 ? catalog[0] : NomenclatureVydCatalog.All[0];

        foreach (var vyd in NomenclatureVydCatalog.ComboChoices(custom))
            VydForAllBox.Items.Add(vyd);
        VydForAllBox.SelectedItem = suggested;
        _lastRealVyd = suggested;
        VydForAllBox.SelectionChanged += VydForAllChanged;

        foreach (var id in nameMatched.Select(n => n.Id))
            _nameMatchIds.Add(id);
        foreach (var n in allWithoutVyd)
        {
            var row = new AssignVydRow(
                n.Id,
                n.Name,
                n.Unit,
                NomenclatureKindNames.Of(n.Kind),
                isSelected: _nameMatchIds.Contains(n.Id));
            _allRows.Add(row);
        }

        ModeByName.IsChecked = true;
        _ready = true;
        ApplyVisibleList(byNameOnly: true);
    }

    /// <summary>Копія довідника (не спільний масив) — для тестів / майбутніх комбо.</summary>
    internal static IReadOnlyList<string> CopyVydChoices() =>
        NomenclatureVydCatalog.Combined(AppStore.Instance.Data.CustomVyds).ToList();

    private void VydForAllChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VydForAllBox.SelectedItem is not string s) return;
        if (NomenclatureVydCatalog.IsNewItem(s))
        {
            VydForAllBox.IsDropDownOpen = false;
            Dispatcher.BeginInvoke(new Action(HandleNewVydPick));
            return;
        }
        _lastRealVyd = s;
    }

    private void HandleNewVydPick()
    {
        var previous = string.IsNullOrWhiteSpace(_lastRealVyd)
            ? NomenclatureVydCatalog.Combined(AppStore.Instance.Data.CustomVyds).FirstOrDefault()
              ?? NomenclatureVydCatalog.All[0]
            : _lastRealVyd;
        var dlg = new NewCustomVydWindow(AppStore.Instance.Data.CustomVyds) { Owner = this };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.CreatedName))
        {
            var name = AppStore.Instance.AddCustomVyd(dlg.CreatedName);
            if (name == null) { RestoreVydForAll(previous); return; }
            VydForAllBox.SelectionChanged -= VydForAllChanged;
            VydForAllBox.Items.Clear();
            foreach (var vyd in NomenclatureVydCatalog.ComboChoices(AppStore.Instance.Data.CustomVyds))
                VydForAllBox.Items.Add(vyd);
            VydForAllBox.SelectedItem = name;
            _lastRealVyd = name;
            VydForAllBox.SelectionChanged += VydForAllChanged;
        }
        else
            RestoreVydForAll(previous);
    }

    private void RestoreVydForAll(string previous)
    {
        VydForAllBox.SelectionChanged -= VydForAllChanged;
        if (VydForAllBox.Items.Contains(previous))
            VydForAllBox.SelectedItem = previous;
        else if (VydForAllBox.Items.Count > 0)
            VydForAllBox.SelectedIndex = 0;
        _lastRealVyd = VydForAllBox.SelectedItem as string ?? previous;
        VydForAllBox.SelectionChanged += VydForAllChanged;
    }

    /// <summary>
    /// Клік по рядку (не по галочці і не по заголовку) — перемикає обрання.
    /// </summary>
    internal static bool ShouldToggleFromRowClick(DependencyObject? src)
    {
        if (src == null) return false;
        if (IsCheckBoxHit(src)) return false;
        if (FindParent<DataGridColumnHeader>(src) != null) return false;
        return FindParent<DataGridRow>(src) != null;
    }

    /// <summary>
    /// Клік по галочці / комірці галочки — одразу toggle (без «спочатку обрати рядок»).
    /// DataGrid інакше з’їдає перший клік на SelectedItem / edit mode.
    /// </summary>
    internal static bool IsCheckBoxHit(DependencyObject? src)
    {
        if (src == null) return false;
        if (FindParent<DataGridColumnHeader>(src) != null) return false;
        if (FindParent<ToggleButton>(src) != null) return true;
        var cell = FindParent<DataGridCell>(src);
        return cell?.Column is DataGridCheckBoxColumn;
    }

    private void RowsGridRowClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src) return;
        if (!TryToggleSelection(src)) return;
        // Handled: галочка — без другого кліку; рядок — без Select+ScrollIntoView.
        e.Handled = true;
    }

    private void RowsGridTouchDown(object sender, TouchEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src) return;
        if (!TryToggleSelection(src)) return;
        e.Handled = true; // щоб не дублювати з синтезованим mouse
    }

    private bool TryToggleSelection(DependencyObject src)
    {
        if (!IsCheckBoxHit(src) && !ShouldToggleFromRowClick(src)) return false;
        var row = FindParent<DataGridRow>(src);
        if (row?.Item is not AssignVydRow item) return false;
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

    private void ModeChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        ApplyVisibleList(byNameOnly: ModeByName.IsChecked == true);
    }

    private void ApplyVisibleList(bool byNameOnly)
    {
        _visible.Clear();
        IEnumerable<AssignVydRow> src = byNameOnly
            ? _allRows.Where(r => _nameMatchIds.Contains(r.Id))
            : _allRows;
        foreach (var row in src)
            _visible.Add(row);
        // ItemsSource лише якщо ще не прив’язано — повторне призначення скидає скрол.
        if (!ReferenceEquals(RowsGrid.ItemsSource, _visible))
            RowsGrid.ItemsSource = _visible;
    }

    private void SelectAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in _visible)
            row.IsSelected = true;
    }

    private void ClearAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in _visible)
            row.IsSelected = false;
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = _visible.Where(r => r.IsSelected).ToList();
            // 0 галочок — закрити без призначень (як «Скасувати» за ефектом).
            if (selected.Count == 0)
            {
                DialogResult = false;
                return;
            }

            var vyd = (VydForAllBox.SelectedItem as string ?? "").Trim();
            if (vyd.Length == 0 || NomenclatureVydCatalog.IsNewItem(vyd))
            {
                MessageBox.Show("Оберіть Вид.", "Обозний",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var assignments = selected.Select(r => (r.Id, vyd)).ToList();
            AppStore.Instance.AssignNomenclatureVyds(assignments);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не вдалося зберегти види:\n" + ex.Message, "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed class AssignVydRow : INotifyPropertyChanged
    {
        private bool _isSelected;

        public AssignVydRow(Guid id, string name, string unit, string kindText, bool isSelected)
        {
            Id = id;
            Name = name;
            Unit = unit;
            KindText = kindText;
            _isSelected = isSelected;
        }

        public Guid Id { get; }
        public string Name { get; }
        public string Unit { get; }
        public string KindText { get; }

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

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
