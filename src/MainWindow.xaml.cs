using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Obozny;

public partial class MainWindow : Window
{
    private bool _opsOpen;
    private const int OpsAnimMs = 220;
    private bool _showReserved;
    private ViewMode _view = ViewMode.Stock;
    private Document? _draft;
    /// <summary>У чернетці: чи показувати в переліку для додавання також основні засоби (старт — ні).</summary>
    private bool _draftIncludeOz;
    private string _draftStamp = "";
    private Point _dragStart;
    private PropertyRow? _dragRow;
    private bool _fingerDrag;
    private TouchDevice? _dragTouch;
    private readonly DispatcherTimer _qtyHoldTimer = new() { Interval = TimeSpan.FromMilliseconds(380) };
    private DocumentLine? _qtyHoldLine;
    private TouchDevice? _qtyHoldTouch;
    private bool _qtyHoldRepeating;
    private DocumentLine? _draftDragLine;
    private Point _draftDragStart;
    private bool _draftFingerDrag;
    private TouchDevice? _draftDragTouch;
    private string _sortKey = "";
    private int _sortState; // 0 none, 1 asc, 2 desc
    private readonly Dictionary<string, string> _filters = new();
    private readonly Dictionary<string, TextBlock> _totalBlocks = new();
    private List<PropertyRow> _shownRows = new();
    private readonly Dictionary<string, TextBox> _filterBoxes = new();
    private readonly Dictionary<string, Button> _sortButtons = new();
    private bool _buildingColumns;
    private bool _syncDraftDate;
    private bool _syncCommissionDate;
    private bool _applyingLayout;
    private bool _resizingColumns;
    private DataGridColumn? _resizeColumn;
    private DataGridColumn? _resizePartner;
    private double _resizeColumnStart;
    private double _resizePairWidth;
    private double _resizeAccumulated;
    private readonly Dictionary<string, double> _sessionColumnWeights = new();
    private StickerHost? _stickers;

