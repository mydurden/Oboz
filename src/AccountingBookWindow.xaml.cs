using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Obozny;

public partial class AccountingBookWindow : Window
{
    private const string EmptyFilterStockText =
        "Відсутній залишок цього виду майна на складі. Вимкніть «Фільтр по наявності», щоб побачити всі зафіксовані операції по цьому виду майна.";
    private const string EmptyKindText =
        "У цьому виді немає позицій і проведених рухів.";

    private DateTime _periodFrom;
    private DateTime _periodTo;
    private string? _openVyd;
    private AccountingBookSection? _fullSection;
    private AccountingBookSection? _viewSection;
    private readonly AccountingBookStickySurface _sticky = new();
    private readonly AccountingBookBodySurface _body = new();
    private bool _scrollHooked;
    private bool _headerPinned;
    private bool _ignorePinScroll;
    private bool _periodUiReady;
    private double _contentLeftPad;
    /// <summary>Стан «Фільтр по наявності» — не лише IsChecked (надійніше при Visible/Collapsed).</summary>
    private bool _presenceFilterOn;
    private bool _syncingPresenceFilterUi;

    public AccountingBookWindow()
    {
        InitializeComponent();
        StickyCanvas.Children.Add(_sticky);
        Canvas.SetLeft(_sticky, 0);
        Canvas.SetTop(_sticky, 0);
        SectionGrid.Children.Add(_body);
        SectionTableHost.PreviewMouseWheel += ForwardWheelToSectionScroll;

        _periodFrom = AccountingBook.DefaultPeriodFrom();
        _periodTo = AccountingBook.DefaultPeriodTo();
        PeriodFromBox.SelectedDate = _periodFrom;
        PeriodToBox.SelectedDate = _periodTo;
        _periodUiReady = true;
        RefreshToc();
    }

