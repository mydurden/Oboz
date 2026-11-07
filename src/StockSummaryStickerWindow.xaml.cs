using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Obozny;

public partial class StockSummaryStickerWindow : Window
{
    private readonly Action<StockSummaryStickerState> _onChanged;
    private List<string>? _enabledVyds;
    private Dictionary<string, string> _displayUnitByVyd = new(StringComparer.Ordinal);
    private bool _loading;
    private DispatcherTimer? _saveTimer;

    public StockSummaryStickerWindow(StockSummaryStickerState state, Action<StockSummaryStickerState> onChanged)
    {
        _onChanged = onChanged;
        InitializeComponent();
        StickerShell.Attach(this);
        ApplyFromState(state);
        Refresh();
    }

    private bool _allowClose;

    public void PrepareClose() => _allowClose = true;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        StickerShell.HideInsteadOfClose(this, e, _allowClose);
        if (!e.Cancel) base.OnClosing(e);
    }

    public void ApplyFromState(StockSummaryStickerState state)
    {
        _loading = true;
        _enabledVyds = state.EnabledVyds == null ? null : state.EnabledVyds.ToList();
        _displayUnitByVyd = new Dictionary<string, string>(
            VydDisplayUnitHelper.ToDisplayMap(state.SummaryDisplayUnits),
            StringComparer.Ordinal);
        Place(state.Left, state.Top, state.Width, state.Height, 360, 300);
        ApplyColors();
        _loading = false;
    }

    private void ApplyColors()
    {
        var sw = StickerColors.Get("jade");
        Root.Background = BrushOf(sw.Body);
        ChromeBar.Background = BrushOf(sw.Accent);
        CollapseBtn.Foreground = BrushOf(sw.Ink);
        SettingsBtn.Foreground = BrushOf(sw.Ink);
    }

    private static SolidColorBrush BrushOf(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    public void Refresh()
    {
        RowsPanel.Children.Clear();
        if (StockSummaryByVyd.AllVydsDisabled(_enabledVyds))
        {
            RowsPanel.Children.Add(new TextBlock
            {
                Text = "Увімкніть види в налаштуваннях (шестерня).",
                FontSize = 13,
                Foreground = BrushOf("#2C241F"),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75
            });
            return;
        }

        var rows = ComputeRows();
        if (rows.Count == 0)
        {
            RowsPanel.Children.Add(new TextBlock
            {
                Text = "Немає залишків з обраним Видом.",
                FontSize = 13,
                Foreground = BrushOf("#2C241F"),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75
            });
            return;
        }

        // Три колонки без рамок/сітки: Вид | од. вим. | кількість (спочатку од. вим.).
        Grid.SetIsSharedSizeScope(RowsPanel, true);
        RowsPanel.Children.Add(MakeSummaryHeaderRow());
        foreach (var row in rows)
            RowsPanel.Children.Add(MakeSummaryDataRow(row.Vyd, row.Unit, row.Quantity));
    }

    private IReadOnlyList<StockSummaryRow> ComputeRows()
    {
        var data = AppStore.Instance.Data;
        var byId = data.Nomenclatures.ToDictionary(n => n.Id);
        return StockSummaryByVyd.Compute(
            data.Properties,
            byId,
            _enabledVyds,
            data.CustomVyds,
            _displayUnitByVyd.Count > 0 ? _displayUnitByVyd : null);
    }

    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        var dlg = new StockSummaryVydFilterWindow(_enabledVyds, _displayUnitByVyd)
        {
            Owner = Owner ?? this
        };
        if (dlg.ShowDialog() != true) return;
        // Усі з каталогу (фікс+custom) → null (нові види теж з’являться).
        // Порожній список → [] (нотатка з підказкою) — валідне збереження.
        var selected = dlg.SelectedVyds;
        var catalog = NomenclatureVydCatalog.Combined(AppStore.Instance.Data.CustomVyds);
        var allOn = selected.Count == catalog.Count
            && catalog.All(v => selected.Contains(v));
        _enabledVyds = allOn ? null : selected.ToList();
        _displayUnitByVyd = new Dictionary<string, string>(
            dlg.SelectedDisplayUnits, StringComparer.Ordinal);
        PersistNow(force: true);
        Refresh();
    }

    private static Grid MakeSummaryHeaderRow()
    {
        var grid = MakeThreeColGrid(marginBottom: 6);
        AddColText(grid, 0, "Вид", header: true, wrap: false);
        AddColText(grid, 1, "од. вим.", header: true, wrap: false, rightPad: 10);
        AddColText(grid, 2, "кількість", header: true, wrap: false);
        return grid;
    }

    private Grid MakeSummaryDataRow(string vyd, string unit, decimal qty)
    {
        var ink = BrushOf("#2C241F");
        var grid = MakeThreeColGrid(marginBottom: 8);
        AddColText(grid, 0, vyd, header: false, wrap: true, brush: ink);
        AddColText(grid, 1, unit, header: false, wrap: false, brush: ink, rightPad: 10);
        AddColText(grid, 2, Ua.FormatQty(qty), header: false, wrap: false, brush: ink, bold: true);
        return grid;
    }

    private static Grid MakeThreeColGrid(double marginBottom)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, marginBottom) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
            SharedSizeGroup = "StockUnit"
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
            SharedSizeGroup = "StockQty"
        });
        return grid;
    }

    private static void AddColText(
        Grid grid,
        int column,
        string text,
        bool header,
        bool wrap,
        Brush? brush = null,
        double rightPad = 0,
        bool bold = false)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = header ? 12 : 14,
            FontWeight = header || bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = brush ?? BrushOf("#2C241F"),
            Opacity = header ? 0.7 : 1,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, rightPad, 0)
        };
        if (column > 0)
            tb.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(tb, column);
        grid.Children.Add(tb);
    }

    private void OnActivated(object sender, EventArgs e) { }

    private void OnDeactivated(object sender, EventArgs e) => PersistNow();

    private void CollapseClick(object sender, RoutedEventArgs e) => StickerShell.SafeHide(this);

    private void AccentDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1 || IsButton(e.OriginalSource as DependencyObject)) return;
        Activate();
        try { DragMove(); } catch { /* ignore */ }
    }

    private static bool IsButton(DependencyObject? src)
    {
        while (src != null)
        {
            if (src is Button) return true;
            src = VisualTreeHelper.GetParent(src);
        }
        return false;
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (_loading || !IsLoaded) return;
        PersistSoon();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        PersistSoon();
    }

    private void PersistSoon()
    {
        _saveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Stop();
        _saveTimer.Tick -= SaveTick;
        _saveTimer.Tick += SaveTick;
        _saveTimer.Start();
    }

    private void SaveTick(object? sender, EventArgs e)
    {
        _saveTimer?.Stop();
        PersistNow();
    }

    private void PersistNow(bool force = false)
    {
        if (_loading) return;
        if (!force)
        {
            if (WindowState != WindowState.Normal || !IsVisible) return;
            if (!StickerShell.IsUsableSize(ActualWidth, ActualHeight)) return;
        }

        var w = ActualWidth > 0 ? ActualWidth : Width;
        var h = ActualHeight > 0 ? ActualHeight : Height;
        if (!StickerShell.IsUsableSize(w, h))
        {
            w = Width;
            h = Height;
        }

        _onChanged(new StockSummaryStickerState
        {
            Left = Left,
            Top = Top,
            Width = w,
            Height = h,
            EnabledVyds = _enabledVyds == null ? null : _enabledVyds.ToList(),
            SummaryDisplayUnits = _displayUnitByVyd.Count == 0
                ? null
                : VydDisplayUnitHelper.FromDisplayMap(_displayUnitByVyd)
        });
    }

    private void Place(double left, double top, double width, double height, double defW, double defH)
    {
        var area = SystemParameters.WorkArea;
        if (width < MinWidth || width > area.Width * 0.92) width = defW;
        if (height < MinHeight || height > area.Height * 0.92) height = defH;
        var screenLeft = SystemParameters.VirtualScreenLeft;
        var screenTop = SystemParameters.VirtualScreenTop;
        var screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
        var screenBottom = screenTop + SystemParameters.VirtualScreenHeight;
        if (left < screenLeft || left > screenRight - 48) left = area.Left + 24;
        if (top < screenTop || top > screenBottom - 48) top = area.Top + 24;
        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }
}