    public MainWindow()
    {
        InitializeComponent();
        _qtyHoldTimer.Tick += QtyHoldTick;
        _stickers = new StickerHost(this);
        ThemeService.Changed += OnThemeChanged;
        UpdateHeaderLogo();
        ApplyDraftFades();
        AppStore.Instance.Changed += RefreshAll;
        Loaded += (_, _) =>
        {
            ApplyColumnLayout();
            _stickers?.Start();
            SyncStickerButtons();
            Dispatcher.BeginInvoke(ShowStartupSummary, DispatcherPriority.ApplicationIdle);
        };
        PropertyGrid.SizeChanged += (_, e) =>
        {
            // лише зміна ширини — без LayoutUpdated (він смикав кнопки/фільтри на кожному кадрі)
            if (e.WidthChanged) ApplyColumnLayout();
        };
        PropertyGrid.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnColumnThumbDragStarted), true);
        PropertyGrid.AddHandler(Thumb.DragDeltaEvent, new DragDeltaEventHandler(OnColumnThumbDragDelta), true);
        PropertyGrid.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnColumnThumbDragCompleted), true);
        PropertyGrid.AddHandler(Control.MouseDoubleClickEvent, new MouseButtonEventHandler(OnColumnGripperDoubleClick), true);
        PropertyGrid.AddHandler(DataGridColumnHeader.ClickEvent, new RoutedEventHandler(OnColumnHeaderClick));
        PreviewMouseDown += MainWindowPreviewMouseDown;
        RefreshHeader();
        BuildColumns();
        RefreshAll();
    }

    private void ShowStartupSummary()
    {
        if (!IsVisible) return;
        // Без резерву під чернетки стартове нагадування не потрібне.
        var summary = AppStore.Instance.GetStartupSummary();
        if (summary.ReservedPositions <= 0) return;
        var dlg = new SummaryInfoWindow { Owner = this };
        dlg.ShowDialog();
    }

    /// <summary>Клік поза полем назви споруди — завершити введення.</summary>
    private void MainWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.FocusedElement is not TextBox box) return;
        if (!ReferenceEquals(FindParent<Canvas>(box), StructuresStrip)) return;
        if (e.OriginalSource is DependencyObject d && (ReferenceEquals(d, box) || box.IsAncestorOf(d)))
            return;
        EndStructureNameEditing();
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded || !e.WidthChanged) return;
        ApplyColumnLayout();
        UpdateDraftFrameHighlight();
    }

    private void DraftNumberGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (!string.Equals(box.Text.Trim(), DocTypeNames.Numberless, StringComparison.OrdinalIgnoreCase))
            return;
        box.Text = "";
    }

    private void DraftNumberLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox box) return;
        box.Text = DocTypeNames.FormatNumber(box.Text);
        if (_draft != null)
            _draft.Number = DocTypeNames.NormalizeNumber(box.Text);
    }

    private void DraftDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncDraftDate || _draft == null || DraftDate.SelectedDate == null) return;
        var oldDate = _draft.Date;
        _draft.Date = DraftDate.SelectedDate.Value.Date;
        if (_draft.Type == DocType.QualityState
            && (_draft.CommissioningDate == null || _draft.CommissioningDate.Value.Date == oldDate.Date))
        {
            _draft.CommissioningDate = _draft.Date;
            _syncCommissionDate = true;
            CommissioningDateBox.SelectedDate = _draft.CommissioningDate;
            _syncCommissionDate = false;
        }
        AppStore.Instance.RecalcAnachronistic(_draft);
        RefreshDraftLines();
    }

    private void CommissioningDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncCommissionDate || _draft == null) return;
        _draft.CommissioningDate = CommissioningDateBox.SelectedDate?.Date;
    }

    private void RefreshHeader()
    {
        var s = AppStore.Instance.Data.Settings;
        TitleBlock.Text = (s.UnitName ?? "").Trim();
        ApplyHeaderContext();
    }

    /// <summary>Центр рядка заголовку: назва КАПСОМ в один рядок + світліша підказка нижче.</summary>
    private void ApplyHeaderContext()
    {
        string title;
        string? subtitle = null;

        if (_draft != null)
        {
            if (_draft.Type == DocType.QualityState)
            {
                title = "Підготовка Актів якісного стану та виконаних робіт";
                var work = (_draft.WorkTypeName ?? "").Trim();
                if (!string.IsNullOrEmpty(work))
                    subtitle = work;
            }
            else if (_draft.Type == DocType.QualityChange)
            {
                title = "Підготовка Актів зміни якісного стану та виконаних робіт";
                var work = (_draft.WorkTypeName ?? "").Trim();
                if (!string.IsNullOrEmpty(work))
                    subtitle = work;
            }
            else
            {
                title = DocTypeNames.Title(_draft.Type);
                var work = (_draft.WorkTypeName ?? "").Trim();
                if (!string.IsNullOrEmpty(work))
                    subtitle = work;
            }
        }
        else
        {
            title = _view switch
            {
                ViewMode.Movements => "Рух майна",
                ViewMode.Strongpoints => "Облік опорних пунктів",
                ViewMode.FixedAssets => "Основні засоби",
                _ => "Залишки на складі та зарезервоване майно"
            };
            subtitle = _view switch
            {
                ViewMode.Movements => "Списання, отримання, переміщення, повернення",
                ViewMode.Strongpoints => "Що утримуються підрозділом",
                ViewMode.FixedAssets => "Майно з типом номенклатури «Основні засоби»",
                _ => "Запаси підрозділу (без основних засобів)"
            };
        }

        TableTitleBlock.Text = CultureInfo.GetCultureInfo("uk-UA").TextInfo.ToUpper(title);
        if (string.IsNullOrWhiteSpace(subtitle))
        {
            TableSubtitleBlock.Text = "";
            TableSubtitleBlock.Visibility = Visibility.Collapsed;
        }
        else
        {
            TableSubtitleBlock.Text = subtitle;
            TableSubtitleBlock.Visibility = Visibility.Visible;
        }
    }

    private void HeaderClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d &&
            (IsInside(d, ProfileBtn) || IsInside(d, StickersBtn) || IsInside(d, AddStickerBtn)))
            return;
        if (_draft != null) return;
        SetOpsMenuOpen(!_opsOpen);
    }

    /// <summary>Згорнути головне меню після вибору пункту (знову відкрити — клік по шапці).</summary>
    private void CollapseOpsMenu() => SetOpsMenuOpen(false);

    private void SetOpsMenuOpen(bool open)
    {
        _opsOpen = open;
        AnimateOpsPanel(open);
    }

    /// <summary>Плавна висота (~220 мс) + легкий fade; без стрибка контенту під меню.</summary>
    private void AnimateOpsPanel(bool open)
    {
        OpsPanel.BeginAnimation(FrameworkElement.HeightProperty, null);
        OpsPanel.BeginAnimation(UIElement.OpacityProperty, null);

        if (open)
        {
            var from = OpsPanel.Visibility == Visibility.Visible && OpsPanel.ActualHeight > 1
                ? OpsPanel.ActualHeight
                : 0;

            OpsPanel.Visibility = Visibility.Visible;
            OpsPanel.Height = double.NaN;
            OpsPanel.UpdateLayout();
            var width = OpsPanel.ActualWidth > 0 ? OpsPanel.ActualWidth : Math.Max(ActualWidth, 1);
            OpsPanel.Measure(new Size(width, double.PositiveInfinity));
            var target = OpsPanel.DesiredSize.Height;
            if (target < 1) target = 1;

            OpsPanel.Height = from;
            if (from <= 0) OpsPanel.Opacity = 0;

            var hAnim = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(OpsAnimMs))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            hAnim.Completed += (_, _) =>
            {
                if (!_opsOpen) return;
                OpsPanel.BeginAnimation(FrameworkElement.HeightProperty, null);
                OpsPanel.Height = double.NaN;
            };

            var oAnim = new DoubleAnimation(OpsPanel.Opacity, 1, TimeSpan.FromMilliseconds(OpsAnimMs * 0.85))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            oAnim.Completed += (_, _) =>
            {
                if (!_opsOpen) return;
                OpsPanel.BeginAnimation(UIElement.OpacityProperty, null);
                OpsPanel.Opacity = 1;
            };

            OpsPanel.BeginAnimation(FrameworkElement.HeightProperty, hAnim);
            OpsPanel.BeginAnimation(UIElement.OpacityProperty, oAnim);
            return;
        }

        if (OpsPanel.Visibility != Visibility.Visible)
        {
            OpsPanel.Height = double.NaN;
            OpsPanel.Opacity = 1;
            return;
        }

        var collapseFrom = OpsPanel.ActualHeight;
        if (collapseFrom < 1)
        {
            OpsPanel.Visibility = Visibility.Collapsed;
            OpsPanel.Height = double.NaN;
            OpsPanel.Opacity = 1;
            return;
        }

        OpsPanel.Height = collapseFrom;

        var collapseH = new DoubleAnimation(collapseFrom, 0, TimeSpan.FromMilliseconds(OpsAnimMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.Stop
        };
        collapseH.Completed += (_, _) =>
        {
            if (_opsOpen) return;
            OpsPanel.BeginAnimation(FrameworkElement.HeightProperty, null);
            OpsPanel.BeginAnimation(UIElement.OpacityProperty, null);
            OpsPanel.Visibility = Visibility.Collapsed;
            OpsPanel.Height = double.NaN;
            OpsPanel.Opacity = 1;
        };

        var collapseO = new DoubleAnimation(OpsPanel.Opacity, 0, TimeSpan.FromMilliseconds(OpsAnimMs * 0.85))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.Stop
        };

        OpsPanel.BeginAnimation(FrameworkElement.HeightProperty, collapseH);
        OpsPanel.BeginAnimation(UIElement.OpacityProperty, collapseO);
    }

    private void ToggleStickers(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _stickers?.Toggle();
        SyncStickerButtons();
    }

    private void AddSticker(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _stickers?.AddNew();
        SyncStickerButtons();
    }

    private void SyncStickerButtons()
    {
        var on = _stickers?.Visible == true;
        AddStickerBtn.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        StickersBtn.Opacity = on ? 1.0 : 0.55;
    }

    /// <summary>Після ClearWarehouse: закрити старі вікна стікерів і знову показати (напр. керівні).</summary>
    internal void RelayoutStickersAfterClear()
    {
        _stickers?.CloseAll();
        _stickers?.Start();
        SyncStickerButtons();
        RefreshAll();
    }

    private static bool IsInside(DependencyObject? src, DependencyObject target)
    {
        while (src != null)
        {
            if (ReferenceEquals(src, target)) return true;
            src = VisualTreeHelper.GetParent(src);
        }
        return false;
    }

    private static T? FindChild<T>(DependencyObject? src) where T : DependencyObject
    {
        if (src == null) return null;
        var count = VisualTreeHelper.GetChildrenCount(src);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(src, i);
            if (child is T match) return match;
            var nested = FindChild<T>(child);
            if (nested != null) return nested;
        }
        return null;
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

    private void OpenProfile(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        new ProfileWindow { Owner = this }.ShowDialog();
        ThemeService.ApplyFromSettings();
        RefreshHeader();
    }

    private void OnThemeChanged()
    {
        Dispatcher.Invoke(() =>
        {
            UpdateHeaderLogo();
            ApplyDraftFades();
            PaintViewButtons();
            PaintFilterBoxes();
            if (_draft != null) UpdatePairChrome();
            RefreshHeader();
            if (DraftLines != null) FitDraftUnitColumn();
        });
    }

    private void ApplyDraftFades()
    {
        if (Application.Current.Resources["DraftBg"] is not SolidColorBrush brush) return;
        StructFadeLeft.Background = EdgeFade(brush.Color, fromLeft: true);
        StructFadeRight.Background = EdgeFade(brush.Color, fromLeft: false);
    }

    private static Brush EdgeFade(Color solid, bool fromLeft)
    {
        var clear = solid;
        clear.A = 0;
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(fromLeft ? 0 : 1, 0.5),
            EndPoint = new Point(fromLeft ? 1 : 0, 0.5),
        };
        brush.GradientStops.Add(new GradientStop(solid, 0));
        brush.GradientStops.Add(new GradientStop(clear, 1));
        brush.Freeze();
        return brush;
    }

    private void UpdateHeaderLogo()
    {
        HeaderLogo.Source = new BitmapImage(ThemeService.LogoUri);
    }

    private void AddProperty(object sender, RoutedEventArgs e)
    {
        CollapseOpsMenu();
        new AddPropertyWindow { Owner = this }.ShowDialog();
    }

    private void OpenNomenclature(object sender, RoutedEventArgs e)
    {
        CollapseOpsMenu();
        new NomenclatureListWindow { Owner = this }.ShowDialog();
        RefreshAll();
    }

    private void OpenAccountingBook(object sender, RoutedEventArgs e)
    {
        CollapseOpsMenu();
        new AccountingBookWindow { Owner = this }.ShowDialog();
    }

    private void ToggleFixedAssets(object sender, RoutedEventArgs e)
    {
        CollapseOpsMenu();
        var next = _view == ViewMode.FixedAssets ? ViewMode.Stock : ViewMode.FixedAssets;
        SwitchView(next);
    }

    private void PrepareWriteOff(object sender, RoutedEventArgs e)
    {
        CollapseOpsMenu();
        var pick = new TypePickWindow { Owner = this };
        if (pick.ShowDialog() != true || pick.Selected == null) return;
        var choice = pick.Selected;
        if (DraftListWindow.Count(choice) == 0)
        {
            var workNow = choice.DocType == DocType.Loss ? null : choice.WorkTypeName;
            StartDraft(choice.DocType, workNow);
            return;
        }
        var list = new DraftListWindow(choice) { Owner = this };
        if (list.ShowDialog() != true) return;
        if (list.CreateNew)
        {
            var work = choice.DocType == DocType.Loss ? null : choice.WorkTypeName;
            StartDraft(choice.DocType, work);
            return;
        }
        if (list.Selected != null)
            OpenDraft(list.Selected);
    }

    private void PrepareWarehouseMove(object sender, RoutedEventArgs e)
    {
        CollapseOpsMenu();
        // Одразу повернення на склад служби — без вибору видів переміщення.
        StartDraft(DocType.Return, "На склад служби");
    }

    private void StartDraft(DocType type, string? workTypeName = null)
    {
        var hdr = new DocHeaderWindow(type, workTypeName) { Owner = this };
        if (hdr.ShowDialog() != true) return;
        _draft = AppStore.Instance.CreateDraft(type, hdr.Number, hdr.Date, hdr.Note, workTypeName);
        if (type is DocType.QualityState or DocType.QualityChange)
            _draft.VrActNumber = hdr.VrActNumber;
        CollapseOpsMenu();
        ShowDraft();
        _draftStamp = StampDraft(_draft);
    }

    private void OpenDraftActions(object sender, RoutedEventArgs e)
    {
        if (DraftActionsMenu == null || DraftActionsBtn == null) return;
        PaintDraftOzButton();
        DraftActionsMenu.PlacementTarget = DraftActionsBtn;
        DraftActionsMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        DraftActionsMenu.IsOpen = true;
    }

    private void ToggleDraftOz(object sender, RoutedEventArgs e)
    {
        if (_draft == null) return;
        // IsCheckable вже перемкнув IsChecked до Click — читаємо стан з пункту меню.
        _draftIncludeOz = sender is MenuItem mi ? mi.IsChecked == true : !_draftIncludeOz;
        PaintDraftOzButton();
        // Лише перелік для додавання; рядки вже в документі не чіпаємо.
        RefreshAll();
    }

    private void PaintDraftOzButton()
    {
        if (DraftOzItem == null) return;
        DraftOzItem.IsChecked = _draftIncludeOz;
    }

    private void ShowDraft()
    {
        if (_draft == null)
        {
            DraftPanel.Visibility = Visibility.Collapsed;
            _draftIncludeOz = false;
            PaintDraftOzButton();
            return;
        }
        _draftIncludeOz = false;
        DraftPanel.Visibility = Visibility.Visible;
        PaintDraftOzButton();
        RefreshHeader();
        DraftNumberBox.Text = DocTypeNames.FormatNumber(_draft.Number);
        _syncDraftDate = true;
        DraftDate.SelectedDate = _draft.Date;
        _syncDraftDate = false;

        var qs = _draft.Type == DocType.QualityState;
        var qc = _draft.Type == DocType.QualityChange;
        var pairedVr = qs || qc;
        var visQs = qs ? Visibility.Visible : Visibility.Collapsed;
        var visQc = qc ? Visibility.Visible : Visibility.Collapsed;
        StrongpointNamePanel.Visibility = visQs;
        CommissioningDatePanel.Visibility = visQs;
        StrongpointRegPanel.Visibility = visQs;
        VrActNumberPanel.Visibility = pairedVr ? Visibility.Visible : Visibility.Collapsed;
        FormSeriesPanel.Visibility = visQc;
        FormNumberPanel.Visibility = visQc;
        BarrierNamePanel.Visibility = visQc;
        StructuresHost.Visibility = visQs;
        DraftNumberLabel.Text = qs
            ? "Акт якісного стану"
            : qc
                ? "Акт зміни якісного стану"
                : "№";
        if (OpDocsItem != null)
            OpDocsItem.Visibility = qs ? Visibility.Visible : Visibility.Collapsed;
        if (VrActFromDraftItem != null)
            VrActFromDraftItem.Visibility = qc ? Visibility.Visible : Visibility.Collapsed;

        if (_draft.Type == DocType.QualityState)
        {
            if (_draft.CommissioningDate == null)
                _draft.CommissioningDate = _draft.Date;
            StrongpointNameBox.Text = _draft.StrongpointName;
            StrongpointRegBox.Text = _draft.StrongpointRegNumber;
            VrActNumberBox.Text = _draft.VrActNumber ?? "";
            _syncCommissionDate = true;
            CommissioningDateBox.SelectedDate = _draft.CommissioningDate;
            _syncCommissionDate = false;
            if (_draft.ActiveStructureId == null || _draft.Structures.All(s => s.Id != _draft.ActiveStructureId))
                _draft.ActiveStructureId = _draft.Structures.OrderBy(s => s.Order).Select(s => (Guid?)s.Id).FirstOrDefault();
            _ensureActiveTabVisible = true;
            RebuildStructuresPanel();
        }
        else if (_draft.Type == DocType.QualityChange)
        {
            VrActNumberBox.Text = _draft.VrActNumber ?? "";
            FormSeriesBox.Text = _draft.FormSeries;
            FormNumberBox.Text = _draft.FormNumber;
            BarrierNameBox.Text = _draft.BarrierName;
        }

        UpdateDraftHint();
        SyncDraftHeaderFieldColumns();
        PaintDraftNoteButton();
        AppStore.Instance.RecalcAnachronistic(_draft);
        RefreshDraftLines();
        // Перелік для додавання зі складу: старт — лише Запаси (не вид «Основні засоби»).
        if (_view != ViewMode.Stock)
            SwitchView(ViewMode.Stock);
        else
            RefreshAll();
    }

    /// <summary>
    /// Пропорційні колонки полів шапки: приховані ЯС/ЗЯС — Width=0, щоб не займали місце в ряду.
    /// </summary>
    private void SyncDraftHeaderFieldColumns()
    {
        var qs = StrongpointNamePanel.Visibility == Visibility.Visible;
        var qc = FormSeriesPanel.Visibility == Visibility.Visible;
        var pairedVr = VrActNumberPanel.Visibility == Visibility.Visible;

        // ЯС/ЗЯС: зліва «Акт виконаних робіт», потім номер основного акта, далі дата / ОП або формуляр…
        ColVrAct.Width = pairedVr ? new GridLength(1.35, GridUnitType.Star) : new GridLength(0);
        ColVrAct.MinWidth = pairedVr ? 140 : 0;
        ColDraftNum.Width = pairedVr
            ? new GridLength(1.35, GridUnitType.Star)
            : new GridLength(0.85, GridUnitType.Star);
        ColDraftNum.MinWidth = pairedVr ? 140 : 72;

        ColSpName.Width = qs ? new GridLength(1.4, GridUnitType.Star) : new GridLength(0);
        ColSpName.MinWidth = qs ? 120 : 0;
        ColCommission.Width = qs ? new GridLength(1.35, GridUnitType.Star) : new GridLength(0);
        ColCommission.MinWidth = qs ? 120 : 0;
        ColSpReg.Width = qs ? new GridLength(1.4, GridUnitType.Star) : new GridLength(0);
        ColSpReg.MinWidth = qs ? 120 : 0;

        ColFormSeries.Width = qc ? new GridLength(0.9, GridUnitType.Star) : new GridLength(0);
        ColFormSeries.MinWidth = qc ? 80 : 0;
        ColFormNumber.Width = qc ? new GridLength(0.9, GridUnitType.Star) : new GridLength(0);
        ColFormNumber.MinWidth = qc ? 80 : 0;
        ColBarrier.Width = qc ? new GridLength(1.5, GridUnitType.Star) : new GridLength(0);
        ColBarrier.MinWidth = qc ? 120 : 0;
    }

    /// <summary>Кнопка «Примітка» на панелі — лише коли текст уже є; додати порожню — з меню дій.</summary>
    private void PaintDraftNoteButton()
    {
        if (DraftNoteBtn == null) return;
        var note = _draft?.Note;
        var has = !string.IsNullOrWhiteSpace(note);
        DraftNoteBtn.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        NoteEditWindow.ApplyButton(DraftNoteBtn, note);
    }

    private void ExportDraftExcel(object sender, RoutedEventArgs e)
    {
        if (_draft == null) return;
        CaptureDraftFields();
        DocumentExport.SaveExcel(this, _draft);
    }

    private void EditDraftNote(object sender, RoutedEventArgs e)
    {
        if (_draft == null) return;
        var title = $"{DocTypeNames.Title(_draft.Type)} № {DocTypeNames.FormatNumber(_draft.Number)}";
        var dlg = new NoteEditWindow(_draft.Note, title) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        _draft.Note = dlg.Note;
        PaintDraftNoteButton();
    }

    private void UpdateDraftHint()
    {
        DraftHint.Text =
            "Можете додати майно подвійним натисканням або простим перетягуванням. Щоб прибрати — двічі натисніть рядок у документі або перетягніть назад у таблицю.";
    }

    private void RefreshDraftLines()
    {
        if (_draft == null) return;
        IEnumerable<DocumentLine> lines = _draft.Lines;
        if (_draft.Type == DocType.QualityState && _draft.ActiveStructureId != null)
            lines = _draft.Lines.Where(l => l.StructureId == _draft.ActiveStructureId);

        var list = lines.ToList();
        DraftLines.ItemsSource = null;
        DraftLines.ItemsSource = list;
        FitDraftLinesHeight(list.Count);
        ColorDraftLines();
        // одразу після першого рядка — інакше SizeToCells/Auto зсуває кнопки за край
        DraftLines.UpdateLayout();
        FitDraftUnitColumn();
        UpdateDraftHint();
        UpdateDraftFrameHighlight();
        RestylePropertyRows();
        UpdateStructureTabNameColors();
    }

    private static readonly SolidColorBrush TabActiveBg = new(Color.FromRgb(0xC8, 0xE6, 0xC9));
    private static readonly SolidColorBrush TabInactiveBg = new(Color.FromRgb(0xEE, 0xF5, 0xEE));
    private static readonly SolidColorBrush ViewOnBrush = CreateBrush(0x15, 0x65, 0xC0);
    private static readonly SolidColorBrush ReservedOnBrush = CreateBrush(0xC6, 0x28, 0x28);
    private static readonly SolidColorBrush TabInactiveBorder = new(Color.FromRgb(0xB7, 0xC9, 0xB7));
    private static readonly SolidColorBrush TabNameEmpty = new(Color.FromRgb(0x9E, 0x9E, 0x9E));
    private static readonly SolidColorBrush TabNameFilled = new(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private const double StructTabGap = 8;
    private const double StructTabHeight = 44;
    private const double StructDeleteHit = 40;
    private bool _rebuildingStructures;
    /// <summary>Піксельний зсув смуги вкладок (вліво). Вкладки не знімаються — виїжджають за ClipToBounds.</summary>
    private double _structScrollPx;
    /// <summary>Підкрутити scroll до активної (лише після вибору/додавання, не від стрілок).</summary>
    private bool _ensureActiveTabVisible;
    private Guid? _structTabPressId;
    private Point _structTabPressPos;
    private bool _structTabDidDrag;
    private int _structTabDragInsert = -1;
    private List<Guid>? _structureOrderBeforeDrag;
    private bool _structStripDragWired;

    /// <summary>Маркер DnD для зміни порядку вкладок споруд.</summary>
    private sealed class StructureTabDrag
    {
        public Guid Id { get; }
        public StructureTabDrag(Guid id) => Id = id;
    }

    private void DraftContentFrameSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged) RebuildStructuresPanel();
        else UpdateDraftFrameHighlight();
    }

    private void StructuresHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged) RebuildStructuresPanel();
    }

    private void UpdateDraftFrameHighlight()
    {
        Dispatcher.BeginInvoke(UpdatePairChrome, DispatcherPriority.ContextIdle);
    }

    /// <summary>Поле майна — звичайна картка; активна споруда підсвічує рамку.</summary>
    private void UpdatePairChrome()
    {
        if (_draft == null) return;

        var qsActive = _draft.Type == DocType.QualityState
                       && StructuresHost.Visibility == Visibility.Visible
                       && _draft.Structures.Count > 0
                       && _draft.ActiveStructureId != null;

        if (qsActive)
        {
            DraftLinesHost.Background = (Brush)FindResource("CardBg");
            DraftLinesHost.BorderBrush = (Brush)FindResource("Accent");
            DraftLinesHost.BorderThickness = new Thickness(2);
            DraftLinesHost.CornerRadius = new CornerRadius(14);
        }
        else
        {
            DraftLinesHost.Background = (Brush)FindResource("CardBg");
            DraftLinesHost.BorderBrush = (Brush)FindResource("InputBorder");
            DraftLinesHost.BorderThickness = new Thickness(1);
            DraftLinesHost.CornerRadius = new CornerRadius(14);
        }
    }

    /// <summary>Висота зони майна: мінімум 2 рядки, далі росте з наповненням (до 8 з прокруткою).</summary>
    private void FitDraftLinesHeight(int count)
    {
        const double rowH = 44;
        const int minRows = 2;
        const int maxRows = 8;
        var rows = Math.Clamp(Math.Max(count, minRows), minRows, maxRows);
        DraftLines.Height = rows * rowH + 2;
        DraftLines.MinHeight = minRows * rowH;
        DraftLines.VerticalScrollBarVisibility = count > maxRows
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;
    }

    private void FitDraftUnitColumn()
    {
        if (DraftUnitCol == null) return;
        void Apply()
        {
            var max = 36.0;
            var dip = 1.0;
            try { dip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { /* not loaded */ }
            foreach (var item in DraftLines.Items.OfType<DocumentLine>())
            {
                var text = string.IsNullOrWhiteSpace(item.Unit) ? "шт" : item.Unit;
                var ft = new FormattedText(
                    text,
                    CultureInfo.GetCultureInfo("uk-UA"),
                    FlowDirection.LeftToRight,
                    ThemeService.UiTypeface(FontWeights.Normal),
                    16,
                    Brushes.Black,
                    dip);
                max = Math.Max(max, ft.Width);
            }
            DraftUnitCol.Width = new DataGridLength(Math.Ceiling(max) + 12);
        }

        Apply();
        DraftLines.Dispatcher.BeginInvoke(Apply, DispatcherPriority.Loaded);
    }

    private double MeasureStructureTabWidth(string? name, bool active)
    {
        // не Trim — під час введення пробіли теж мають розширювати вкладку
        var label = string.IsNullOrEmpty(name) ? "без назви" : name;
        var dip = 1.0;
        try { dip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { /* not loaded */ }
        var ft = new FormattedText(
            label,
            CultureInfo.GetCultureInfo("uk-UA"),
            FlowDirection.LeftToRight,
            ThemeService.UiTypeface(active ? FontWeights.SemiBold : FontWeights.Normal),
            14,
            Brushes.Black,
            dip);
        // текст + кнопка × (~40) + padding вкладки
        return Math.Max(120, Math.Ceiling(ft.Width) + StructDeleteHit + 8 + 4 + 16);
    }

    private static double TotalTabsWidth(IReadOnlyList<double> widths)
    {
        if (widths.Count == 0) return 0;
        return widths.Sum() + StructTabGap * (widths.Count - 1);
    }

    /// <summary>Ліва координата кожної вкладки в «світових» координатах смуги (без scroll).</summary>
    private static List<double> ComputeTabLefts(IReadOnlyList<double> widths)
    {
        var lefts = new List<double>(widths.Count);
        double x = 0;
        for (var i = 0; i < widths.Count; i++)
        {
            lefts.Add(x);
            x += widths[i];
            if (i < widths.Count - 1) x += StructTabGap;
        }
        return lefts;
    }

    private static double MaxScrollPx(double totalWidth, double avail) =>
        Math.Max(0, totalWidth - avail);

    /// <summary>Підкрутити scroll так, щоб вкладка idx повністю вміщалась у смугу.</summary>
    private void EnsureStructureTabVisible(int idx, IReadOnlyList<double> widths, double avail)
    {
        if (idx < 0 || widths.Count == 0) return;
        var total = TotalTabsWidth(widths);
        if (total <= avail + 0.5)
        {
            _structScrollPx = 0;
            return;
        }

        var lefts = ComputeTabLefts(widths);
        var left = lefts[idx];
        var right = left + widths[idx];
        if (left < _structScrollPx)
            _structScrollPx = left;
        if (right > _structScrollPx + avail)
            _structScrollPx = right - avail;
        _structScrollPx = Math.Clamp(_structScrollPx, 0, MaxScrollPx(total, avail));
    }

    private void ApplyStructureStripChrome(double totalWidth, double avail, bool editingName)
    {
        var allFit = totalWidth <= avail + 0.5;
        if (allFit)
        {
            _structScrollPx = 0;
            StructPrevBtn.Visibility = Visibility.Collapsed;
            StructNextBtn.Visibility = Visibility.Collapsed;
            StructFadeLeft.Visibility = Visibility.Collapsed;
            StructFadeRight.Visibility = Visibility.Collapsed;
            return;
        }

        _structScrollPx = Math.Clamp(_structScrollPx, 0, MaxScrollPx(totalWidth, avail));
        var moreLeft = _structScrollPx > 0.5;
        var moreRight = totalWidth - _structScrollPx > avail + 0.5;
        StructPrevBtn.Visibility = Visibility.Visible;
        StructNextBtn.Visibility = Visibility.Visible;
        StructPrevBtn.IsEnabled = moreLeft;
        StructNextBtn.IsEnabled = moreRight;
        StructFadeLeft.Visibility = moreLeft ? Visibility.Visible : Visibility.Collapsed;
        StructFadeRight.Visibility = moreRight && !editingName ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Верхні вкладки зліва направо. Усі чіпи лишаються на Canvas і виїжджають за краї
    /// (ClipToBounds); стрілки лише зсувають ряд у пікселях.
    /// </summary>
    private void RebuildStructuresPanel()
    {
        if (_rebuildingStructures) return;
        _rebuildingStructures = true;
        try
        {
            StructuresStrip.Children.Clear();
            if (_draft == null || StructuresHost.Visibility != Visibility.Visible)
            {
                StructPrevBtn.Visibility = Visibility.Collapsed;
                StructNextBtn.Visibility = Visibility.Collapsed;
                _structScrollPx = 0;
                UpdatePairChrome();
                return;
            }

            var ordered = _draft.Structures.OrderBy(x => x.Order).ToList();
            if (ordered.Count == 0)
            {
                StructPrevBtn.Visibility = Visibility.Collapsed;
                StructNextBtn.Visibility = Visibility.Collapsed;
                _structScrollPx = 0;
                UpdatePairChrome();
                return;
            }

            var activeIdx = ordered.FindIndex(s => s.Id == _draft.ActiveStructureId);
            if (activeIdx < 0)
            {
                activeIdx = 0;
                _draft.ActiveStructureId = ordered[0].Id;
            }

            var avail = StructuresStrip.ActualWidth;
            if (avail < 40)
            {
                Dispatcher.BeginInvoke(RebuildStructuresPanel, DispatcherPriority.Loaded);
                return;
            }

            var widths = ordered
                .Select(s => MeasureStructureTabWidth(s.Name, s.Id == _draft.ActiveStructureId))
                .ToList();
            for (var i = 0; i < widths.Count; i++)
                if (widths[i] > avail) widths[i] = avail;

            var total = TotalTabsWidth(widths);
            var allFit = total <= avail + 0.5;
            if (allFit)
                _structScrollPx = 0;
            else
            {
                if (_ensureActiveTabVisible)
                    EnsureStructureTabVisible(activeIdx, widths, avail);
                _ensureActiveTabVisible = false;
                // якщо з поточного зсуву вже видно кінець — підтягнути вліво
                var maxPx = MaxScrollPx(total, avail);
                if (_structScrollPx > maxPx) _structScrollPx = maxPx;
            }

            EnsureStructureStripDragWired();

            var lefts = ComputeTabLefts(widths);
            var accent = (Brush)FindResource("Accent");

            for (var i = 0; i < ordered.Count; i++)
            {
                var active = i == activeIdx;
                var tab = BuildStructureTab(ordered[i], active, widths[i], accent);
                Canvas.SetLeft(tab, Math.Round(lefts[i] - _structScrollPx));
                Canvas.SetTop(tab, 2);
                Panel.SetZIndex(tab, active ? 2 : 1);
                StructuresStrip.Children.Add(tab);
            }

            var editingName = Keyboard.FocusedElement is TextBox { Tag: Guid fid }
                              && fid == _draft.ActiveStructureId;
            ApplyStructureStripChrome(total, avail, editingName);
            UpdatePairChrome();
        }
        finally
        {
            _rebuildingStructures = false;
        }
    }

    private bool StructureHasProperty(Guid structureId) =>
        _draft != null && _draft.Lines.Any(l => l.StructureId == structureId);

    private Brush StructureNameBrush(Guid structureId) =>
        StructureHasProperty(structureId) ? TabNameFilled : TabNameEmpty;

    /// <summary>Сірий / чорний текст назви залежно від наявності майна (без повної перезбірки).</summary>
    private void UpdateStructureTabNameColors()
    {
        if (_draft == null || StructuresHost.Visibility != Visibility.Visible) return;
        foreach (var tab in StructuresStrip.Children.OfType<Border>())
        {
            if (tab.Tag is not Guid id) continue;
            var brush = StructureNameBrush(id);
            if (tab.Child is not DockPanel row) continue;
            foreach (var child in row.Children)
            {
                if (child is TextBox box) { box.Foreground = brush; box.CaretBrush = TabNameFilled; }
                else if (child is TextBlock tb) tb.Foreground = brush;
            }
        }
    }

    private Border BuildStructureTab(FortStructure s, bool active, double width, Brush accent)
    {
        // чіпи однієї висоти: активний яскравіший із акцентною рамкою
        var text = StructureNameBrush(s.Id);
        var tab = new Border
        {
            Width = width,
            Height = StructTabHeight,
            Tag = s.Id,
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true,
            Background = active ? TabActiveBg : TabInactiveBg,
            BorderBrush = active ? accent : TabInactiveBorder,
            BorderThickness = new Thickness(active ? 2 : 1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 2, 2, 2)
        };

        var row = new DockPanel { LastChildFill = true };
        var del = new Button
        {
            Content = "×",
            Width = StructDeleteHit,
            Height = StructDeleteHit,
            MinHeight = StructDeleteHit,
            MinWidth = StructDeleteHit,
            Padding = new Thickness(0),
            FontSize = 20,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = s.Id,
            Style = (Style)FindResource("ChipDeleteButton")
        };
        DockPanel.SetDock(del, Dock.Right);
        del.Click += DeleteStructureClick;

        var captured = s.Id;
        if (active)
        {
            var box = new TextBox
            {
                Text = s.Name,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                MinHeight = 28,
                Height = 28,
                Padding = new Thickness(2, 0, 2, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                TextWrapping = TextWrapping.NoWrap,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CaretBrush = TabNameFilled,
                Foreground = text,
                Tag = captured
            };
            box.AcceptsReturn = false;
            box.TextChanged += ActiveStructureTabTextChanged;
            box.GotKeyboardFocus += StructureTabNameGotFocus;
            box.LostKeyboardFocus += StructureTabNameLostFocus;
            box.PreviewKeyDown += StructureTabNamePreviewKeyDown;
            row.Children.Add(del);
            row.Children.Add(box);
        }
        else
        {
            var title = new TextBlock
            {
                Text = string.IsNullOrEmpty(s.Name) ? "без назви" : s.Name,
                FontSize = 13,
                Foreground = text,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0)
            };
            row.Children.Add(del);
            row.Children.Add(title);
        }

        tab.PreviewMouseLeftButtonDown += StructureTabPreviewMouseDown;
        tab.Child = row;
        WireStructureDrop(tab, captured);
        return tab;
    }

    private void StructurePrev(object sender, RoutedEventArgs e)
    {
        if (_draft == null || StructuresHost.Visibility != Visibility.Visible) return;
        if (_structScrollPx <= 0.5) return;
        var ordered = _draft.Structures.OrderBy(x => x.Order).ToList();
        if (ordered.Count == 0) return;
        var avail = StructuresStrip.ActualWidth;
        if (avail < 40) return;
        var widths = ordered
            .Select(s => Math.Min(avail, MeasureStructureTabWidth(s.Name, s.Id == _draft.ActiveStructureId)))
            .ToList();
        var lefts = ComputeTabLefts(widths);
        // остання вкладка, що починається лівіше поточного зсуву — вивести її до лівого краю
        var target = -1;
        for (var i = 0; i < lefts.Count; i++)
        {
            if (lefts[i] < _structScrollPx - 0.5) target = i;
            else break;
        }
        _structScrollPx = target >= 0 ? lefts[target] : 0;
        _ensureActiveTabVisible = false;
        RebuildStructuresPanel();
    }

    private void StructureNext(object sender, RoutedEventArgs e)
    {
        if (_draft == null || StructuresHost.Visibility != Visibility.Visible) return;
        var ordered = _draft.Structures.OrderBy(x => x.Order).ToList();
        if (ordered.Count == 0) return;
        var avail = StructuresStrip.ActualWidth;
        if (avail < 40) return;
        var widths = ordered
            .Select(s => Math.Min(avail, MeasureStructureTabWidth(s.Name, s.Id == _draft.ActiveStructureId)))
            .ToList();
        var total = TotalTabsWidth(widths);
        var maxPx = MaxScrollPx(total, avail);
        if (_structScrollPx >= maxPx - 0.5) return;

        var lefts = ComputeTabLefts(widths);
        var rightEdge = _structScrollPx + avail;
        // перша вкладка, що виходить за правий край — докрутити, щоб вона повністю вмістилась
        for (var i = 0; i < lefts.Count; i++)
        {
            if (lefts[i] + widths[i] > rightEdge + 0.5)
            {
                _structScrollPx = Math.Min(maxPx, lefts[i] + widths[i] - avail);
                break;
            }
        }
        _ensureActiveTabVisible = false;
        RebuildStructuresPanel();
    }

    private bool _suppressStructureTabNameSync;
    private Guid? _structureEditId;
    private string _structureNameBeforeEdit = "";

    private void StructureTabNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressStructureTabNameSync) return;
        if (sender is not TextBox { Tag: Guid id } box || _draft == null) return;
        var s = _draft.Structures.FirstOrDefault(x => x.Id == id);
        if (s == null) return;
        s.Name = box.Text;
    }

    /// <summary>Типова назва «Споруда N» — плейсхолдер: зникає при фокусі.</summary>
    private static bool IsPlaceholderStructureName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && System.Text.RegularExpressions.Regex.IsMatch(
            name.Trim(),
            @"^Споруда\s+\d+$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>«Споруда N», де N = макс. існуючий номер + 1.</summary>
    private string NextSequentialStructureName()
    {
        if (_draft == null) return "Споруда 1";
        var max = 0;
        foreach (var s in _draft.Structures)
        {
            var raw = s.Name?.Trim() ?? "";
            if (_structureEditId == s.Id && !string.IsNullOrWhiteSpace(_structureNameBeforeEdit))
                raw = _structureNameBeforeEdit.Trim();
            var m = System.Text.RegularExpressions.Regex.Match(
                raw, @"^Споруда\s+(\d+)$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n))
                max = Math.Max(max, n);
        }
        return $"Споруда {max + 1}";
    }

    private string ResolveEmptyStructureName()
    {
        // якщо була попередня назва — вертаємо її; інакше наступний порядковий номер
        if (!string.IsNullOrWhiteSpace(_structureNameBeforeEdit))
            return _structureNameBeforeEdit.Trim();
        return NextSequentialStructureName();
    }

    private void StructureTabNameGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox { Tag: Guid id } box) return;
        if (!IsPlaceholderStructureName(box.Text)) return;

        _structureEditId = id;
        _structureNameBeforeEdit = box.Text;
        // очищаємо лише відображення; у моделі лишається стара назва, доки не почнуть вводити
        _suppressStructureTabNameSync = true;
        try
        {
            box.Text = "";
        }
        finally
        {
            _suppressStructureTabNameSync = false;
        }

        if (StructuresStrip.ActualWidth >= 40)
            ReflowStructureTabsWhileEditing(id, "");
    }

    private void StructureTabNamePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return or Key.Escape)) return;
        e.Handled = true;
        EndStructureNameEditing();
    }

    /// <summary>Зняти фокус з назви споруди → спрацює LostFocus і збереження.</summary>
    private void EndStructureNameEditing()
    {
        if (Keyboard.FocusedElement is not TextBox box) return;
        if (!ReferenceEquals(FindParent<Canvas>(box), StructuresStrip)) return;
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(this, null);
    }

    private void StructureTabNameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox { Tag: Guid id } box || _draft == null) return;
        var s = _draft.Structures.FirstOrDefault(x => x.Id == id);
        if (s == null) return;

        var typed = box.IsLoaded ? box.Text : s.Name;
        if (string.IsNullOrWhiteSpace(typed))
        {
            var restored = ResolveEmptyStructureName();
            s.Name = restored;
            if (box.IsLoaded)
            {
                _suppressStructureTabNameSync = true;
                try { box.Text = restored; }
                finally { _suppressStructureTabNameSync = false; }
            }
        }
        else if (box.IsLoaded)
        {
            s.Name = typed.Trim();
        }

        _structureEditId = null;
        _structureNameBeforeEdit = "";

        // після кліку поза полем — перезбірка після обробки миші, щоб клік не «з’ївся»
        if (_rebuildingStructures || !box.IsLoaded) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_rebuildingStructures || _draft == null) return;
            _ensureActiveTabVisible = true;
            RebuildStructuresPanel();
        }, DispatcherPriority.Input);
    }

    /// <summary>Під час введення — активна росте й розштовхує сусідів; ряд зсувається, щоб усе було видно.</summary>
    private void ActiveStructureTabTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressStructureTabNameSync) return;

        StructureTabNameChanged(sender, e);
        if (sender is not TextBox { Tag: Guid id } box) return;
        if (_draft == null) return;

        var avail = StructuresStrip.ActualWidth;
        if (avail < 40) return;

        ReflowStructureTabsWhileEditing(id, box.Text);
    }

    /// <summary>
    /// Під час введення: активна росте, усі чіпи лишаються в смузі й плавно
    /// зсуваються за краї (ClipToBounds) — нічого не знімається з Canvas.
    /// </summary>
    private void ReflowStructureTabsWhileEditing(Guid activeId, string activeText)
    {
        var ordered = _draft!.Structures.OrderBy(x => x.Order).ToList();
        var activeIdx = ordered.FindIndex(s => s.Id == activeId);
        if (activeIdx < 0) return;

        var avail = StructuresStrip.ActualWidth;
        var widths = ordered
            .Select(s =>
            {
                var name = s.Id == activeId ? activeText : s.Name;
                return Math.Min(MeasureStructureTabWidth(name, s.Id == activeId), avail);
            })
            .ToList();

        // активна завжди повністю у видимій зоні — сусіди виїжджають за край
        EnsureStructureTabVisible(activeIdx, widths, avail);

        var lefts = ComputeTabLefts(widths);
        var byId = StructuresStrip.Children.OfType<Border>()
            .Where(b => b.Tag is Guid)
            .GroupBy(b => (Guid)b.Tag!)
            .ToDictionary(g => g.Key, g => g.First());

        var accent = (Brush)FindResource("Accent");
        var keep = new HashSet<Guid>();

        for (var i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            if (!byId.TryGetValue(s.Id, out var tab))
            {
                if (s.Id == activeId) continue; // TextBox не пересоздаємо
                tab = BuildStructureTab(s, active: false, widths[i], accent);
                StructuresStrip.Children.Add(tab);
                byId[s.Id] = tab;
            }

            tab.Width = widths[i];
            Canvas.SetLeft(tab, Math.Round(lefts[i] - _structScrollPx));
            Canvas.SetTop(tab, 2);
            Panel.SetZIndex(tab, s.Id == activeId ? 3 : 1);
            keep.Add(s.Id);
        }

        foreach (var kv in byId)
        {
            if (!keep.Contains(kv.Key))
                StructuresStrip.Children.Remove(kv.Value);
        }

        ApplyStructureStripChrome(TotalTabsWidth(widths), avail, editingName: true);
    }

    private void DeleteStructureClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { Tag: Guid id }) return;
        DeleteStructure(id);
    }

    private void DeleteStructure(Guid id)
    {
        if (_draft == null) return;
        var s = _draft.Structures.FirstOrDefault(x => x.Id == id);
        if (s == null) return;
        var n = _draft.Lines.Count(l => l.StructureId == id);
        var name = string.IsNullOrWhiteSpace(s.Name) ? "без назви" : s.Name.Trim();
        var msg = n > 0
            ? $"Видалити споруду «{name}» разом із майном ({n})?"
            : $"Видалити споруду «{name}»?";
        if (MessageBox.Show(msg, "Обозний", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _draft.Lines.RemoveAll(l => l.StructureId == id);
        _draft.Structures.RemoveAll(x => x.Id == id);
        if (_draft.ActiveStructureId == id)
            _draft.ActiveStructureId = _draft.Structures.OrderBy(x => x.Order).Select(x => (Guid?)x.Id).FirstOrDefault();

        RebuildStructuresPanel();
        RefreshDraftLines();
    }

    private void WireStructureDrop(UIElement el, Guid structureId)
    {
        el.AllowDrop = true;
        el.DragOver += (_, e) =>
        {
            if (e.Data.GetDataPresent(typeof(StructureTabDrag)))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                if (e.Data.GetData(typeof(StructureTabDrag)) is StructureTabDrag drag)
                    PreviewStructureTabReorder(drag.Id, e.GetPosition(StructuresStrip));
                return;
            }
            if (!e.Data.GetDataPresent(typeof(PropertyRow))) return;
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        };
        el.Drop += (_, e) =>
        {
            if (e.Data.GetData(typeof(StructureTabDrag)) is StructureTabDrag drag)
            {
                CommitStructureTabReorder(drag.Id, e.GetPosition(StructuresStrip));
                e.Handled = true;
                return;
            }
            if (e.Data.GetData(typeof(PropertyRow)) is not PropertyRow row) return;
            AddToDraft(row, structureId);
            e.Handled = true;
        };
    }

    private void EnsureStructureStripDragWired()
    {
        if (_structStripDragWired) return;
        _structStripDragWired = true;
        StructuresStrip.AllowDrop = true;
        StructuresStrip.PreviewMouseMove += StructureStripPreviewMouseMove;
        StructuresStrip.PreviewMouseLeftButtonUp += StructureStripPreviewMouseUp;
        StructuresStrip.DragOver += (_, e) =>
        {
            if (e.Data.GetData(typeof(StructureTabDrag)) is not StructureTabDrag drag) return;
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            PreviewStructureTabReorder(drag.Id, e.GetPosition(StructuresStrip));
        };
        StructuresStrip.Drop += (_, e) =>
        {
            if (e.Data.GetData(typeof(StructureTabDrag)) is not StructureTabDrag drag) return;
            CommitStructureTabReorder(drag.Id, e.GetPosition(StructuresStrip));
            e.Handled = true;
        };
        StructuresStrip.DragLeave += (_, e) =>
        {
            // лише якщо курсор реально покинув смугу
            var pos = e.GetPosition(StructuresStrip);
            if (pos.X >= 0 && pos.Y >= 0
                && pos.X <= StructuresStrip.ActualWidth
                && pos.Y <= StructuresStrip.ActualHeight)
                return;
            // preview лишаємо — скасування через Esc / результат DoDragDrop
        };
    }

    private void StructureTabPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: Guid id }) return;
        if (e.OriginalSource is DependencyObject d && FindParent<Button>(d) != null) return;
        _structTabPressId = id;
        _structTabPressPos = e.GetPosition(StructuresStrip);
        _structTabDidDrag = false;
    }

    private void StructureStripPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_structTabPressId is not Guid id) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _structTabPressId = null;
            return;
        }
        var pos = e.GetPosition(StructuresStrip);
        if ((pos - _structTabPressPos).Length < 8) return;

        _structTabDidDrag = true;
        _structTabPressId = null;
        if (_draft == null) return;

        EndStructureNameEditing();
        _structureOrderBeforeDrag = _draft.Structures.OrderBy(x => x.Order).Select(x => x.Id).ToList();
        _structTabDragInsert = -1;
        if (_draft.ActiveStructureId != id)
            _draft.ActiveStructureId = id;

        var src = StructuresStrip.Children.OfType<Border>().FirstOrDefault(b => b.Tag is Guid g && g == id);
        if (src == null) return;

        var data = new DataObject(typeof(StructureTabDrag), new StructureTabDrag(id));
        var result = DragDrop.DoDragDrop(src, data, DragDropEffects.Move);
        if (result != DragDropEffects.Move)
            RestoreStructureOrder(_structureOrderBeforeDrag);
        _structureOrderBeforeDrag = null;
        _structTabDragInsert = -1;
        _ensureActiveTabVisible = true;
        RebuildStructuresPanel();
        RefreshDraftLines();
    }

    private void StructureStripPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_structTabPressId is not Guid id)
        {
            _structTabDidDrag = false;
            return;
        }
        var pressed = id;
        _structTabPressId = null;
        if (_structTabDidDrag || _draft == null)
        {
            _structTabDidDrag = false;
            return;
        }
        _structTabDidDrag = false;
        if (_draft.ActiveStructureId != pressed)
            SelectStructure(pressed);
    }

    private int StructureInsertIndexFromMouse(Point stripPos, Guid dragId)
    {
        var tabs = StructuresStrip.Children.OfType<Border>()
            .Where(b => b.Tag is Guid g && g != dragId)
            .OrderBy(b => Canvas.GetLeft(b))
            .ToList();
        for (var i = 0; i < tabs.Count; i++)
        {
            var left = Canvas.GetLeft(tabs[i]);
            if (double.IsNaN(left)) left = 0;
            if (stripPos.X < left + tabs[i].Width / 2) return i;
        }
        return tabs.Count;
    }

    private void ApplyStructureInsertOrder(Guid dragId, int insertAmongOthers)
    {
        if (_draft == null) return;
        var ordered = _draft.Structures.OrderBy(x => x.Order).ToList();
        var drag = ordered.FirstOrDefault(s => s.Id == dragId);
        if (drag == null) return;
        var others = ordered.Where(s => s.Id != dragId).ToList();
        insertAmongOthers = Math.Clamp(insertAmongOthers, 0, others.Count);
        others.Insert(insertAmongOthers, drag);
        for (var i = 0; i < others.Count; i++)
            others[i].Order = i;
    }

    private void RelayoutStructureTabsAfterReorder()
    {
        if (_draft == null) return;
        var ordered = _draft.Structures.OrderBy(x => x.Order).ToList();
        var avail = StructuresStrip.ActualWidth;
        if (avail < 40) return;
        var widths = ordered
            .Select(s => Math.Min(avail, MeasureStructureTabWidth(s.Name, s.Id == _draft.ActiveStructureId)))
            .ToList();
        var total = TotalTabsWidth(widths);
        _structScrollPx = Math.Clamp(_structScrollPx, 0, MaxScrollPx(total, avail));
        var lefts = ComputeTabLefts(widths);
        var byId = StructuresStrip.Children.OfType<Border>()
            .Where(b => b.Tag is Guid)
            .ToDictionary(b => (Guid)b.Tag!, b => b);
        for (var i = 0; i < ordered.Count; i++)
        {
            if (!byId.TryGetValue(ordered[i].Id, out var tab)) continue;
            tab.Width = widths[i];
            Canvas.SetLeft(tab, Math.Round(lefts[i] - _structScrollPx));
            Canvas.SetTop(tab, 2);
            Panel.SetZIndex(tab, ordered[i].Id == _draft.ActiveStructureId ? 3 : 1);
        }
        ApplyStructureStripChrome(total, avail, editingName: false);
    }

    private void PreviewStructureTabReorder(Guid dragId, Point stripPos)
    {
        if (_draft == null || _structureOrderBeforeDrag == null) return;
        var insert = StructureInsertIndexFromMouse(stripPos, dragId);
        if (insert == _structTabDragInsert) return;
        _structTabDragInsert = insert;
        ApplyStructureInsertOrder(dragId, insert);
        RelayoutStructureTabsAfterReorder();
    }

    private void CommitStructureTabReorder(Guid dragId, Point stripPos)
    {
        if (_draft == null) return;
        var insert = StructureInsertIndexFromMouse(stripPos, dragId);
        ApplyStructureInsertOrder(dragId, insert);
        _structTabDragInsert = insert;
        // фінальна перезбірка після DoDragDrop
    }

    private void RestoreStructureOrder(List<Guid>? order)
    {
        if (_draft == null || order == null) return;
        for (var i = 0; i < order.Count; i++)
        {
            var s = _draft.Structures.FirstOrDefault(x => x.Id == order[i]);
            if (s != null) s.Order = i;
        }
    }

    private void SelectStructure(Guid id, bool rebuild = true)
    {
        if (_draft == null) return;
        if (_draft.ActiveStructureId == id && !rebuild) return;
        _draft.ActiveStructureId = id;
        if (rebuild)
        {
            _ensureActiveTabVisible = true;
            RebuildStructuresPanel();
        }
        RefreshDraftLines();
    }

    private void AddStructure(object sender, RoutedEventArgs e)
    {
        if (_draft == null || _draft.Type != DocType.QualityState) return;
        CaptureDraftFields();
        var s = new FortStructure
        {
            Name = NextSequentialStructureName(),
            Order = _draft.Structures.Count == 0 ? 0 : _draft.Structures.Max(x => x.Order) + 1
        };
        _draft.Structures.Add(s);
        _draft.ActiveStructureId = s.Id;
        _ensureActiveTabVisible = true;
        RebuildStructuresPanel();
        RefreshDraftLines();
    }

    private void CaptureDraftFields()
    {
        if (_draft == null) return;
        _draft.Number = DocTypeNames.NormalizeNumber(DraftNumberBox.Text);
        if (_draft.Type == DocType.QualityState)
        {
            _draft.StrongpointName = StrongpointNameBox.Text.Trim();
            _draft.StrongpointRegNumber = StrongpointRegBox.Text.Trim();
            _draft.CommissioningDate = CommissioningDateBox.SelectedDate?.Date ?? _draft.Date;
            _draft.VrActNumber = VrActNumberBox.Text.Trim();
        }
        else if (_draft.Type == DocType.QualityChange)
        {
            _draft.VrActNumber = VrActNumberBox.Text.Trim();
            _draft.FormSeries = FormSeriesBox.Text.Trim();
            _draft.FormNumber = FormNumberBox.Text.Trim();
            _draft.BarrierName = BarrierNameBox.Text.Trim();
        }
    }

    private void ColorDraftLines()
    {
        DraftLines.Items.Refresh();
    }

    private void OnDraftLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is DocumentLine line && line.Anachronistic)
        {
            e.Row.Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xBB, 0xD0));
            e.Row.Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
        }
        else
        {
            e.Row.Background = Brushes.Transparent;
            e.Row.Foreground = (Brush)FindResource("TextPrimary");
        }
    }

    private void CloseDraft(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardDraft()) return;
        DiscardDraft();
    }

    private void DiscardDraft()
    {
        _draft = null;
        _draftStamp = "";
        _draftIncludeOz = false;
        PaintDraftOzButton();
        DraftPanel.Visibility = Visibility.Collapsed;
        RefreshHeader();
        RefreshAll();
    }

    private bool ConfirmDiscardDraft()
    {
        if (_draft == null) return true;
        CaptureDraftFields();
        if (StampDraft(_draft) == _draftStamp) return true;
        var ask = MessageBox.Show(
            "Чернетку не збережено. Закрити без збереження?",
            "Обозний", MessageBoxButton.YesNo, MessageBoxImage.Question);
        return ask == MessageBoxResult.Yes;
    }

    private static string StampDraft(Document doc)
    {
        var lines = string.Join(";", doc.Lines.Select(l => $"{l.PropertyId:N}:{l.StructureId:N}:{l.Quantity}"));
        var structures = string.Join(";", doc.Structures.OrderBy(s => s.Order).Select(s => $"{s.Id:N}:{s.Name}"));
        return string.Join("|",
            doc.Number, doc.Date.ToString("yyyyMMdd"), doc.Note, doc.StrongpointName,
            doc.StrongpointRegNumber, doc.VrActNumber, doc.CommissioningDate?.ToString("yyyyMMdd"),
            doc.FormSeries, doc.FormNumber, doc.BarrierName, doc.ActiveStructureId, lines, structures);
    }

    private void DeleteDraft(object sender, RoutedEventArgs e)
    {
        if (_draft == null) return;
        CaptureDraftFields();
        var stored = AppStore.Instance.Data.Documents.FirstOrDefault(d => d.Id == _draft.Id);
        if (stored == null)
        {
            var drop = MessageBox.Show(
                "Чернетку ще не збережено. Прибрати її з екрана?",
                "Обозний", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (drop != MessageBoxResult.Yes) return;
            DiscardDraft();
            return;
        }

        var ask = MessageBox.Show(
            $"Видалити документ «{DocTypeNames.Title(_draft.Type)}» № {DocTypeNames.FormatNumber(_draft.Number)}?\n\n" +
            "Залишки на складі буде перераховано без цього документа.",
            "Обозний", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ask != MessageBoxResult.Yes) return;

        var err = AppStore.Instance.DeleteDocument(stored);
        if (err != null)
        {
            MessageBox.Show(err, "Обозний", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DiscardDraft();
        MessageBox.Show("Документ видалено.", "Обозний", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveDraft(object sender, RoutedEventArgs e)
    {
        if (_draft == null) return;
        CaptureDraftFields();
        _draft.Lines.RemoveAll(l => l.Quantity <= 0);
        AppStore.Instance.SaveDraft(_draft);
        MessageBox.Show("Чернетку збережено. Майно зарезервовано до проведення.", "Обозний");
        DiscardDraft();
    }

    /// <summary>
    /// Зберегти чернетку ЯС і відкрити картку ОП (блок «Документи опорного пункту»: ЯС + Акт ВР).
    /// ОП одразу в переліку опорних пунктів; до проведення — суфікс *ЧЕРНЕТКА*.
    /// </summary>
    private void OpenOpDocsFromDraft(object sender, RoutedEventArgs e)
    {
        if (_draft == null || _draft.Type != DocType.QualityState) return;
        CaptureDraftFields();
        _draft.Lines.RemoveAll(l => l.Quantity <= 0);
        if (string.IsNullOrWhiteSpace(_draft.StrongpointName)
            && string.IsNullOrWhiteSpace(_draft.StrongpointRegNumber))
        {
            MessageBox.Show(
                "Вкажіть назву або реєстраційний номер опорного пункту.",
                "Обозний", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AppStore.Instance.SaveDraft(_draft);
        _draftStamp = StampDraft(_draft);
        var row = AppStore.Instance.StrongpointRowFor(_draft);
        if (row == null)
        {
            MessageBox.Show("Не вдалося зібрати картку опорного пункту.", "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var card = new StrongpointCardWindow(row) { Owner = this };
        if (card.ShowDialog() == true && card.DraftToOpen != null)
            OpenDraft(card.DraftToOpen);
        else
        {
            // Чернетка лишається відкритою; перелік ОП уже містить цей пункт.
            ShowDraft();
            RefreshAll();
        }
    }

    /// <summary>
    /// Зберегти чернетку ЗЯС (НВЗ) і відкрити парний Акт ВР (перегляд/друк, без картки ОП).
    /// </summary>
    private void OpenVrActFromDraft(object sender, RoutedEventArgs e)
    {
        if (_draft == null || _draft.Type != DocType.QualityChange) return;
        CaptureDraftFields();
        AppStore.Instance.SaveDraft(_draft);
        _draftStamp = StampDraft(_draft);
        new VrActViewWindow(_draft) { Owner = this }.ShowDialog();
        ShowDraft();
    }

    private void OpenDraft(Document doc)
    {
        _draft = AppStore.CloneDraft(doc);
        CollapseOpsMenu();
        ShowDraft();
        _draftStamp = StampDraft(_draft);
    }

    private void PostDraft(object sender, RoutedEventArgs e)
    {
        if (_draft == null) return;
        CaptureDraftFields();
        _draft.Lines.RemoveAll(l => l.Quantity <= 0);
        if (_draft.Lines.Count == 0)
        {
            MessageBox.Show("У чернетці немає рядків для проведення.", "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            ShowDraft();
            return;
        }
        if (_draft.Type == DocType.QualityState && _draft.Structures.Count == 0)
        {
            MessageBox.Show("Додайте хоча б одну фортифікаційну споруду.", "Обозний",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var leavesStock = _draft.Type is DocType.Return or DocType.Transfer;
        var ask = MessageBox.Show(
            $"Провести «{DocTypeNames.Title(_draft.Type)}» № {DocTypeNames.FormatNumber(_draft.Number)} від {_draft.Date:dd.MM.yyyy}?\n\n" +
            (leavesStock
                ? $"Рух: {MoveKindNames.Of(_draft.Type)}. Після проведення майно буде знято зі складу."
                : "Після проведення майно буде знято з обліку."),
            "Обозний", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes) return;

        var err = AppStore.Instance.Post(_draft);
        if (err != null)
        {
            MessageBox.Show(err, "Обозний", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show(
            leavesStock
                ? "Документ проведено. Майно знято зі складу."
                : "Документ проведено. Майно знято з обліку.",
            "Обозний",
            MessageBoxButton.OK, MessageBoxImage.Information);
        DiscardDraft();
    }

    private void ToggleReserved(object sender, RoutedEventArgs e)
    {
        if (_view is ViewMode.Movements or ViewMode.Strongpoints) return;
        CollapseOpsMenu();
        // Залишки та Основні засоби — той самий патерн резервування.
        _showReserved = !_showReserved;
        PaintViewButtons();
        RefreshAll();
    }

    private void ToggleMovements(object sender, RoutedEventArgs e)
    {
        CollapseOpsMenu();
        var next = _view == ViewMode.Movements ? ViewMode.Stock : ViewMode.Movements;
        SwitchView(next);
        if (next == ViewMode.Movements)
        {
            var unknown = AppStore.Instance.Data.Movements
                .Count(m => !MoveKindNames.IsInventory(m.DocumentType)
                            && MoveKindNames.IsUnknown(MoveKindNames.Of(m)));
            if (unknown > 0)
            {
                MessageBox.Show(
                    $"У реєстрі є {unknown} рядків з невизначеним типом руху (жовта підсвітка).\n\n" +
                    "Натисніть на рядок і оберіть робочий тип вручну.",
                    "Обозний", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void ToggleStrongpoints(object sender, RoutedEventArgs e)
    {
        CollapseOpsMenu();
        var next = _view == ViewMode.Strongpoints ? ViewMode.Stock : ViewMode.Strongpoints;
        SwitchView(next);
    }

    private void SwitchView(ViewMode next)
    {
        var stockLike = next is ViewMode.Stock or ViewMode.FixedAssets;
        // З реєстру руху / ОП — скинути сортування, якого немає в таблиці залишків/ОЗ.
        if (stockLike && _sortKey is "Date" or "MoveKind" or "DocumentNumber" or "RegNumber" or "TotalValue")
        { _sortKey = ""; _sortState = 0; }

        _view = next;
        if (!stockLike) _showReserved = false;

        PaintViewButtons();

        BuildColumns();
        RefreshHeader();
        RefreshAll();
    }

    /// <summary>
    /// Неактивні кнопки лишають колір темі. Локальний фон тримав старий пензель,
    /// тож після зміни теми вони не збігались з рештою, поки їх не натиснути.
    /// </summary>
    private void PaintViewButtons()
    {
        PaintModeButton(MovementBtn, _view == ViewMode.Movements, ViewOnBrush);
        PaintModeButton(StrongpointsBtn, _view == ViewMode.Strongpoints, ViewOnBrush);
        PaintModeButton(FixedAssetsBtn, _view == ViewMode.FixedAssets, ViewOnBrush);
        PaintModeButton(ReservedBtn, _showReserved, ReservedOnBrush);
        PaintDraftOzButton();
    }

    private static void PaintModeButton(Button button, bool on, Brush onBrush)
    {
        if (on)
        {
            button.Background = onBrush;
            button.Foreground = Brushes.White;
        }
        else
        {
            button.ClearValue(Control.BackgroundProperty);
            button.ClearValue(Control.ForegroundProperty);
        }
    }

    private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void PaintFilterBoxes()
    {
        foreach (var box in _filterBoxes.Values)
            PaintFilterBox(box);
    }

    private void PaintFilterBox(TextBox box)
    {
        if (string.IsNullOrWhiteSpace(box.Text))
            box.ClearValue(Control.BackgroundProperty);
        else
            box.Background = (Brush)FindResource("FilterActive");
        if (box.Tag is Button clearBtn)
            clearBtn.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshAll()
    {
        _stickers?.RefreshSummary();
        ExportTableBtn.Visibility = _view is ViewMode.Stock or ViewMode.FixedAssets or ViewMode.Movements
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_view == ViewMode.Strongpoints)
        {
            _shownRows = new();
            TotalsHost.Visibility = Visibility.Collapsed;
            RefreshStrongpoints();
            if (_draft != null) RefreshDraftLines();
            return;
        }

        IEnumerable<PropertyRow> rows;
        if (_view == ViewMode.Movements)
            rows = MovementRows();
        else
        {
            // Чернетка: лише Запаси, або Запаси+ОЗ за кнопкою «Основні засоби».
            // Без чернетки: Залишки = Запаси; вид «Основні засоби» = лише ОЗ.
            var source = _draft != null
                ? AppStore.Instance.PropertiesForDraftPick(_draftIncludeOz)
                : _view == ViewMode.FixedAssets
                    ? AppStore.Instance.FixedAssetProperties()
                    : AppStore.Instance.StockProperties();
            rows = source
                .SelectMany(p =>
                {
                    var list = new List<PropertyRow>();
                    // мінус завжди видно; нуль «списаного» — ні; плюс — як раніше
                    if (p.Quantity < -0.0001m || (!p.FullyWrittenOff && p.Quantity > 0.0001m))
                        list.Add(ToRow(p, p.Quantity, false));
                    if (_showReserved && !p.FullyWrittenOff && p.ReservedQuantity > 0)
                        list.Add(ToRow(p, p.ReservedQuantity, true));
                    return list;
                });
        }

        var list = rows.ToList();
        foreach (var kv in _filters)
        {
            if (!FilterApplies(kv.Key)) continue;
            if (string.IsNullOrWhiteSpace(kv.Value)) continue;
            var f = kv.Value.Trim().ToLowerInvariant();
            list = kv.Key switch
            {
                "Date" => list.Where(r => MatchesDate(r.Date, f)).ToList(),
                "MoveKind" => list.Where(r => r.MoveKind.ToLowerInvariant().Contains(f)).ToList(),
                "DocumentNumber" => list.Where(r => r.DocumentNumber.ToLowerInvariant().Contains(f)).ToList(),
                "Name" => list.Where(r => r.Name.ToLowerInvariant().Contains(f)).ToList(),
                "Unit" => list.Where(r => r.Unit.ToLowerInvariant().Contains(f)).ToList(),
                "Category" => list.Where(r => r.Category.ToLowerInvariant().Contains(f)).ToList(),
                "UnitPrice" => list.Where(r => MatchesNumber(r.UnitPrice, f)).ToList(),
                "Quantity" => list.Where(r => MatchesNumber(r.Quantity, f)).ToList(),
                _ => list
            };
        }

        if (_sortState != 0 && !string.IsNullOrEmpty(_sortKey))
        {
            list = _sortKey switch
            {
                "Date" => (_sortState == 1
                    ? list.OrderBy(r => r.Date ?? DateTime.MinValue)
                    : list.OrderByDescending(r => r.Date ?? DateTime.MinValue)).ToList(),
                "MoveKind" => (_sortState == 1
                    ? list.OrderBy(r => r.MoveKind)
                    : list.OrderByDescending(r => r.MoveKind)).ToList(),
                "DocumentNumber" => (_sortState == 1
                    ? list.OrderBy(r => r.DocumentNumber)
                    : list.OrderByDescending(r => r.DocumentNumber)).ToList(),
                "Name" => (_sortState == 1 ? list.OrderBy(r => r.Name) : list.OrderByDescending(r => r.Name)).ToList(),
                "Unit" => (_sortState == 1 ? list.OrderBy(r => r.Unit) : list.OrderByDescending(r => r.Unit)).ToList(),
                "Category" => (_sortState == 1 ? list.OrderBy(r => r.Category) : list.OrderByDescending(r => r.Category)).ToList(),
                "UnitPrice" => (_sortState == 1 ? list.OrderBy(r => r.UnitPrice) : list.OrderByDescending(r => r.UnitPrice)).ToList(),
                "Quantity" => (_sortState == 1 ? list.OrderBy(r => r.Quantity) : list.OrderByDescending(r => r.Quantity)).ToList(),
                _ => list
            };
        }

        _shownRows = list;
        PropertyGrid.ItemsSource = list;
        UpdateTotals(list);
        if (_draft != null) RefreshDraftLines();
    }

    private bool HasActiveFilter() =>
        _filters.Any(kv => FilterApplies(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value));

    private void UpdateTotals(IReadOnlyList<PropertyRow> rows)
    {
        var show = HasActiveFilter() && _view is ViewMode.Stock or ViewMode.FixedAssets or ViewMode.Movements;
        TotalsHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        var qty = rows.Sum(r => r.Quantity);
        var value = rows.Sum(r => r.Quantity * r.UnitPrice);
        var labeled = false;
        foreach (var col in PropertyGrid.Columns)
        {
            var key = col.SortMemberPath ?? "";
            if (!_totalBlocks.TryGetValue(key, out var block)) continue;
            if (key == "Quantity")
            {
                block.Text = Ua.FormatQty(qty);
            }
            else if (key == "UnitPrice")
            {
                block.Text = Ua.Num(value, "#,##0.00");
            }
            else if (!labeled)
            {
                block.Text = "Разом";
                labeled = true;
            }
            else
            {
                block.Text = "";
            }
        }
        SyncTotalsWidths();
    }

    private void ExportTableExcel(object sender, RoutedEventArgs e)
    {
        if (_view is not (ViewMode.Stock or ViewMode.FixedAssets or ViewMode.Movements)) return;
        var columns = PropertyGrid.Columns
            .OrderBy(c => c.DisplayIndex)
            .Select(c =>
            {
                var key = c.SortMemberPath ?? "";
                return new TableExportColumn(key, ColumnTitle(key));
            })
            .Where(c => c.Key.Length > 0)
            .ToList();
        var title = _view switch
        {
            ViewMode.Movements => "Рух майна",
            ViewMode.FixedAssets => "Основні засоби",
            _ => "Залишки"
        };
        TableExport.Save(this, title, ActiveFilterNote(), columns, _shownRows);
    }

    private string? ActiveFilterNote()
    {
        var parts = new List<string>();
        foreach (var kv in _filters)
        {
            if (!FilterApplies(kv.Key) || string.IsNullOrWhiteSpace(kv.Value)) continue;
            parts.Add(ColumnTitle(kv.Key) + ": " + kv.Value.Trim());
        }
        return parts.Count == 0 ? null : "Фільтр — " + string.Join("; ", parts);
    }

    private static string ColumnTitle(string key)
    {
        var def = ColumnCatalog.All.FirstOrDefault(c => c.Key == key);
        if (!string.IsNullOrEmpty(def.Key)) return def.Aliases[0];
        return key switch
        {
            "Date" => "Дата",
            "MoveKind" => "Тип руху",
            "DocumentNumber" => "Номер",
            "RegNumber" => "Реєстраційний номер",
            "TotalValue" => "Вартість",
            _ => key
        };
    }

    private void RefreshStrongpoints()
    {
        var list = AppStore.Instance.StrongpointRows();
        foreach (var kv in _filters)
        {
            if (!FilterApplies(kv.Key)) continue;
            if (string.IsNullOrWhiteSpace(kv.Value)) continue;
            var f = kv.Value.Trim().ToLowerInvariant();
            list = kv.Key switch
            {
                "Name" => list.Where(r => r.Name.ToLowerInvariant().Contains(f)).ToList(),
                "RegNumber" => list.Where(r => r.RegNumber.ToLowerInvariant().Contains(f)).ToList(),
                "Date" => list.Where(r => MatchesDate(r.CommissioningDate, f)).ToList(),
                "TotalValue" => list.Where(r => MatchesNumber(r.TotalValue, f)).ToList(),
                _ => list
            };
        }

        if (_sortState != 0 && !string.IsNullOrEmpty(_sortKey))
        {
            list = _sortKey switch
            {
                "Name" => (_sortState == 1 ? list.OrderBy(r => r.Name) : list.OrderByDescending(r => r.Name)).ToList(),
                "RegNumber" => (_sortState == 1 ? list.OrderBy(r => r.RegNumber) : list.OrderByDescending(r => r.RegNumber)).ToList(),
                "Date" => (_sortState == 1
                    ? list.OrderBy(r => r.CommissioningDate ?? DateTime.MinValue)
                    : list.OrderByDescending(r => r.CommissioningDate ?? DateTime.MinValue)).ToList(),
                "TotalValue" => (_sortState == 1 ? list.OrderBy(r => r.TotalValue) : list.OrderByDescending(r => r.TotalValue)).ToList(),
                _ => list
            };
        }

        PropertyGrid.ItemsSource = list;
    }

    private static IEnumerable<PropertyRow> MovementRows()
    {
        var props = AppStore.Instance.Data.Properties.ToDictionary(p => p.Id);
        // Інвентаризація — окремо; не змішувати з реєстром «Рух майна».
        return AppStore.Instance.Data.Movements
            .Where(m => !MoveKindNames.IsInventory(m.DocumentType)
                        && !MoveKindNames.IsInventoryLabel(MoveKindNames.Of(m)))
            .OrderByDescending(m => m.DocumentDate)
            .ThenByDescending(m => m.At)
            .Select(m =>
            {
                props.TryGetValue(m.PropertyId, out var p);
                var moveKind = MoveKindNames.Of(m);
                return new PropertyRow
                {
                    Id = m.PropertyId,
                    Name = string.IsNullOrWhiteSpace(m.PropertyName) ? (p?.Name ?? "") : m.PropertyName,
                    Unit = p?.Unit ?? "",
                    Category = p?.Category ?? "",
                    UnitPrice = p?.UnitPrice ?? 0,
                    Quantity = Math.Abs(m.QuantityDelta),
                    Date = m.DocumentDate.Date,
                    MoveKind = moveKind,
                    DocumentNumber = m.DocumentNumber ?? "",
                    DocumentId = m.DocumentId,
                    IsWrittenOff = p?.FullyWrittenOff == true,
                    NeedsKindPick = MoveKindNames.IsUnknown(moveKind)
                };
            });
    }

    /// <summary>
    /// Фільтр дати, типу руху чи номера з «Руху майна» не чіпає склад:
    /// у рядках складу цих полів немає, тож увесь список зникав до перезапуску.
    /// </summary>
    private bool FilterApplies(string key) => _view switch
    {
        ViewMode.Strongpoints => key is "Name" or "RegNumber" or "Date" or "TotalValue",
        ViewMode.Movements => key is "Date" or "MoveKind" or "DocumentNumber"
            or "Name" or "Unit" or "Category" or "UnitPrice" or "Quantity",
        _ => key is "Name" or "Unit" or "Category" or "UnitPrice" or "Quantity"
    };

    private static bool MatchesNumber(decimal value, string filter)
    {
        static string Compact(string s) =>
            s.Replace(" ", "").Replace("\u00A0", "").Replace("\u202F", "");
        var grouped = Compact(Ua.Num(value, "#,##0.########"));
        var plain = Compact(Ua.Num(value, "0.########"));
        var inv = Compact(value.ToString("0.########", CultureInfo.InvariantCulture));
        var f = Compact(filter);
        return grouped.Contains(f) || plain.Contains(f) || inv.Contains(f.Replace(',', '.'));
    }

    private static bool MatchesDate(DateTime? value, string filter)
    {
        if (value == null) return false;
        var d = value.Value;
        var f = filter.Trim().ToLowerInvariant();
        return d.ToString("dd.MM.yyyy", Ua.Culture).Contains(f, StringComparison.OrdinalIgnoreCase)
            || d.ToString("d.M.yyyy", Ua.Culture).Contains(f, StringComparison.OrdinalIgnoreCase)
            || d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture).Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    private static PropertyRow ToRow(PropertyItem item, decimal qty, bool reservedVisual) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Unit = item.Unit,
        Category = item.Category,
        UnitPrice = item.UnitPrice,
        Quantity = qty,
        IsReserved = reservedVisual,
        IsFullyReserved = reservedVisual && item.Quantity <= 0.0001m,
        IsWrittenOff = item.FullyWrittenOff
    };

    private void OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is PropertyRow row)
            ApplyPropertyRowStyle(e.Row, row);
    }

    /// <summary>Рожевий — повний резерв; помаранчевий — частковий резерв або в чернетці документа.</summary>
    private static SolidColorBrush BrushForReservedVisual(bool fullyReserved) =>
        fullyReserved
            ? new SolidColorBrush(Color.FromRgb(0xF8, 0xBB, 0xD0))
            : new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x80));

    private bool IsInOpenDraft(Guid propertyId) =>
        _draft != null && _draft.Lines.Any(l => l.PropertyId == propertyId);

    private void ApplyPropertyRowStyle(DataGridRow dgRow, PropertyRow row)
    {
        var text = (Brush)FindResource("TextPrimary");

        if (row.IsReserved)
        {
            dgRow.Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            dgRow.Background = BrushForReservedVisual(row.IsFullyReserved);
            return;
        }

        // У відкритій чернетці (ще без збереження) — та сама рожева/помаранчева логіка
        if (IsInOpenDraft(row.Id))
        {
            dgRow.Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            dgRow.Background = BrushForReservedVisual(IsDraftTakingAllFree(row.Id));
            return;
        }

        if (row.NeedsKindPick && _view == ViewMode.Movements)
        {
            dgRow.Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            dgRow.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xCD));
            return;
        }

        if (row.IsWrittenOff && _view == ViewMode.Movements)
        {
            dgRow.Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            dgRow.Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xE6, 0xEB));
            return;
        }

        if (row.Quantity < -0.0001m && _view is ViewMode.Stock or ViewMode.FixedAssets)
        {
            dgRow.Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C));
            dgRow.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE));
            dgRow.FontWeight = FontWeights.SemiBold;
            return;
        }

        dgRow.Foreground = text;
        dgRow.Background = Brushes.Transparent;
        dgRow.FontWeight = FontWeights.Normal;
    }

    /// <summary>Чернетка забирає весь доступний залишок → як «повний резерв» (рожевий).</summary>
    private bool IsDraftTakingAllFree(Guid propertyId)
    {
        if (_draft == null) return false;
        var item = AppStore.Instance.Data.Properties.FirstOrDefault(p => p.Id == propertyId);
        if (item == null) return false;
        var available = item.Quantity + item.ReservedQuantity;
        var draftQty = _draft.Lines.Where(l => l.PropertyId == propertyId).Sum(l => l.Quantity);
        return draftQty >= available - 0.0001m;
    }

    private void RestylePropertyRows()
    {
        foreach (var item in PropertyGrid.Items)
        {
            if (item is PropertyRow row
                && PropertyGrid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow dg)
                ApplyPropertyRowStyle(dg, row);
        }
    }

    private void BuildColumns()
    {
        _buildingColumns = true;
        _sortButtons.Clear();
        PropertyGrid.Columns.Clear();

        if (_view == ViewMode.Strongpoints)
        {
            var nameTemplate = new DataTemplate();
            var nameFactory = new FrameworkElementFactory(typeof(StrongpointNameBlock));
            nameTemplate.VisualTree = nameFactory;
            PropertyGrid.Columns.Add(new DataGridTemplateColumn
            {
                CellTemplate = nameTemplate,
                Header = MakeHeader("Name", "Назва опорного пункту"),
                MinWidth = 70,
                CanUserSort = false,
                SortMemberPath = "Name"
            });
            PropertyGrid.Columns.Add(new DataGridTextColumn
            {
                Binding = new Binding(nameof(StrongpointRow.RegNumber)),
                Header = MakeHeader("RegNumber", "Реєстраційний номер"),
                MinWidth = 70,
                CanUserSort = false,
                SortMemberPath = "RegNumber",
                ElementStyle = CenterCellStyle()
            });
            PropertyGrid.Columns.Add(new DataGridTextColumn
            {
                Binding = new Binding(nameof(StrongpointRow.CommissioningDate))
                {
                    StringFormat = "dd.MM.yyyy",
                    ConverterCulture = Ua.Culture,
                    TargetNullValue = ""
                },
                Header = MakeHeader("Date", "Дата введення в експлуатацію"),
                MinWidth = 70,
                CanUserSort = false,
                SortMemberPath = "Date",
                ElementStyle = CenterCellStyle()
            });
            PropertyGrid.Columns.Add(new DataGridTextColumn
            {
                Binding = Ua.Number(nameof(StrongpointRow.TotalValue), "#,##0.00"),
                Header = MakeHeader("TotalValue", "Вартість опорного пункту"),
                MinWidth = 70,
                CanUserSort = false,
                SortMemberPath = "TotalValue",
                ElementStyle = RightCellStyle()
            });
            _buildingColumns = false;
            RebuildFilters();
            ApplyColumnLayout();
            return;
        }

        if (_view == ViewMode.Movements)
        {
            PropertyGrid.Columns.Add(new DataGridTextColumn
            {
                Binding = new Binding(nameof(PropertyRow.Date))
                {
                    StringFormat = "dd.MM.yyyy",
                    ConverterCulture = Ua.Culture,
                    TargetNullValue = ""
                },
                Header = MakeHeader("Date", "Дата"),
                MinWidth = 70,
                CanUserSort = false,
                SortMemberPath = "Date",
                ElementStyle = CenterCellStyle()
            });
            PropertyGrid.Columns.Add(new DataGridTextColumn
            {
                Binding = new Binding(nameof(PropertyRow.MoveKind)),
                Header = MakeHeader("MoveKind", "Тип руху"),
                MinWidth = 70,
                CanUserSort = false,
                SortMemberPath = "MoveKind",
                ElementStyle = CenterCellStyle()
            });
            PropertyGrid.Columns.Add(new DataGridTextColumn
            {
                Binding = new Binding(nameof(PropertyRow.DocumentNumber)),
                Header = MakeHeader("DocumentNumber", "Номер"),
                MinWidth = 70,
                CanUserSort = false,
                SortMemberPath = "DocumentNumber",
                ElementStyle = CenterCellStyle()
            });
        }

        var settings = AppStore.Instance.Data.Settings.Columns.OrderBy(c => c.Order).ToList();
        foreach (var st in settings.Where(c => c.Visible))
        {
            var def = ColumnCatalog.All.First(c => c.Key == st.Key);
            DataGridTextColumn col = st.Key switch
            {
                "UnitPrice" => new DataGridTextColumn
                {
                    Binding = Ua.Number(nameof(PropertyRow.UnitPrice), DecimalFormat(Decimals(st)))
                },
                "Quantity" => new DataGridTextColumn
                {
                    Binding = Ua.Number(nameof(PropertyRow.Quantity), DecimalFormat(Decimals(st)))
                },
                _ => new DataGridTextColumn { Binding = new Binding(st.Key) }
            };
            col.Header = MakeHeader(st.Key, def.Aliases[0]);
            col.MinWidth = 70;
            col.CanUserSort = false;
            col.SortMemberPath = st.Key;
            col.ElementStyle = st.Key switch
            {
                "Unit" or "Category" => CenterCellStyle(),
                "UnitPrice" or "Quantity" => RightCellStyle(),
                _ => LeftCellStyle()
            };
            PropertyGrid.Columns.Add(col);
        }
        _buildingColumns = false;
        RebuildFilters();
        ApplyColumnLayout();
    }

    private static int Decimals(ColumnSetting st)
    {
        if (st.DecimalPlaces is >= 0 and <= 8) return st.DecimalPlaces.Value;
        return st.Key == "UnitPrice" ? 4 : st.Key == "Quantity" ? 3 : 2;
    }

    private static string DecimalFormat(int places) => Ua.DecimalFormat(places);

    private static Style LeftCellStyle()
    {
        var s = new Style(typeof(TextBlock));
        s.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Left));
        s.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        s.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        s.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 0, 8, 0)));
        return s;
    }

    private static Style RightCellStyle()
    {
        var s = new Style(typeof(TextBlock));
        s.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        s.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        s.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 0, 8, 0)));
        return s;
    }

    private static Style CenterCellStyle()
    {
        var s = new Style(typeof(TextBlock));
        s.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
        s.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        s.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
        return s;
    }

    private FrameworkElement MakeHeader(string key, string title)
    {
        var grid = new Grid { Tag = key };
        grid.Children.Add(new TextBlock
        {
            Text = title,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 0, 22, 0)
        });
        var btn = new Button
        {
            Content = CreateSortGlyph(key),
            Style = (Style)FindResource("CompactIconButton"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = key
        };
        btn.Click += (_, e) =>
        {
            CycleSort(key);
            e.Handled = true;
        };
        grid.Children.Add(btn);
        _sortButtons[key] = btn;
        return grid;
    }

    private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (_buildingColumns) return;
        if (FindParent<Thumb>(e.OriginalSource as DependencyObject) != null) return;
        if (FindParent<Button>(e.OriginalSource as DependencyObject) is Button btn
            && btn.Style == FindResource("CompactIconButton") as Style)
            return;
        var header = FindParent<DataGridColumnHeader>(e.OriginalSource as DependencyObject);
        var key = header?.Column?.SortMemberPath;
        if (string.IsNullOrEmpty(key)) return;
        CycleSort(key);
        e.Handled = true;
    }

    private void OnColumnThumbDragStarted(object sender, DragStartedEventArgs e)
    {
        if (_applyingLayout || _buildingColumns) return;
        if (!TryGripColumn(e.OriginalSource as DependencyObject, out var resized, out var partner)) return;
        _resizingColumns = true;
        _resizeColumn = resized;
        _resizePartner = partner;
        _resizeColumnStart = resized.ActualWidth;
        _resizeAccumulated = 0;
        _resizePairWidth = resized.ActualWidth + (partner?.ActualWidth ?? 0);
        // Таблиця в цей момент сама переводить стовпець у ширину «як текст».
        // Повертаємо пікселі, які вже стояли, щоб сусіди не стиснулись.
        SetColumnPixels(resized, Math.Max(MinColumnWidth(resized.SortMemberPath ?? ""), resized.ActualWidth));
    }

    private void OnColumnThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_resizingColumns || _resizeColumn == null) return;
        _resizeAccumulated += e.HorizontalChange;
        var attempted = _resizeColumnStart + _resizeAccumulated;
        var minResized = MinColumnWidth(_resizeColumn.SortMemberPath ?? "");
        if (_resizePartner == null)
        {
            SetColumnPixels(_resizeColumn, Math.Max(minResized, attempted));
            return;
        }

        var minPartner = MinColumnWidth(_resizePartner.SortMemberPath ?? "");
        var width = Math.Clamp(attempted, minResized, Math.Max(minResized, _resizePairWidth - minPartner));
        SetColumnPixels(_resizeColumn, width);
        SetColumnPixels(_resizePartner, _resizePairWidth - width);
    }

    private void OnColumnThumbDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!_resizingColumns) return;
        _resizingColumns = false;
        _resizeColumn = null;
        _resizePartner = null;
        RememberColumnWeights();
        ApplyColumnLayout();
    }

    /// <summary>Подвійний клік по межі підганяє стовпець під текст і здавлює сусідів. Скасовуємо це.</summary>
    private void OnColumnGripperDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!TryGripColumn(e.OriginalSource as DependencyObject, out _, out _)) return;
        _resizingColumns = false;
        _resizeColumn = null;
        _resizePartner = null;
        ApplyColumnLayout();
        e.Handled = true;
    }

    private bool TryGripColumn(DependencyObject? src, out DataGridColumn resized, out DataGridColumn? partner)
    {
        resized = null!;
        partner = null;
        if (src is not Thumb thumb) return false;
        if (thumb.Name is not ("PART_LeftHeaderGripper" or "PART_RightHeaderGripper")) return false;
        var header = FindParent<DataGridColumnHeader>(thumb);
        if (header?.Column == null) return false;

        if (thumb.Name == "PART_LeftHeaderGripper")
        {
            var prev = AdjacentColumn(header.Column, -1);
            if (prev == null) return false;
            resized = prev;
            partner = header.Column;
            return true;
        }

        resized = header.Column;
        partner = AdjacentColumn(header.Column, 1) ?? AdjacentColumn(header.Column, -1);
        return true;
    }

    private DataGridColumn? AdjacentColumn(DataGridColumn col, int direction)
    {
        return PropertyGrid.Columns
            .Where(c => c.Visibility == Visibility.Visible && (direction < 0 ? c.DisplayIndex < col.DisplayIndex : c.DisplayIndex > col.DisplayIndex))
            .OrderBy(c => direction < 0 ? -c.DisplayIndex : c.DisplayIndex)
            .FirstOrDefault();
    }

    private void SetColumnPixels(DataGridColumn col, double width)
    {
        var key = col.SortMemberPath ?? "";
        col.MinWidth = MinColumnWidth(key);
        col.MaxWidth = double.PositiveInfinity;
        var next = Math.Round(Math.Max(MinColumnWidth(key), width), 1);
        if (col.Width.IsAbsolute && Math.Abs(col.Width.Value - next) < 0.4) return;
        col.Width = new DataGridLength(next);
    }

    /// <summary>Частки ширини вікна. Сума близько 10. Пікселі таблиці сюди не пишемо.</summary>
    private void RememberColumnWeights()
    {
        var cols = PropertyGrid.Columns.Where(c => c.Visibility == Visibility.Visible).ToList();
        var total = cols.Sum(c => c.Width.IsAbsolute ? c.Width.Value : Math.Max(0, c.ActualWidth));
        if (total < 1) return;
        var settings = AppStore.Instance.Data.Settings.Columns;
        foreach (var col in cols)
        {
            var key = col.SortMemberPath ?? "";
            if (key.Length == 0) continue;
            var px = col.Width.IsAbsolute ? col.Width.Value : col.ActualWidth;
            var weight = Math.Clamp(Math.Round(px / total * 10.0, 2), 0.15, 40);
            _sessionColumnWeights[key] = weight;
            var st = settings.FirstOrDefault(c => c.Key == key);
            if (st == null) continue;
            st.WidthCustom = true;
            st.Width = weight;
        }
        AppStore.Instance.Save(false);
    }

    private void ApplyColumnLayout()
    {
        if (_applyingLayout || _buildingColumns || _resizingColumns || PropertyGrid.Columns.Count == 0) return;

        var viewport = ColumnViewport();
        if (viewport < 1) return;

        var settings = AppStore.Instance.Data.Settings.Columns;
        var cols = PropertyGrid.Columns.Where(c => c.Visibility == Visibility.Visible).ToList();
        var weights = cols.Select(c => ColumnWeight(c.SortMemberPath ?? "", settings.FirstOrDefault(s => s.Key == c.SortMemberPath))).ToArray();
        var mins = cols.Select(c => MinColumnWidth(c.SortMemberPath ?? "")).ToArray();
        var widths = AllocateColumnWidths(viewport, weights, mins);

        _applyingLayout = true;
        try
        {
            // Hidden, не Disabled: інакше таблиця сама стискає стовпці, щоб улізти в вікно.
            PropertyGrid.HorizontalScrollBarVisibility = widths.Sum() > viewport + 1
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Hidden;
            for (var i = 0; i < cols.Count; i++)
            {
                cols[i].MinWidth = mins[i];
                cols[i].MaxWidth = double.PositiveInfinity;
                cols[i].Width = new DataGridLength(widths[i]);
            }
        }
        finally
        {
            _applyingLayout = false;
        }

        Dispatcher.BeginInvoke(() =>
        {
            FitHeaders();
            SyncFilterWidths();
        }, DispatcherPriority.Loaded);
    }

    private double ColumnViewport()
    {
        var width = PropertyGrid.ActualWidth;
        if (width < 1) return 0;
        var scroll = FindChild<ScrollViewer>(PropertyGrid);
        if (scroll != null && scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible)
            width -= SystemParameters.VerticalScrollBarWidth;
        return Math.Max(0, width - 2);
    }

    private static double[] AllocateColumnWidths(double viewport, double[] weights, double[] mins)
    {
        var result = new double[weights.Length];
        var sumMin = mins.Sum();
        if (sumMin >= viewport - 0.5)
        {
            for (var i = 0; i < mins.Length; i++) result[i] = mins[i];
            return result;
        }

        var extra = viewport - sumMin;
        var sumWeight = weights.Sum();
        if (sumWeight < 0.01) sumWeight = weights.Length;
        for (var i = 0; i < weights.Length; i++)
            result[i] = Math.Round(mins[i] + extra * (weights[i] / sumWeight), 1);
        var drift = Math.Round(viewport - result.Sum(), 1);
        result[^1] = Math.Max(mins[^1], result[^1] + drift);
        return result;
    }

    private static double MinColumnWidth(string key) => key switch
    {
        "Name" => 180,
        "Date" => 108,
        "MoveKind" => 110,
        "DocumentNumber" => 100,
        "RegNumber" => 140,
        "TotalValue" => 120,
        "Quantity" or "UnitPrice" => 96,
        "Unit" or "Category" => 80,
        _ => 80
    };

    private double ColumnWeight(string key, ColumnSetting? st)
    {
        if (_sessionColumnWeights.TryGetValue(key, out var session) && session is >= 0.15 and <= 40)
            return session;
        if (st?.WidthCustom == true && st.Width is >= 0.15 and <= 40)
            return st.Width;

        return key switch
        {
            "Name" => 3.2,
            "Unit" => 0.9,
            "Category" => 0.9,
            "UnitPrice" => 1.0,
            "Quantity" => 0.9,
            "Date" => 1.0,
            "MoveKind" => 1.1,
            "DocumentNumber" => 0.9,
            "RegNumber" => 1.3,
            "TotalValue" => 1.4,
            _ => 1.0
        };
    }

    private double MeasureHeader(string text)
    {
        var dip = 1.0;
        try { dip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { /* not loaded yet */ }
        var ft = new FormattedText(
            text,
            CultureInfo.GetCultureInfo("uk-UA"),
            FlowDirection.LeftToRight,
            ThemeService.UiTypeface(FontWeights.Normal),
            16,
            Brushes.Black,
            dip);
        return Math.Ceiling(ft.Width) + 50;
    }

    private void SyncFilterWidths()
    {
        if (FilterGrid.ColumnDefinitions.Count != PropertyGrid.Columns.Count) return;
        for (var i = 0; i < PropertyGrid.Columns.Count; i++)
        {
            var col = PropertyGrid.Columns[i];
            // Фільтри в тих самих * -частках, що й стовпці таблиці
            if (col.Width.IsStar)
            {
                var star = col.Width.Value;
                var current = FilterGrid.ColumnDefinitions[i].Width;
                if (current.IsStar && Math.Abs(current.Value - star) < 0.001) continue;
                FilterGrid.ColumnDefinitions[i].Width = new GridLength(star, GridUnitType.Star);
                continue;
            }
            var w = col.Width.IsAbsolute ? col.Width.Value : col.ActualWidth;
            if (w < 1) continue;
            var cur = FilterGrid.ColumnDefinitions[i].Width;
            if (cur.IsAbsolute && Math.Abs(cur.Value - w) < 0.5) continue;
            FilterGrid.ColumnDefinitions[i].Width = new GridLength(w);
        }
        SyncTotalsWidths();
    }

    private void SyncTotalsWidths()
    {
        if (TotalsGrid.ColumnDefinitions.Count != PropertyGrid.Columns.Count) return;
        for (var i = 0; i < PropertyGrid.Columns.Count; i++)
        {
            var col = PropertyGrid.Columns[i];
            var w = col.Width.IsAbsolute ? col.Width.Value : col.ActualWidth;
            if (w < 1) continue;
            var cur = TotalsGrid.ColumnDefinitions[i].Width;
            if (cur.IsAbsolute && Math.Abs(cur.Value - w) < 0.5) continue;
            TotalsGrid.ColumnDefinitions[i].Width = new GridLength(w);
        }
    }

    private void RebuildFilters()
    {
        FilterGrid.Children.Clear();
        FilterGrid.ColumnDefinitions.Clear();
        _filterBoxes.Clear();

        var i = 0;
        foreach (var col in PropertyGrid.Columns)
        {
            var key = col.SortMemberPath ?? "";
            FilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var host = new Grid { Margin = new Thickness(0, 0, 4, 0) };
            // PaddingRight під корзинку ~40, щоб текст не заходив під іконку
            var box = new TextBox { Padding = new Thickness(8, 6, 40, 6) };
            if (_filters.TryGetValue(key, out var v)) box.Text = v;
            var clearBtn = new Button
            {
                Style = (Style)FindResource("FilterClearButton"),
                Content = CreateTrashGlyph(),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            box.Tag = clearBtn;
            PaintFilterBox(box);
            var captured = key;
            box.TextChanged += (_, _) =>
            {
                _filters[captured] = box.Text;
                PaintFilterBox(box);
                RefreshAll();
            };
            clearBtn.Click += (_, e) =>
            {
                box.Text = "";
                box.Focus();
                e.Handled = true;
            };
            host.Children.Add(box);
            host.Children.Add(clearBtn);
            Grid.SetColumn(host, i);
            FilterGrid.Children.Add(host);
            _filterBoxes[key] = box;
            i++;
        }
        RebuildTotals();
        FitHeaders();
        SyncFilterWidths();
    }

    private void RebuildTotals()
    {
        TotalsGrid.Children.Clear();
        TotalsGrid.ColumnDefinitions.Clear();
        _totalBlocks.Clear();
        if (_view is not (ViewMode.Stock or ViewMode.FixedAssets or ViewMode.Movements)) return;

        var i = 0;
        foreach (var col in PropertyGrid.Columns)
        {
            var key = col.SortMemberPath ?? "";
            TotalsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var right = key is "Quantity" or "UnitPrice";
            var block = new TextBlock
            {
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = right ? TextAlignment.Right : TextAlignment.Left,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 4, 8, 4)
            };
            block.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            Grid.SetColumn(block, i);
            TotalsGrid.Children.Add(block);
            if (key.Length > 0) _totalBlocks[key] = block;
            i++;
        }
    }

    /// <summary>
    /// Розмір стрілки сортування — спільний для всіх тем (фіксована геометрія Path).
    /// Не текстовий гліф шрифту теми — інакше величина залежить від FontFamily.
    /// </summary>
    private const double SortGlyphBox = 11;
    private const double TrashGlyphBox = 14;

    /// <summary>
    /// Закрита корзинка очищення фільтра (варіант №2) — Path/EvenOdd, колір з теми (як стрілки сортування).
    /// </summary>
    private FrameworkElement CreateTrashGlyph()
    {
        // Ручка + кришка + корпус; дві щілини — «дірки» через EvenOdd.
        var geo = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geo.Children.Add(Geometry.Parse(
            "M 5.6,2.15 L 5.6,1.15 C 5.6,0.45 8.4,0.45 8.4,1.15 L 8.4,2.15 Z"));
        geo.Children.Add(Geometry.Parse(
            "M 2.1,2.15 L 11.9,2.15 L 11.9,3.55 L 2.1,3.55 Z"));
        geo.Children.Add(Geometry.Parse(
            "M 3.15,3.7 L 10.85,3.7 L 10.05,12.55 C 10.0,12.95 9.65,13.2 9.25,13.2 " +
            "L 4.75,13.2 C 4.35,13.2 4.0,12.95 3.95,12.55 Z"));
        geo.Children.Add(Geometry.Parse("M 5.45,5.35 L 6.25,5.35 L 6.25,10.55 L 5.45,10.55 Z"));
        geo.Children.Add(Geometry.Parse("M 7.75,5.35 L 8.55,5.35 L 8.55,10.55 L 7.75,10.55 Z"));

        var path = new System.Windows.Shapes.Path
        {
            Data = geo,
            Stretch = Stretch.Uniform,
            Width = 13,
            Height = 13,
            SnapsToDevicePixels = true
        };
        path.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "GhostFg");
        return new Viewbox
        {
            Width = TrashGlyphBox,
            Height = TrashGlyphBox,
            Stretch = Stretch.Uniform,
            Child = path,
            SnapsToDevicePixels = true
        };
    }

    private FrameworkElement CreateSortGlyph(string key)
    {
        var state = _sortKey == key ? _sortState : 0;
        // Фіксована геометрія в логічних одиницях → Viewbox завжди SortGlyphBox×SortGlyphBox.
        var data = state switch
        {
            1 => "M 5,0.8 L 9.5,9.2 L 0.5,9.2 Z",           // ↑
            2 => "M 0.5,0.8 L 9.5,0.8 L 5,9.2 Z",           // ↓
            _ => "M 5,0.4 L 8.6,4.2 L 1.4,4.2 Z M 1.4,5.8 L 8.6,5.8 L 5,9.6 Z" // ↕
        };
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data),
            Stretch = Stretch.Uniform,
            Width = 10,
            Height = 10,
            SnapsToDevicePixels = true
        };
        path.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "GhostFg");
        return new Viewbox
        {
            Width = SortGlyphBox,
            Height = SortGlyphBox,
            Stretch = Stretch.Uniform,
            Child = path,
            SnapsToDevicePixels = true
        };
    }

    private void CycleSort(string key)
    {
        if (_sortKey != key) { _sortKey = key; _sortState = 1; }
        else _sortState = _sortState == 1 ? 2 : _sortState == 2 ? 0 : 1;
        foreach (var kv in _sortButtons)
            kv.Value.Content = CreateSortGlyph(kv.Key);
        RefreshAll();
    }

    private void ColumnsChanged(object sender, EventArgs e)
    {
        if (_buildingColumns) return;
        PersistColumnLayout();
        RebuildFilters();
        ApplyColumnLayout();
    }

    private void PersistColumnLayout()
    {
        var settings = AppStore.Instance.Data.Settings.Columns;
        for (var i = 0; i < PropertyGrid.Columns.Count; i++)
        {
            var col = PropertyGrid.Columns[i];
            var key = col.SortMemberPath;
            var st = settings.FirstOrDefault(c => c.Key == key);
            if (st == null) continue;
            st.Order = i;
        }
        AppStore.Instance.Save(false);
    }

    private void FitHeaders()
    {
        foreach (var col in PropertyGrid.Columns)
        {
            var key = col.SortMemberPath;
            if (key is "Date" or "MoveKind" or "DocumentNumber" or "RegNumber" or "TotalValue") continue;
            var width = col.ActualWidth;
            if (width < 1) continue;
            string[] aliases;
            if (_view == ViewMode.Strongpoints && key == "Name")
            {
                aliases = new[] { "Назва опорного пункту", "Назва оп. пункту", "Назва ОП", "ОП" };
            }
            else
            {
                var def = ColumnCatalog.All.FirstOrDefault(c => c.Key == key);
                if (def.Key == null) continue;
                aliases = def.Aliases;
            }
            SetHeaderText(col, FitAlias(aliases, width));
        }
    }

    private static void SetHeaderText(DataGridColumn col, string text)
    {
        if (col.Header is not Grid grid) return;
        var tb = grid.Children.OfType<TextBlock>().FirstOrDefault();
        if (tb != null && tb.Text != text) tb.Text = text;
    }

    private static string FitAlias(string[] aliases, double width)
    {
        var est = width / 9.5;
        foreach (var a in aliases)
            if (a.Length <= est) return a;
        return aliases[^1];
    }

    private void OpenColumns(object sender, RoutedEventArgs e)
    {
        ColsList.Children.Clear();
        foreach (var st in AppStore.Instance.Data.Settings.Columns.OrderBy(c => c.Order))
        {
            var def = ColumnCatalog.All.First(c => c.Key == st.Key);
            var block = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
            var cb = new CheckBox
            {
                Content = def.Aliases[0],
                IsChecked = st.Visible,
                FontSize = 16,
                MinHeight = 36,
                Tag = st.Key
            };
            cb.Click += (_, _) =>
            {
                st.Visible = cb.IsChecked == true;
                if (!AppStore.Instance.Data.Settings.Columns.Any(c => c.Visible))
                {
                    st.Visible = true;
                    cb.IsChecked = true;
                }
                AppStore.Instance.Save();
                BuildColumns();
                RefreshAll();
                ApplyColumnLayout();
            };
            block.Children.Add(cb);
            if (st.Key is "UnitPrice" or "Quantity")
                block.Children.Add(DecimalEditor(st));
            ColsList.Children.Add(block);
        }
        ColsPopup.IsOpen = true;
    }

    private UIElement DecimalEditor(ColumnSetting st)
    {
        var value = new TextBlock
        {
            Text = Decimals(st).ToString(),
            FontSize = 16,
            MinWidth = 28,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(24, 4, 0, 0)
        };
        row.Children.Add(new TextBlock
        {
            Text = "Знаків після коми:",
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        var minus = new Button { Content = "−", Width = 40, MinHeight = 40, Padding = new Thickness(0) };
        var plus = new Button { Content = "+", Width = 40, MinHeight = 40, Padding = new Thickness(0) };
        minus.Click += (_, _) => ChangeDecimals(st, -1, value);
        plus.Click += (_, _) => ChangeDecimals(st, 1, value);
        row.Children.Add(minus);
        row.Children.Add(value);
        row.Children.Add(plus);
        return row;
    }

    private void ChangeDecimals(ColumnSetting st, int delta, TextBlock label)
    {
        var next = Math.Clamp(Decimals(st) + delta, 0, 8);
        st.DecimalPlaces = next;
        label.Text = next.ToString();
        AppStore.Instance.Save(false);
        if (PropertyGrid.Columns.FirstOrDefault(c => c.SortMemberPath == st.Key) is DataGridTextColumn col)
        {
            var path = st.Key == "UnitPrice" ? nameof(PropertyRow.UnitPrice) : nameof(PropertyRow.Quantity);
            col.Binding = Ua.Number(path, DecimalFormat(next));
        }
        PropertyGrid.Items.Refresh();
        if (_draft != null && st.Key == "Quantity")
            RefreshDraftLines();
    }

    private void RowClick(object sender, MouseButtonEventArgs e)
    {
        if (_draft != null) return;
        if (FindParent<DataGridColumnHeader>(e.OriginalSource as DependencyObject) != null) return;

        if (_view == ViewMode.Strongpoints)
        {
            if (PropertyGrid.SelectedItem is not StrongpointRow sp) return;
            var card = new StrongpointCardWindow(sp) { Owner = this };
            if (card.ShowDialog() == true)
            {
                if (card.DraftToOpen != null) OpenDraft(card.DraftToOpen);
                else RefreshAll();
            }
            return;
        }

        if (PropertyGrid.SelectedItem is not PropertyRow row) return;

        if (_view == ViewMode.Movements && row.NeedsKindPick && row.DocumentId is Guid needDocId)
        {
            if (TryPickMovementKind(needDocId, row.Id))
                RefreshAll();
            return;
        }

        if (_view == ViewMode.Movements && row.DocumentId is Guid docId)
        {
            OpenDocumentView(docId, row.Id);
            return;
        }

        var item = AppStore.Instance.Data.Properties.FirstOrDefault(p => p.Id == row.Id);
        if (item == null) return;
        var propCard = new PropertyCardWindow(item) { Owner = this };
        if (propCard.ShowDialog() == true)
        {
            if (propCard.DraftToOpen != null) OpenDraft(propCard.DraftToOpen);
            else RefreshAll();
        }
    }

    private void OpenDocumentView(Guid documentId, Guid? highlightPropertyId = null)
    {
        var doc = AppStore.Instance.Data.Documents.FirstOrDefault(d => d.Id == documentId);
        if (doc == null)
        {
            MessageBox.Show("Документ не знайдено.", "Обозний", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var view = new DocumentViewWindow(doc, highlightPropertyId) { Owner = this };
        if (view.ShowDialog() != true) return;
        if (view.Deleted)
        {
            if (_draft?.Id == doc.Id)
            {
                _draft = null;
                DraftPanel.Visibility = Visibility.Collapsed;
            }
            RefreshAll();
            return;
        }
        if (view.OpenAsDraft)
            OpenDraft(view.DraftToOpen ?? doc);
    }

    /// <summary>Жовтий рядок у «Рух майна»: ручний вибір робочого типу руху.</summary>
    private bool TryPickMovementKind(Guid documentId, Guid propertyId)
    {
        var choices = MoveKindNames.WorkingKinds
            .Concat(new[] { MoveKindNames.Issue })
            .Select(k => new WriteOffChoice(k, ImportGrouping.TypeOf(MoveKindNames.ToImportKind(k))))
            .ToArray();
        var pick = new TypePickWindow(
            "Тип руху невизначений",
            "Оберіть робочий тип руху для цього рядка (не назву документа).",
            choices)
        { Owner = this };
        if (pick.ShowDialog() != true || pick.Selected == null) return false;
        return AppStore.Instance.SetMovementKind(documentId, propertyId, pick.Selected.WorkTypeName);
    }

    private void GridMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Сенсор обробляємо через Touch*; миша — через OLE DragDrop
        if (e.StylusDevice != null) return;
        _dragStart = e.GetPosition(this);
        _dragRow = RowFrom(e.OriginalSource as DependencyObject);
        _fingerDrag = false;
    }

    private void GridMouseMove(object sender, MouseEventArgs e)
    {
        if (e.StylusDevice != null) return;
        if (e.LeftButton != MouseButtonState.Pressed || _dragRow == null || _draft == null) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _dragStart.X) < 8 && Math.Abs(p.Y - _dragStart.Y) < 8) return;
        var row = _dragRow;
        _dragRow = null;
        DragDrop.DoDragDrop(PropertyGrid, row, DragDropEffects.Copy);
    }

    private void GridMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_fingerDrag) _dragRow = null;
    }

    private void GridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_draft == null) return;
        if (FindParent<DataGridColumnHeader>(e.OriginalSource as DependencyObject) != null) return;
        var row = RowFrom(e.OriginalSource as DependencyObject)
                  ?? PropertyGrid.SelectedItem as PropertyRow;
        if (row == null) return;
        AddToDraft(row);
        e.Handled = true;
    }

    private void GridTouchDown(object sender, TouchEventArgs e)
    {
        if (_draft == null) return;
        if (FindParent<DataGridColumnHeader>(e.OriginalSource as DependencyObject) != null) return;
        _dragStart = e.GetTouchPoint(this).Position;
        _dragRow = RowFrom(e.OriginalSource as DependencyObject);
        _fingerDrag = false;
        _dragTouch = e.TouchDevice;
    }

    private void GridTouchMove(object sender, TouchEventArgs e)
    {
        if (_draft == null || _dragRow == null || _dragTouch == null) return;
        if (e.TouchDevice != _dragTouch) return;

        var p = e.GetTouchPoint(this).Position;
        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;
        if (!_fingerDrag)
        {
            if (dx * dx + dy * dy < 24 * 24) return;
            _fingerDrag = true;
            PropertyGrid.CaptureTouch(e.TouchDevice);
            HighlightDraftDropTarget(true);
        }

        HighlightDraftDropTarget(IsPointOverDraft(p));
        e.Handled = true; // не даємо таблиці скролитись під час перетягування
    }

    private void GridTouchUp(object sender, TouchEventArgs e)
    {
        if (_dragTouch == null || e.TouchDevice != _dragTouch) return;
        var wasDrag = _fingerDrag;
        FinishFingerDrag(e.GetTouchPoint(this).Position);
        e.Handled = wasDrag;
    }

    private void GridLostTouchCapture(object sender, TouchEventArgs e)
    {
        if (_dragTouch == null || e.TouchDevice != _dragTouch) return;
        FinishFingerDrag(e.GetTouchPoint(this).Position);
    }

    private void FinishFingerDrag(Point p)
    {
        if (_dragRow == null && !_fingerDrag && _dragTouch == null) return;

        var row = _dragRow;
        var drop = _fingerDrag && row != null && _draft != null && IsPointOverDraft(p);
        var touch = _dragTouch;

        _dragRow = null;
        _fingerDrag = false;
        _dragTouch = null;
        HighlightDraftDropTarget(false);

        if (touch != null && PropertyGrid.TouchesCaptured.Contains(touch))
            PropertyGrid.ReleaseTouchCapture(touch);

        if (drop && row != null)
        {
            var structId = StructureAtPoint(p);
            AddToDraft(row, structId);
        }
    }

    private bool IsPointOverDraft(Point pInWindow)
    {
        if (DraftPanel.Visibility != Visibility.Visible) return false;
        var topLeft = DraftPanel.TransformToAncestor(this).Transform(new Point(0, 0));
        var rect = new Rect(topLeft, new Size(DraftPanel.ActualWidth, DraftPanel.ActualHeight));
        rect.Inflate(8, 12);
        return rect.Contains(pInWindow);
    }

    private Guid? StructureAtPoint(Point pInWindow)
    {
        if (StructuresHost.Visibility != Visibility.Visible) return null;
        foreach (var child in StructuresStrip.Children.OfType<FrameworkElement>())
        {
            if (child.Tag is not Guid id) continue;
            Point topLeft;
            try { topLeft = child.TransformToAncestor(this).Transform(new Point(0, 0)); }
            catch { continue; }
            var rect = new Rect(topLeft, new Size(child.ActualWidth, child.ActualHeight));
            rect.Inflate(4, 4);
            if (rect.Contains(pInWindow)) return id;
        }
        return null;
    }

    private void HighlightDraftDropTarget(bool active)
    {
        if (active)
        {
            DraftPanel.BorderBrush = (Brush)FindResource("Accent");
            DraftPanel.BorderThickness = new Thickness(2);
        }
        else
            DraftPanel.BorderThickness = new Thickness(0);
    }

    private static PropertyRow? RowFrom(DependencyObject? src)
    {
        while (src != null)
        {
            if (src is DataGridRow r) return r.Item as PropertyRow;
            src = VisualTreeHelper.GetParent(src);
        }
        return null;
    }

    private void DraftDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(PropertyRow)) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DraftDragLeave(object sender, DragEventArgs e)
    {
        // no-op: рамка документа лишається зі звичайною підсвіткою споруди
    }

    private void DraftDrop(object sender, DragEventArgs e)
    {
        if (_draft == null) return;
        if (e.Data.GetData(typeof(PropertyRow)) is not PropertyRow row) return;
        // якщо кинули прямо на споруду — обробить її Drop; інакше — в активну
        if (FindParentStructureId(e.OriginalSource as DependencyObject) is not null)
            return;
        AddToDraft(row);
        e.Handled = true;
    }

    private static Guid? FindParentStructureId(DependencyObject? src)
    {
        while (src != null)
        {
            if (src is FrameworkElement { Tag: Guid id } fe
                && fe.AllowDrop
                && (fe is Border or TextBox))
                return id;
            src = VisualTreeHelper.GetParent(src);
        }
        return null;
    }

    private void AddToDraft(PropertyRow row, Guid? structureId = null)
    {
        if (_draft == null) return;
        CaptureDraftFields();

        Guid? targetStruct = null;
        if (_draft.Type == DocType.QualityState)
        {
            targetStruct = structureId ?? _draft.ActiveStructureId;
            if (targetStruct == null || _draft.Structures.All(s => s.Id != targetStruct))
            {
                MessageBox.Show("Спочатку додайте фортифікаційну споруду.", "Обозний",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        var item = AppStore.Instance.Data.Properties.First(p => p.Id == row.Id);
        var available = item.Quantity + item.ReservedQuantity;
        if (available <= 0) available = 1;
        var used = _draft.Lines.Where(l => l.PropertyId == row.Id).Sum(l => l.Quantity);
        var remaining = available - used;
        if (remaining <= 0.0001m)
        {
            MessageBox.Show("Уся доступна кількість цього майна вже в документі.", "Обозний");
            return;
        }

        var alreadyInDoc = used > 0.0001m;
        if (!alreadyInDoc)
        {
            var present = AppStore.Instance.WasPresentOnDate(item.Id, _draft.Date);
            if (!present)
            {
                var ans = MessageBox.Show(
                    "Ви хочете додати майно, якого не було на дату документа, яку ви ввели.\n\nДодати все одно?",
                    "Обозний", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (ans != MessageBoxResult.Yes) return;
            }
        }

        var existing = _draft.Type == DocType.QualityState
            ? _draft.Lines.FirstOrDefault(l => l.PropertyId == row.Id && l.StructureId == targetStruct)
            : _draft.Lines.FirstOrDefault(l => l.PropertyId == row.Id);

        if (existing != null)
        {
            existing.Quantity += remaining;
        }
        else
        {
            var present = AppStore.Instance.WasPresentOnDate(item.Id, _draft.Date);
            _draft.Lines.Add(new DocumentLine
            {
                PropertyId = item.Id,
                Name = item.Name,
                Unit = item.Unit,
                Quantity = remaining,
                Anachronistic = !present,
                StructureId = targetStruct
            });
        }

        // скинули на іншу споруду — робимо її активною, щоб одразу було видно рядок
        if (targetStruct is Guid sid && _draft.ActiveStructureId != sid)
            SelectStructure(sid);
        else
            RefreshDraftLines();
    }

    private void QtyPlus(object sender, RoutedEventArgs e)
    {
        if (LineFrom(sender) is not { } line || _draft == null) return;
        var max = MaxQty(line);
        line.Quantity = Math.Min(max, line.Quantity + 1);
        RefreshDraftLines();
    }

    private void QtyMinusPress(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null) return;
        if (LineFrom(sender) is not { } line) return;
        if (sender is UIElement el) el.CaptureMouse();
        BeginQtyHold(line);
        e.Handled = true;
    }

    private void QtyMinusRelease(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null) return;
        StopQtyHold();
        if (sender is UIElement el && el.IsMouseCaptured) el.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void QtyMinusLostMouse(object sender, MouseEventArgs e)
    {
        if (_qtyHoldTouch != null) return;
        StopQtyHold();
    }

    private void QtyMinusTouchPress(object sender, TouchEventArgs e)
    {
        if (LineFrom(sender) is not { } line) return;
        _qtyHoldTouch = e.TouchDevice;
        if (sender is UIElement el) el.CaptureTouch(e.TouchDevice);
        BeginQtyHold(line);
        e.Handled = true;
    }

    private void QtyMinusTouchRelease(object sender, TouchEventArgs e)
    {
        if (_qtyHoldTouch != null && e.TouchDevice != _qtyHoldTouch) return;
        StopQtyHold();
        if (sender is UIElement el && _qtyHoldTouch != null && el.TouchesCaptured.Contains(_qtyHoldTouch))
            el.ReleaseTouchCapture(_qtyHoldTouch);
        _qtyHoldTouch = null;
        e.Handled = true;
    }

    private void QtyMinusLostTouch(object sender, TouchEventArgs e)
    {
        if (_qtyHoldTouch == null || e.TouchDevice != _qtyHoldTouch) return;
        StopQtyHold();
        _qtyHoldTouch = null;
    }

    private void BeginQtyHold(DocumentLine line)
    {
        _qtyHoldTimer.Stop();
        _qtyHoldLine = line;
        _qtyHoldRepeating = false;
        ApplyQtyMinus(line);
        _qtyHoldTimer.Interval = TimeSpan.FromMilliseconds(380);
        _qtyHoldTimer.Start();
    }

    private void QtyHoldTick(object? sender, EventArgs e)
    {
        if (_qtyHoldLine == null)
        {
            StopQtyHold();
            return;
        }
        ApplyQtyMinus(_qtyHoldLine);
        if (!_qtyHoldRepeating)
        {
            _qtyHoldRepeating = true;
            _qtyHoldTimer.Interval = TimeSpan.FromMilliseconds(85);
        }
        if (_qtyHoldLine.Quantity <= 0)
            StopQtyHold();
    }

    private void StopQtyHold()
    {
        _qtyHoldTimer.Stop();
        _qtyHoldLine = null;
        _qtyHoldRepeating = false;
    }

    private void ApplyQtyMinus(DocumentLine line)
    {
        if (line.Quantity <= 0) return;
        line.Quantity = Math.Max(0, line.Quantity - 1);
        RestylePropertyRows();
    }

    private void RemoveDraftLine(DocumentLine line)
    {
        if (_draft == null) return;
        _draft.Lines.Remove(line);
        RefreshDraftLines();
    }

    private void DraftLinesDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_draft == null) return;
        if (FindParent<Button>(e.OriginalSource as DependencyObject) != null) return;
        var line = DraftLineFrom(e.OriginalSource as DependencyObject)
                   ?? DraftLines.SelectedItem as DocumentLine;
        if (line == null) return;
        RemoveDraftLine(line);
        e.Handled = true;
    }

    private void DraftLinesMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null) return;
        if (FindParent<Button>(e.OriginalSource as DependencyObject) != null) return;
        _draftDragStart = e.GetPosition(this);
        _draftDragLine = DraftLineFrom(e.OriginalSource as DependencyObject);
        _draftFingerDrag = false;
    }

    private void DraftLinesMouseMove(object sender, MouseEventArgs e)
    {
        if (e.StylusDevice != null) return;
        if (e.LeftButton != MouseButtonState.Pressed || _draftDragLine == null || _draft == null) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _draftDragStart.X) < 8 && Math.Abs(p.Y - _draftDragStart.Y) < 8) return;
        var line = _draftDragLine;
        _draftDragLine = null;
        DragDrop.DoDragDrop(DraftLines, line, DragDropEffects.Move);
    }

    private void DraftLinesMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_draftFingerDrag) _draftDragLine = null;
    }

    private void DraftLinesTouchDown(object sender, TouchEventArgs e)
    {
        if (_draft == null) return;
        if (FindParent<Button>(e.OriginalSource as DependencyObject) != null) return;
        _draftDragStart = e.GetTouchPoint(this).Position;
        _draftDragLine = DraftLineFrom(e.OriginalSource as DependencyObject);
        _draftFingerDrag = false;
        _draftDragTouch = e.TouchDevice;
    }

    private void DraftLinesTouchMove(object sender, TouchEventArgs e)
    {
        if (_draft == null || _draftDragLine == null || _draftDragTouch == null) return;
        if (e.TouchDevice != _draftDragTouch) return;
        var p = e.GetTouchPoint(this).Position;
        var dx = p.X - _draftDragStart.X;
        var dy = p.Y - _draftDragStart.Y;
        if (!_draftFingerDrag)
        {
            if (dx * dx + dy * dy < 24 * 24) return;
            _draftFingerDrag = true;
            DraftLines.CaptureTouch(e.TouchDevice);
        }
        e.Handled = true;
    }

    private void DraftLinesTouchUp(object sender, TouchEventArgs e)
    {
        if (_draftDragTouch == null || e.TouchDevice != _draftDragTouch) return;
        var wasDrag = _draftFingerDrag;
        FinishDraftFingerDrag(e.GetTouchPoint(this).Position);
        e.Handled = wasDrag;
    }

    private void DraftLinesLostTouch(object sender, TouchEventArgs e)
    {
        if (_draftDragTouch == null || e.TouchDevice != _draftDragTouch) return;
        FinishDraftFingerDrag(e.GetTouchPoint(this).Position);
    }

    private void FinishDraftFingerDrag(Point p)
    {
        if (_draftDragLine == null && !_draftFingerDrag && _draftDragTouch == null) return;
        var line = _draftDragLine;
        var drop = _draftFingerDrag && line != null && _draft != null && IsPointOverPropertyGrid(p);
        var touch = _draftDragTouch;
        _draftDragLine = null;
        _draftFingerDrag = false;
        _draftDragTouch = null;
        if (touch != null && DraftLines.TouchesCaptured.Contains(touch))
            DraftLines.ReleaseTouchCapture(touch);
        if (drop && line != null)
            RemoveDraftLine(line);
    }

    private bool IsPointOverPropertyGrid(Point pInWindow)
    {
        var topLeft = PropertyGrid.TransformToAncestor(this).Transform(new Point(0, 0));
        var rect = new Rect(topLeft, new Size(PropertyGrid.ActualWidth, PropertyGrid.ActualHeight));
        rect.Inflate(8, 12);
        return rect.Contains(pInWindow);
    }

    private void PropertyGridDragOver(object sender, DragEventArgs e)
    {
        if (_draft != null && e.Data.GetDataPresent(typeof(DocumentLine)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }
        if (e.Data.GetDataPresent(typeof(PropertyRow)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void PropertyGridDrop(object sender, DragEventArgs e)
    {
        if (_draft == null) return;
        if (e.Data.GetData(typeof(DocumentLine)) is not DocumentLine line) return;
        RemoveDraftLine(line);
        e.Handled = true;
    }

    private static DocumentLine? DraftLineFrom(DependencyObject? src)
    {
        while (src != null)
        {
            if (src is DataGridRow r) return r.Item as DocumentLine;
            src = VisualTreeHelper.GetParent(src);
        }
        return null;
    }

    private void QtyEdit(object sender, RoutedEventArgs e)
    {
        if (LineFrom(sender) is not { } line || _draft == null) return;
        var calc = new CalculatorWindow(line.Quantity) { Owner = this };
        if (calc.ShowDialog() == true)
        {
            line.Quantity = Math.Min(MaxQty(line), calc.Result);
            RefreshDraftLines();
        }
    }

    private decimal MaxQty(DocumentLine line)
    {
        var item = AppStore.Instance.Data.Properties.FirstOrDefault(p => p.Id == line.PropertyId);
        if (item == null || _draft == null) return line.Quantity;
        var available = item.Quantity + item.ReservedQuantity;
        var usedElsewhere = _draft.Lines
            .Where(l => l.PropertyId == line.PropertyId && !ReferenceEquals(l, line))
            .Sum(l => l.Quantity);
        return Math.Max(line.Quantity, available - usedElsewhere);
    }

    private static DocumentLine? LineFrom(object sender) =>
        (sender as FrameworkElement)?.Tag as DocumentLine;

    private void OnActivated(object sender, EventArgs e)
    {
        if (BackupService.NeedsEndOfDayStock(AppStore.Instance.Data) &&
            AppStore.Instance.Data.Properties.Count > 0)
        {
            BackupService.Stock(AppStore.Instance.Data, "кінець_дня_після_сну");
            AppStore.Instance.Save();
        }
        BackupService.RememberWorkDay(AppStore.Instance.Data);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        _stickers?.SyncWithOwnerState(WindowState);
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!ConfirmDiscardDraft())
        {
            e.Cancel = true;
            return;
        }
        _draft = null;
        PersistColumnLayout();
        _stickers?.CloseAll();
        AppStore.Instance.CloseAppBackup();
    }
}
