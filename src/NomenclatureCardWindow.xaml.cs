using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Obozny;

public partial class NomenclatureCardWindow : Window
{
    private readonly Guid? _id;
    private bool _loading;
    private bool _vydChangedByUser;
    private string? _lastRealVyd;

    public NomenclatureCardWindow(NomenclatureItem? existing)
    {
        InitializeComponent();
        _id = existing?.Id;
        TitleBlock.Text = existing == null ? "Нова номенклатура" : "Картка номенклатури";
        Title = TitleBlock.Text;

        _loading = true;
        foreach (var label in NomenclatureKindNames.All)
            KindBox.Items.Add(label);
        FillVydCombo(existing?.Vyd);

        if (existing != null)
        {
            NameBox.Text = existing.Name;
            UnitBox.Text = existing.Unit;
            NoteBox.Text = existing.Note;
            KindBox.SelectedItem = NomenclatureKindNames.Of(existing.Kind);
            _lastRealVyd = (existing.Vyd ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(existing.Vyd))
                SelectVydOrInsert(existing.Vyd);
            FillDisplayUnitCombo(existing.Vyd, existing.DisplayUnit);
            FactorBox.Text = existing.DisplayFactor is decimal f && f > 0
                ? Ua.Num(f, "0.####")
                : "";
        }
        else
        {
            UnitBox.Text = "шт";
            KindBox.SelectedItem = NomenclatureKindNames.Stocks;
            FillDisplayUnitCombo(null, null);
        }
        _loading = false;
    }

    private void FillVydCombo(string? selected)
    {
        VydBox.Items.Clear();
        foreach (var vyd in NomenclatureVydCatalog.ComboChoices(AppStore.Instance.Data.CustomVyds))
            VydBox.Items.Add(vyd);
        if (!string.IsNullOrWhiteSpace(selected))
            SelectVydOrInsert(selected);
    }

    private void SelectVydOrInsert(string vyd)
    {
        var t = vyd.Trim();
        if (VydBox.Items.Contains(t))
            VydBox.SelectedItem = t;
        else
        {
            // Збережене поза каталогом — показуємо перед «+ Новий…».
            var idx = VydBox.Items.Count;
            if (idx > 0 && NomenclatureVydCatalog.IsNewItem(VydBox.Items[idx - 1] as string))
                idx--;
            VydBox.Items.Insert(idx, t);
            VydBox.SelectedItem = t;
        }
    }

    private void FillDisplayUnitCombo(string? vyd, string? selected)
    {
        DisplayUnitBox.Items.Clear();
        DisplayUnitBox.Items.Add(""); // порожньо
        var units = AppStore.Instance.GetDisplayUnitsForVyd(vyd);
        foreach (var u in units)
            DisplayUnitBox.Items.Add(u);
        DisplayUnitBox.Items.Add(VydDisplayUnitHelper.AddUnitItem);
        if (!string.IsNullOrWhiteSpace(selected) && DisplayUnitBox.Items.Contains(selected))
            DisplayUnitBox.SelectedItem = selected;
        else if (!string.IsNullOrWhiteSpace(selected))
        {
            DisplayUnitBox.Items.Insert(DisplayUnitBox.Items.Count - 1, selected.Trim());
            DisplayUnitBox.SelectedItem = selected.Trim();
        }
        else
            DisplayUnitBox.SelectedIndex = 0;
    }

