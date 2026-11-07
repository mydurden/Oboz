using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Obozny;

public partial class SummaryStatementMonthPickWindow : Window
{
    private static readonly string[] MonthNames =
    [
        "Січень", "Лютий", "Березень", "Квітень", "Травень", "Червень",
        "Липень", "Серпень", "Вересень", "Жовтень", "Листопад", "Грудень"
    ];

    /// <summary>Короткі підписи для сітки 4×3.</summary>
    private static readonly string[] MonthShort =
    [
        "Січ", "Лют", "Бер", "Кві", "Тра", "Чер",
        "Лип", "Сер", "Вер", "Жов", "Лис", "Гру"
    ];

    private readonly ToggleButton[] _monthToggles = new ToggleButton[12];
    private readonly HashSet<int> _selected = new();
    private bool _syncing;

    public int SelectedYear { get; private set; }
    public IReadOnlyList<int> SelectedMonths { get; private set; } = Array.Empty<int>();
    public string StatementNumber { get; private set; } = "";
    /// <summary>Обрана служба з довідника (`ІС ССП` / `СЗУ` / `СОВТ та МСВ`).</summary>
    public string SelectedSupplyService { get; private set; } = SupplyServiceHelper.IsSsp;

    public SummaryStatementMonthPickWindow()
    {
        InitializeComponent();
        BuildYearList();
        BuildSupplyList();
        BuildMonthToggles();
        // За замовчуванням — поточний місяць поточного року.
        var today = DateTime.Today;
        YearBox.SelectedItem = today.Year;
        SetMonthChecked(today.Month, true);
        _selected.Add(today.Month);
    }

    private void BuildYearList()
    {
        var years = new SortedSet<int> { DateTime.Today.Year };
        foreach (var d in AppStore.Instance.Data.Documents)
            years.Add(d.Date.Year);
        foreach (var m in AppStore.Instance.Data.Movements)
            years.Add(m.DocumentDate.Year);

        var min = Math.Max(2000, years.Min - 1);
        var max = Math.Max(DateTime.Today.Year + 1, years.Max);
        for (var y = min; y <= max; y++)
            YearBox.Items.Add(y);
        YearBox.SelectedItem = DateTime.Today.Year;
    }

    private void BuildSupplyList()
    {
        SupplyBox.Items.Clear();
        foreach (var s in SupplyServiceHelper.All)
            SupplyBox.Items.Add(s);
        // «Інженерна служба» у ТЗ = ІС ССП у довіднику програми.
        SupplyBox.SelectedItem = SupplyServiceHelper.IsSsp;
    }

    private void BuildMonthToggles()
    {
        for (var i = 0; i < 12; i++)
        {
            var month = i + 1;
            var btn = new ToggleButton
            {
                Content = MonthShort[i],
                Tag = month,
                ToolTip = MonthNames[i],
                Style = (Style)FindResource("GhostToggle"),
                MinHeight = 40,
                Height = 40,
                MinWidth = 64,
                Margin = new Thickness(3),
                Padding = new Thickness(4, 0, 4, 0),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            btn.Checked += MonthChecked;
            btn.Unchecked += MonthUnchecked;
            _monthToggles[i] = btn;
            MonthsPanel.Children.Add(btn);
        }
    }

    private void YearChanged(object sender, SelectionChangedEventArgs e)
    {
        // Зміна року не скидає вибір місяців — той самий діапазон у новому році.
    }

    private void MonthChecked(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        if (sender is not ToggleButton btn || btn.Tag is not int month) return;
        if (!SummaryStatement.TryToggleMonth(_selected, month, wantSelected: true,
                out var next, out var error))
        {
            MessageBox.Show(this, error ?? "Некоректний вибір місяців.", "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Information);
            SetMonthChecked(month, false);
            return;
        }
        ApplySelection(next);
    }

    private void MonthUnchecked(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        if (sender is not ToggleButton btn || btn.Tag is not int month) return;
        if (!SummaryStatement.TryToggleMonth(_selected, month, wantSelected: false,
                out var next, out var error))
        {
            MessageBox.Show(this, error ?? "Некоректний вибір місяців.", "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Information);
            SetMonthChecked(month, true);
            return;
        }
        ApplySelection(next);
    }

    private void ApplySelection(List<int> next)
    {
        _selected.Clear();
        foreach (var m in next)
            _selected.Add(m);
        _syncing = true;
        try
        {
            for (var i = 0; i < 12; i++)
                _monthToggles[i].IsChecked = _selected.Contains(i + 1);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SetMonthChecked(int month, bool on)
    {
        if (month is < 1 or > 12) return;
        _syncing = true;
        try { _monthToggles[month - 1].IsChecked = on; }
        finally { _syncing = false; }
    }

    private void OkClick(object sender, RoutedEventArgs e)
    {
        if (YearBox.SelectedItem is not int year)
        {
            MessageBox.Show(this, "Оберіть рік.", "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!SummaryStatement.TryPeriodFromMonths(year, _selected,
                out _, out _, out var error))
        {
            MessageBox.Show(this, error ?? "Оберіть місяці.", "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var supply = SupplyServiceHelper.Normalize(SupplyBox.SelectedItem as string);
        if (supply == null)
        {
            MessageBox.Show(this, "Оберіть службу забезпечення.", "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedYear = year;
        SelectedMonths = _selected.OrderBy(m => m).ToList();
        StatementNumber = (NumberBox.Text ?? "").Trim();
        SelectedSupplyService = supply;
        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Підпис періоду для імені файлу / логів.</summary>
    public static string FormatMonthRange(int year, IReadOnlyList<int> months)
    {
        if (months.Count == 0) return year.ToString(CultureInfo.InvariantCulture);
        var first = months.Min();
        var last = months.Max();
        if (first == last)
            return $"{MonthNames[first - 1]} {year}";
        return $"{MonthNames[first - 1]}–{MonthNames[last - 1]} {year}";
    }
}