    private void PeriodChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_periodUiReady) return;
        if (PeriodFromBox.SelectedDate is DateTime from)
            _periodFrom = from.Date;
        if (PeriodToBox.SelectedDate is DateTime to)
            _periodTo = to.Date;
        // У розділі дати з Змісту вже застосовані; зміна періоду — лише на екрані Змісту.
        if (TocHost.Visibility == Visibility.Visible)
            RefreshToc();
    }

    private void RefreshToc()
    {
        var data = AppStore.Instance.Data;
        var toc = AccountingBook.BuildToc(
            data.Nomenclatures,
            data.Properties,
            data.Movements,
            data.Documents,
            _periodFrom,
            _periodTo);
        TocList.ItemsSource = toc;
        if (toc.Count == 0)
        {
            SubtitleBlock.Text =
                "Немає видів із проведеними рухами за обраний період. Змініть Від/До або перевірте проведення документів.";
        }
        else
        {
            SubtitleBlock.Text =
                "Оберіть вид майна, щоб відкрити розділ книги. Лише перегляд.";
        }
    }

    private void TocItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Content is not string vyd) return;
        OpenSection(vyd);
    }

    private void BackClick(object sender, RoutedEventArgs e) => ShowToc();

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private void SummaryStatementClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new SummaryStatementMonthPickWindow { Owner = this };
            if (dlg.ShowDialog() != true) return;
            if (!SummaryStatement.TryPeriodFromMonths(
                    dlg.SelectedYear,
                    dlg.SelectedMonths,
                    out var from,
                    out var to,
                    out var error))
            {
                MessageBox.Show(this, error ?? "Некоректний період.", "Обозний",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var data = AppStore.Instance.Data;
            var report = SummaryStatement.Build(
                data.Properties,
                data.Movements,
                data.Documents,
                from,
                to,
                statementNumber: dlg.StatementNumber,
                unitName: data.Settings.UnitName,
                userName: data.Settings.UserName,
                compiledAt: DateTime.Today,
                supplyService: dlg.SelectedSupplyService);

            if (report.Rows.Count == 0)
            {
                MessageBox.Show(this,
                    "За обраний період і службу забезпечення немає майна з рухом або залишком для відомості.",
                    "Обозний",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Екран першим: ClosedXML/OpenXml лише з кнопки «Зберегти в Excel» у вікні відомості.
            new SummaryStatementViewWindow(report) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            CrashLog.Write("AccountingBookWindow.SummaryStatementClick", ex);
            MessageBox.Show(this,
                "Не вдалося сформувати узагальнюючу відомість.\n" +
                (string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message) +
                "\n\nДеталі: " + CrashLog.LogPath,
                "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowToc()
    {
        _openVyd = null;
        _fullSection = null;
        _viewSection = null;
        // Скидаємо фільтр при виході в Зміст — наступний розділ стартує без нього.
        _presenceFilterOn = false;
        SetPresenceFilterChecked(false);
        PresenceFilterBtn.Visibility = Visibility.Collapsed;
        TitleBlock.Text = "Зміст";
        SubtitleBlock.Visibility = Visibility.Visible;
        BackBtn.Visibility = Visibility.Collapsed;
        TocHost.Visibility = Visibility.Visible;
        SectionHost.Visibility = Visibility.Collapsed;
        ClearSurfaces();
        RefreshToc();
    }

    private void ClearSurfaces()
    {
        _sticky.Clear();
        _body.Clear();
        _body.Margin = new Thickness(0);
        _headerPinned = false;
        _contentLeftPad = 0;
        Canvas.SetLeft(_sticky, 0);
        StickyCanvas.Height = 0;
        StickyCanvas.Width = 0;
    }

    private void OpenSection(string vyd)
    {
        _openVyd = vyd;
        var data = AppStore.Instance.Data;
        _fullSection = AccountingBook.BuildSection(
            vyd,
            data.Properties,
            data.Nomenclatures,
            data.Movements,
            data.Documents,
            data.Settings.UnitName,
            _periodFrom,
            _periodTo);

        TitleBlock.Text = _fullSection.Vyd;
        SubtitleBlock.Text = "(найменування військового майна — перегляд розділу книги)";
        SubtitleBlock.Visibility = Visibility.Visible;
        BackBtn.Visibility = Visibility.Visible;
        PresenceFilterBtn.Visibility = Visibility.Visible;
        TocHost.Visibility = Visibility.Collapsed;
        SectionHost.Visibility = Visibility.Visible;

        ApplySectionView(resetScroll: true);
    }

    private void PresenceFilterChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPresenceFilterUi) return;
        _presenceFilterOn = PresenceFilterBtn.IsChecked == true;
        if (_fullSection == null) return;
        ApplySectionView(resetScroll: false);
    }

    private void SetPresenceFilterChecked(bool on)
    {
        if (PresenceFilterBtn.IsChecked == on) return;
        _syncingPresenceFilterUi = true;
        try { PresenceFilterBtn.IsChecked = on; }
        finally { _syncingPresenceFilterUi = false; }
    }

    private void ApplySectionView(bool resetScroll)
    {
        if (_fullSection == null) return;
        SetPresenceFilterChecked(_presenceFilterOn);
        _viewSection = AccountingBook.ApplyPresenceFilter(_fullSection, _presenceFilterOn);
        if (resetScroll)
            _headerPinned = false;
        BuildSectionTable(_viewSection);
        if (resetScroll)
        {
            SectionScroll.ScrollToHome();
            SyncStickyHorizontal();
        }
        else
        {
            SyncStickyHorizontal();
            SyncHeaderPin();
        }
    }

    private void BuildSectionTable(AccountingBookSection section)
    {
        // Порожній повний розділ або фільтр зрізав усі колонки з становить=0.
        if (section.IsEmpty || section.Columns.Count == 0)
        {
            EmptyHint.Text = section.IsEmpty ? EmptyKindText : EmptyFilterStockText;
            EmptyHint.Visibility = Visibility.Visible;
            SectionTableHost.Visibility = Visibility.Collapsed;
            ClearSurfaces();
            return;
        }

        EmptyHint.Visibility = Visibility.Collapsed;
        SectionTableHost.Visibility = Visibility.Visible;

        EnsureScrollHooks();
        ApplyLayoutWidths(section);
        // StickyClip = ViewportWidth; Canvas.Left = pad − H (+ залишок vs тіло).
        // Тіло: OnRender + viewport cull; Margin.Left = pad для центру. Без stretch qty.
        BindSurfaces(section);
        SyncStickyHorizontal();
        // Після arrange / появи бігунка — ще раз (фільтр на широкому вікні).
        Dispatcher.BeginInvoke(DispatcherPriority.Render, SyncStickyHorizontal);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, SyncStickyHorizontal);
    }

    private void ApplyLayoutWidths(AccountingBookSection section)
    {
        // В’юпорт тіла = видима ширина без вертикального бігунка.
        // Не брати ActualWidth StickyClip/хоста — там є місце бігунка; pad від нього
        // зсуває sticky відносно тіла на ~sb/2 на широкому вікні з малою таблицею.
        var viewport = ResolveBodyViewportWidth();
        AccountingBookLayout.UpdateQtyColWidth(section.Columns.Count, viewport);
        _contentLeftPad = AccountingBookLayout.ContentLeftPad(section.Columns.Count, viewport);
        _body.Margin = new Thickness(_contentLeftPad, 0, 0, 0);
        MatchStickyClipToViewport(viewport);
    }

    /// <summary>
    /// Ширина, у якій центрується тіло: ViewportWidth (без V-бігунка).
    /// ActualWidth лише як останній запасний варіант до першого layout.
    /// </summary>
    private double ResolveBodyViewportWidth()
    {
        var viewport = SectionScroll.ViewportWidth;
        if (viewport > 0) return viewport;
        // Якщо бігунок уже є в ActualWidth — віднімаємо жолоб, щоб не взяти sb у pad.
        var actual = SectionScroll.ActualWidth;
        if (actual > 0)
        {
            var gutter = SystemParameters.VerticalScrollBarWidth;
            if (SectionScroll.ComputedVerticalScrollBarVisibility == Visibility.Visible)
                return Math.Max(0, actual - gutter);
            return actual;
        }
        return SectionTableHost.ActualWidth;
    }

    /// <summary>
    /// StickyClip = ширина в’юпорта тіла (ліворуч), а не вся клітинка з жолобом бігунка.
    /// Інакше при pad&gt;0 (фільтр → мало колонок на широкому екрані) шапка їде правіше за тіло.
    /// </summary>
    private void MatchStickyClipToViewport(double viewport)
    {
        if (viewport <= 0)
        {
            StickyClip.Width = double.NaN;
            StickyClip.HorizontalAlignment = HorizontalAlignment.Stretch;
            StickyClip.Margin = new Thickness(0);
            return;
        }
        StickyClip.HorizontalAlignment = HorizontalAlignment.Left;
        StickyClip.Width = viewport;
        StickyClip.Margin = new Thickness(0);
    }

    private void EnsureScrollHooks()
    {
        if (_scrollHooked) return;
        _scrollHooked = true;
        SectionScroll.ScrollChanged += (_, e) =>
        {
            if (_ignorePinScroll) return;
            if (e.HorizontalChange != 0 || e.ExtentWidthChange != 0 || e.ViewportWidthChange != 0)
            {
                SyncStickyHorizontal();
                // Зміна в’юпорта → можливо інший pad (центр / H-скрол).
                if (e.ViewportWidthChange != 0 && _viewSection != null
                    && !_viewSection.IsEmpty && _viewSection.Columns.Count > 0)
                {
                    ApplyLayoutWidths(_viewSection);
                    BindSurfaces(_viewSection);
                }
                _body.InvalidateVisual();
            }
            if (e.VerticalChange != 0)
            {
                SyncHeaderPin();
                _body.InvalidateVisual();
            }
        };
        SectionScroll.SizeChanged += (_, _) =>
        {
            if (_ignorePinScroll) return;
            if (_viewSection == null || _viewSection.IsEmpty || _viewSection.Columns.Count == 0)
                return;
            ApplyLayoutWidths(_viewSection);
            BindSurfaces(_viewSection);
            SyncStickyHorizontal();
            _body.InvalidateVisual();
        };
    }

    private void BindSurfaces(AccountingBookSection section)
    {
        _sticky.Bind(section, _headerPinned);
        _body.Bind(section, SectionScroll, SectionScroll);
        StickyCanvas.Height = AccountingBookLayout.StickySurfaceHeight(_headerPinned);
        var viewport = ResolveBodyViewportWidth();
        MatchStickyClipToViewport(viewport);
        var content = AccountingBookLayout.ContentWidth(section.Columns.Count);
        StickyCanvas.Width = Math.Max(viewport, content + _contentLeftPad);
    }

    private void SyncHeaderPin()
    {
        if (_viewSection == null || _viewSection.IsEmpty) return;
        var v = SectionScroll.VerticalOffset;
        var want = AccountingBookLayout.ShouldPinTriplet(v, _headerPinned);
        if (want == _headerPinned) return;

        _headerPinned = want;
        _ignorePinScroll = true;
        try
        {
            BindSurfaces(_viewSection);
            var delta = AccountingBookLayout.MoveRowH;
            var next = want ? v + delta : Math.Max(0, v - delta);
            SectionScroll.ScrollToVerticalOffset(next);
        }
        finally
        {
            _ignorePinScroll = false;
        }
    }

    private void SyncStickyHorizontal()
    {
        var h = SectionScroll.HorizontalOffset;
        var viewport = ResolveBodyViewportWidth();
        MatchStickyClipToViewport(viewport);

        // База: той самий pad, що й Margin тіла, мінус H-зсув.
        // StickyClip = ViewportWidth (без жолоба бігунка) — спільна геометрія з тілом.
        var left = AccountingBookLayout.StickyCanvasLeft(_contentLeftPad, h);
        Canvas.SetLeft(_sticky, left);
        _sticky.SetHorizontalOffset(h);

        // Залишок після arrange/DPI: міряємо вже з виставленим Canvas.Left.
        if (_body.IsVisible && _sticky.IsVisible && _body.ActualWidth > 0 && _sticky.ActualWidth > 0)
        {
            try
            {
                StickyCanvas.UpdateLayout();
                var bodyOrigin = _body.TranslatePoint(new Point(0, 0), SectionTableHost);
                var stickyOrigin = _sticky.TranslatePoint(new Point(0, 0), SectionTableHost);
                var dx = bodyOrigin.X - stickyOrigin.X;
                if (!double.IsNaN(dx) && !double.IsInfinity(dx)
                    && Math.Abs(dx) > 0.05 && Math.Abs(dx) < 48)
                    Canvas.SetLeft(_sticky, left + dx);
            }
            catch (InvalidOperationException)
            {
                // Ще не в visual tree — лишаємо pad − h.
            }
        }
    }

    private void ForwardWheelToSectionScroll(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0) return;
        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            SectionScroll.ScrollToHorizontalOffset(SectionScroll.HorizontalOffset - e.Delta);
            e.Handled = true;
            return;
        }
        // Колесо над sticky не доходить до SectionScroll — прокинути вертикаль.
        if (e.OriginalSource is DependencyObject src && IsUnder(src, StickyClip))
        {
            SectionScroll.ScrollToVerticalOffset(SectionScroll.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private static bool IsUnder(DependencyObject? node, DependencyObject ancestor)
    {
        while (node != null)
        {
            if (ReferenceEquals(node, ancestor)) return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }
}