    private void VydChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        try
        {
            if (VydBox.SelectedItem is not string vyd)
                return;

            if (NomenclatureVydCatalog.IsNewItem(vyd))
            {
                VydBox.IsDropDownOpen = false;
                Dispatcher.BeginInvoke(new Action(HandleNewVydPick));
                return;
            }

            KindBox.SelectedItem = NomenclatureKindNames.Of(NomenclatureVydCatalog.DefaultKind(vyd));
            _vydChangedByUser = true;
            _lastRealVyd = vyd;
            FillDisplayUnitCombo(vyd, DisplayUnitBox.SelectedItem as string);
            CrashLog.Write("VydChanged", detail: "vyd=" + vyd);
        }
        catch (Exception ex)
        {
            CrashLog.Write("VydChanged", ex);
            MessageBox.Show("Помилка при зміні Виду:\n" + ex.Message, "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void HandleNewVydPick()
    {
        if (_loading) return;
        try
        {
            var dlg = new NewCustomVydWindow(AppStore.Instance.Data.CustomVyds) { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.CreatedName))
            {
                var name = AppStore.Instance.AddCustomVyd(dlg.CreatedName);
                if (name == null)
                {
                    RestoreLastVyd();
                    return;
                }
                _loading = true;
                FillVydCombo(name);
                _loading = false;
                _lastRealVyd = name;
                _vydChangedByUser = true;
                KindBox.SelectedItem = NomenclatureKindNames.Of(NomenclatureVydCatalog.DefaultKind(name));
                FillDisplayUnitCombo(name, null);
            }
            else
                RestoreLastVyd();
        }
        catch (Exception ex)
        {
            CrashLog.Write("HandleNewVydPick", ex);
            RestoreLastVyd();
            MessageBox.Show("Не вдалося додати вид:\n" + ex.Message, "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RestoreLastVyd()
    {
        _loading = true;
        if (!string.IsNullOrWhiteSpace(_lastRealVyd) && VydBox.Items.Contains(_lastRealVyd))
            VydBox.SelectedItem = _lastRealVyd;
        else
            VydBox.SelectedItem = null;
        _loading = false;
    }

    private void DisplayUnitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (DisplayUnitBox.SelectedItem is not string s) return;
        if (!VydDisplayUnitHelper.IsAddItem(s)) return;

        DisplayUnitBox.IsDropDownOpen = false;
        Dispatcher.BeginInvoke(new Action(HandleAddDisplayUnit));
    }

    private void HandleAddDisplayUnit()
    {
        if (_loading) return;
        var vyd = (VydBox.SelectedItem as string ?? "").Trim();
        if (NomenclatureVydCatalog.IsNewItem(vyd)) vyd = "";
        // Без Виду теж дозволяємо задати О.В. відображення на цю картку
        // (у каталог виду не пишемо — AddDisplayUnitForVyd поверне null).
        var already = AppStore.Instance.GetDisplayUnitsForVyd(vyd);
        var dlg = new VydDisplayUnitPickWindow(vyd, AppStore.Instance.Data.Nomenclatures, already)
        {
            Owner = this
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ChosenUnit))
        {
            var unit = AppStore.Instance.AddDisplayUnitForVyd(vyd, dlg.ChosenUnit);
            _loading = true;
            FillDisplayUnitCombo(vyd, unit ?? dlg.ChosenUnit);
            _loading = false;
        }
        else
        {
            _loading = true;
            DisplayUnitBox.SelectedIndex = 0;
            _loading = false;
        }
    }

    /// <summary>
    /// Після збереження картки: якщо є інші без Виду зі схожою назвою — діалог.
    /// Якщо за контекстом нікого — мовчки. Виклик з SaveClick (не з SelectionChanged).
    /// </summary>
    private void OfferBulkAssignVyd(string selectedVyd, Guid excludeId)
    {
        try
        {
            CrashLog.Write("OfferBulkAssignVyd.enter", detail: "vyd=" + selectedVyd);
            AppStore.Instance.EnsureNomenclatures();
            var others = NomenclatureWithoutVyd.Others(AppStore.Instance.Data.Nomenclatures, excludeId);
            var matched = NomenclatureVydNameMatch.FilterByName(
                others, selectedVyd, AppStore.Instance.Data.VydKeywords);
            CrashLog.Write("OfferBulkAssignVyd.others",
                detail: $"all={others.Count} matched={matched.Count}");
            if (matched.Count == 0) return;

            VydBox.IsDropDownOpen = false;
            CrashLog.Write("OfferBulkAssignVyd.ShowDialog");
            var dlg = new AssignVydBulkWindow(others, matched, selectedVyd) { Owner = this };
            dlg.ShowDialog();
            CrashLog.Write("OfferBulkAssignVyd.ShowDialog.done");
        }
        catch (Exception ex)
        {
            CrashLog.Write("OfferBulkAssignVyd", ex);
            MessageBox.Show("Помилка діалогу масового призначення:\n" + ex.Message, "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    internal static bool TryParseFactor(string? text, out decimal? factor)
    {
        factor = null;
        var t = (text ?? "").Trim();
        if (t.Length == 0) return true;
        if (decimal.TryParse(t, NumberStyles.Any, Ua.Culture, out var v)
            || decimal.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out v))
        {
            if (v <= 0) return false;
            factor = v;
            return true;
        }
        return false;
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = (NameBox.Text ?? "").Trim();
            if (name.Length == 0)
            {
                MessageBox.Show("Вкажіть назву.", "Обозний", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var unit = string.IsNullOrWhiteSpace(UnitBox.Text) ? "шт" : UnitBox.Text.Trim();
            var kind = NomenclatureKindNames.Parse(KindBox.SelectedItem as string);
            var note = NoteBox.Text ?? "";
            var vyd = (VydBox.SelectedItem as string ?? "").Trim();
            if (NomenclatureVydCatalog.IsNewItem(vyd)) vyd = "";

            var displayUnit = (DisplayUnitBox.SelectedItem as string ?? "").Trim();
            if (VydDisplayUnitHelper.IsAddItem(displayUnit)) displayUnit = "";

            if (!TryParseFactor(FactorBox.Text, out var factor))
            {
                MessageBox.Show("Коефіцієнт має бути додатним числом або порожнім.",
                    "Обозний", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Guid savedId;
            if (_id is Guid id)
            {
                AppStore.Instance.UpdateNomenclature(
                    id, name, unit, kind, note, vyd,
                    displayUnit, factor, updateDisplayFields: true);
                savedId = id;
            }
            else
            {
                var created = AppStore.Instance.AddNomenclature(
                    name, unit, kind, note, vyd, displayUnit, factor);
                savedId = created.Id;
            }

            if (vyd.Length > 0 && _vydChangedByUser)
                OfferBulkAssignVyd(vyd, savedId);

            DialogResult = true;
        }
        catch (Exception ex)
        {
            CrashLog.Write("SaveClick", ex);
            MessageBox.Show("Не вдалося зберегти:\n" + ex.Message, "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
