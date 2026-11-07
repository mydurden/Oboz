using System.Windows;
using System.Windows.Input;

namespace Obozny;

/// <summary>
/// Перше / наступне задання О.В. відображення для Виду:
/// своя назва або вибір з унікальних офіційних О.В. у базі.
/// </summary>
public partial class VydDisplayUnitPickWindow : Window
{
    private readonly IReadOnlyList<NomenclatureItem> _nomenclatures;
    private readonly IReadOnlyList<string> _already;

    public string? ChosenUnit { get; private set; }

    public VydDisplayUnitPickWindow(
        string vydName,
        IEnumerable<NomenclatureItem> nomenclatures,
        IEnumerable<string> alreadyForVyd)
    {
        InitializeComponent();
        _nomenclatures = nomenclatures.ToList();
        _already = alreadyForVyd.Select(u => (u ?? "").Trim()).Where(u => u.Length > 0).ToList();
        var vyd = (vydName ?? "").Trim();
        if (vyd.Length > 0)
            HintText.Text =
                $"Вид «{vyd}»: вкажіть свою О.В. або оберіть зі списку тих, що вже є на картках " +
                "(відмінні від уже заданих для цього виду і від щойно введеного).";
        RefreshBaseList();
        Loaded += (_, _) => OwnBox.Focus();
    }

    /// <summary>Результат: текст поля або обраний зі списку (для тестів / логіки OK).</summary>
    internal static string? ResolveChoice(string? typedOwn, string? selectedFromBase) =>
        VydDisplayUnitHelper.NormalizeUnitName(typedOwn, Array.Empty<string>())
        ?? VydDisplayUnitHelper.NormalizeUnitName(selectedFromBase, Array.Empty<string>());

    private void RefreshBaseList()
    {
        var candidates = VydDisplayUnitHelper.CandidateUnitsFromBase(
            _nomenclatures, _already, OwnBox.Text);
        BaseList.ItemsSource = candidates;
    }

    private void OwnBoxChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        RefreshBaseList();

    private void BaseListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BaseList.SelectedItem is string unit && unit.Length > 0)
        {
            OwnBox.Text = unit;
            SaveClick(sender, e);
        }
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        var fromList = BaseList.SelectedItem as string;
        var chosen = ResolveChoice(OwnBox.Text, fromList);
        if (chosen == null)
        {
            MessageBox.Show("Вкажіть свою О.В. або оберіть зі списку.",
                "Обозний", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Дубль із уже заданих для виду — відхилити.
        if (VydDisplayUnitHelper.NormalizeUnitName(chosen, _already) == null)
        {
            MessageBox.Show("Така О.В. уже задана для цього виду.",
                "Обозний", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ChosenUnit = chosen;
        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
