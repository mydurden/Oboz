using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Obozny;

public partial class StickerWindow : Window
{
    private readonly StickerNote _note;
    private readonly Action<StickerNote> _onChanged;
    private readonly Action<StickerNote> _onDelete;
    private readonly Action<StickerNote> _onCopy;
    private bool _loading;
    private bool _chrome;
    private double _chromeExtra;
    private bool _suppressSize;
    private bool _syncingPage;
    private DispatcherTimer? _saveTimer;

    public Guid NoteId => _note.Id;

    public StickerWindow(
        StickerNote note,
        Action<StickerNote> onChanged,
        Action<StickerNote> onDelete,
        Action<StickerNote> onCopy)
    {
        _note = note;
        _onChanged = onChanged;
        _onDelete = onDelete;
        _onCopy = onCopy;
        InitializeComponent();
        StickerShell.Attach(this);
        BuildColorButtons();
        ApplyFromNote();
        Editor.LayoutUpdated += EditorLayoutUpdated;
        Editor.SizeChanged += EditorSizeChanged;
        SetChrome(false);
        if (note.Role == GuidesSticker.Role)
        {
            Editor.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            Loaded += (_, _) => ScheduleFit();
            if (!AppStore.Instance.Data.Settings.GuidesFitted)
                Loaded += FitGuidesOnce;
        }
    }

    private bool _allowClose;

    public void PrepareClose() => _allowClose = true;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        StickerShell.HideInsteadOfClose(this, e, _allowClose);
        if (!e.Cancel) base.OnClosing(e);
    }

    private void BuildColorButtons()
    {
        ColorGrid.Children.Clear();
        foreach (var sw in StickerColors.All)
        {
            var btn = new Button
            {
                Width = 44,
                Height = 36,
                MinHeight = 36,
                MinWidth = 44,
                Margin = new Thickness(3),
                Background = BrushOf(sw.Body),
                BorderBrush = BrushOf(sw.Accent),
                BorderThickness = new Thickness(3),
                ToolTip = sw.Label,
                Tag = sw.Key,
                Padding = new Thickness(0)
            };
            btn.Click += (_, _) =>
            {
                _note.ColorKey = (string)btn.Tag;
                ApplyColors();
                MenuPopup.IsOpen = false;
                PersistSoon();
            };
            ColorGrid.Children.Add(btn);
        }
    }

    private void ApplyFromNote()
    {
        _loading = true;
        Place(_note.Left, _note.Top, _note.Width, _note.Height);
        ApplyColors();
        Editor.FontSize = _note.FontSize;
        FontSizeLabel.Text = ((int)Math.Round(_note.FontSize)).ToString();
        LoadBody(_note.Rtf);
        UpdatePlaceholder();
        _loading = false;
    }

    private void ApplyColors()
    {
        var sw = StickerColors.Get(_note.ColorKey);
        var ink = BrushOf(sw.Ink);
        Root.Background = BrushOf(sw.Body);
        ChromeBar.Background = BrushOf(sw.Accent);
        Editor.Foreground = ink;
        Editor.CaretBrush = ink;
        Placeholder.Foreground = new SolidColorBrush(Color.FromArgb(0x99, ink.Color.R, ink.Color.G, ink.Color.B));
        FontSizeLabel.Foreground = ink;
        foreach (var btn in new[] { MenuBtn, CloseBtn, CollapseBtn, FontMinusBtn, FontPlusBtn, BoldBtn, ItalicBtn, UnderlineBtn, StrikeBtn, ListBtn, LinkBtn })
            btn.Foreground = ink;
        UnderlineGlyph.Foreground = ink;
        StrikeGlyph.Foreground = ink;
    }

    private static SolidColorBrush BrushOf(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    private void LoadBody(string? stored)
    {
        var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
        if (string.IsNullOrWhiteSpace(stored))
        {
            range.Text = "";
            return;
        }
        try
        {
            if (stored.StartsWith(StickerText.XamlMark, StringComparison.Ordinal))
            {
                var bytes = Convert.FromBase64String(stored[StickerText.XamlMark.Length..]);
                using var ms = new MemoryStream(bytes);
                range.Load(ms, DataFormats.Xaml);
            }
            else
            {
                byte[] bytes;
                try { bytes = Convert.FromBase64String(stored); }
                catch (FormatException) { bytes = Encoding.UTF8.GetBytes(stored); }
                using var ms = new MemoryStream(bytes);
                range.Load(ms, DataFormats.Rtf);
            }
            StickerText.ReleaseChrome(Editor.Document);
            StickerText.RememberAddresses(Editor.Document);
        }
        catch
        {
            range.Text = stored;
        }
    }

    private string CaptureBody()
    {
        try
        {
            return StickerText.Save(Editor.Document);
        }
        catch
        {
            return _note.Rtf;
        }
    }

    private bool IsEditorEmpty()
    {
        var text = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text
            .Replace("\r", "").Replace("\n", "").Trim();
        return string.IsNullOrEmpty(text);
    }

    private void UpdatePlaceholder() =>
        Placeholder.Visibility = IsEditorEmpty() ? Visibility.Visible : Visibility.Collapsed;

    private void SetChrome(bool on)
    {
        if (_chrome == on)
        {
            Toolbar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            MenuBtn.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        _chrome = on;
        MenuBtn.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!IsLoaded || WindowState != WindowState.Normal)
        {
            Toolbar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            _chromeExtra = 0;
            return;
        }

        // Панель форматування не має стискати текст: інакше з’являється прокрутка,
        // рядок вужчає і перенос лишається навіть після того, як панель зникла.
        _suppressSize = true;
        if (on)
        {
            Toolbar.Visibility = Visibility.Visible;
            Toolbar.UpdateLayout();
            var extra = Toolbar.ActualHeight;
            if (extra < 1) extra = 36;
            Height += extra;
            _chromeExtra = extra;
        }
        else
        {
            var extra = _chromeExtra;
            Toolbar.Visibility = Visibility.Collapsed;
            _chromeExtra = 0;
            if (extra > 0)
                Height = Math.Max(MinHeight, Height - extra);
        }
        _suppressSize = false;
        if (_note.Role == GuidesSticker.Role)
            ScheduleFit();
    }

    private int _fitQueued;

    private void ScheduleFit()
    {
        if (_note.Role != GuidesSticker.Role || _fitQueued > 0) return;
        _fitQueued++;
        Dispatcher.BeginInvoke(FitGuidesToText, DispatcherPriority.Background);
    }

    /// <summary>
    /// Клік звужує сторінку документа, довгий рядок переноситься і з’являється прокрутка.
    /// Тримаємо рядки цілими і розсовуємо стікер, якщо тексту бракує місця.
    /// </summary>
    private void FitGuidesToText()
    {
        _fitQueued = 0;
        if (_note.Role != GuidesSticker.Role || Editor.ActualWidth < 1) return;

        Editor.Document.PagePadding = new Thickness(0);
        if (!double.IsNaN(Editor.Document.PageWidth))
        {
            _syncingPage = true;
            Editor.Document.ClearValue(FlowDocument.PageWidthProperty);
            _syncingPage = false;
        }

        // Міряємо вже розкладений рядок, без запасу «про всяк випадок».
        _syncingPage = true;
        Editor.Document.PageWidth = 4000;
        _syncingPage = false;
        Editor.UpdateLayout();
        var textRight = MaxContentRight();
        _syncingPage = true;
        Editor.Document.ClearValue(FlowDocument.PageWidthProperty);
        _syncingPage = false;

        var changed = false;
        if (textRight > 1 && textRight < 1600)
        {
            var side = ActualWidth - Editor.ActualWidth;
            if (side < 0 || side > 60) side = 22;
            var target = Math.Max(MinWidth, Math.Ceiling(textRight + side + 4));
            if (Math.Abs(ActualWidth - target) > 3)
            {
                _suppressSize = true;
                Width = target;
                _suppressSize = false;
                changed = true;
            }
        }

        Editor.UpdateLayout();
        if (Editor.Template?.FindName("PART_ContentHost", Editor) is ScrollViewer scroll)
        {
            var overflow = scroll.ExtentHeight - scroll.ViewportHeight;
            if (overflow > 1)
            {
                _suppressSize = true;
                Height += Math.Ceiling(overflow) + 6;
                _suppressSize = false;
                changed = true;
            }
        }

        if (!changed) return;
        _note.Width = ActualWidth;
        _note.Height = Math.Max(MinHeight, ActualHeight - _chromeExtra);
        PersistSoon();
    }

    private double MaxContentRight()
    {
        double max = 0;
        foreach (var block in Editor.Document.Blocks)
            max = Math.Max(max, BlockRight(block));
        return max;
    }

    private static double BlockRight(Block block)
    {
        switch (block)
        {
            case Paragraph paragraph:
                var ptr = paragraph.ContentEnd.GetNextInsertionPosition(LogicalDirection.Backward);
                if (ptr == null) return 0;
                return ptr.GetCharacterRect(LogicalDirection.Forward).Right;
            case List list:
                double listMax = 0;
                foreach (var item in list.ListItems)
                    foreach (var child in item.Blocks)
                        listMax = Math.Max(listMax, BlockRight(child));
                return listMax;
            case Section section:
                double sectionMax = 0;
                foreach (var child in section.Blocks)
                    sectionMax = Math.Max(sectionMax, BlockRight(child));
                return sectionMax;
            default:
                return 0;
        }
    }

    private void EditorSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || _note.Role == GuidesSticker.Role) return;
        if (double.IsNaN(Editor.Document.PageWidth)) return;
        SyncPageWidth();
    }

    private void EditorLayoutUpdated(object? sender, EventArgs e)
    {
        var w = Editor.ActualWidth;
        var page = Editor.Document.PageWidth;
        if (w < 1 || _syncingPage || double.IsNaN(page)) return;
        if (_note.Role == GuidesSticker.Role)
        {
            // Клік записує вузьку ширину сторінки. Для цього стікера її прибираємо,
            // щоб рядок знову ліг цілком і не з’явилась прокрутка.
            if (page + 1 < w)
            {
                _syncingPage = true;
                Editor.Document.ClearValue(FlowDocument.PageWidthProperty);
                _syncingPage = false;
                ScheduleFit();
            }
            return;
        }
        if (page + 0.5 < w)
            SyncPageWidth();
    }

    private void SyncPageWidth()
    {
        var w = Editor.ActualWidth;
        if (w < 1 || _syncingPage) return;
        if (!double.IsNaN(Editor.Document.PageWidth) && Math.Abs(Editor.Document.PageWidth - w) < 0.5)
            return;
        _syncingPage = true;
        Editor.Document.PageWidth = w;
        _syncingPage = false;
    }

    private void OnActivated(object sender, EventArgs e) => SetChrome(true);

    private void OnDeactivated(object sender, EventArgs e)
    {
        MenuPopup.IsOpen = false;
        SetChrome(false);
        PersistNow();
    }

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

    private void BodyActivate(object sender, MouseButtonEventArgs e)
    {
        if (!_chrome) Activate();
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (_loading || !IsLoaded) return;
        _note.Left = Left;
        _note.Top = Top;
        PersistSoon();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_loading || _suppressSize || !IsLoaded || WindowState != WindowState.Normal) return;
        if (!StickerShell.IsUsableSize(ActualWidth, ActualHeight)) return;
        _note.Width = ActualWidth;
        _note.Height = Math.Max(MinHeight, ActualHeight - _chromeExtra);
        PersistSoon();
    }

    private void EditorChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        UpdatePlaceholder();
        PersistSoon();
    }

    private void EditorFocus(object sender, KeyboardFocusChangedEventArgs e) => SetChrome(true);

    private void EditorBlur(object sender, KeyboardFocusChangedEventArgs e) { }

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

    private void PersistNow()
    {
        if (_allowClose || _loading || WindowState != WindowState.Normal || !IsVisible) return;
        if (!StickerShell.IsUsableSize(ActualWidth, ActualHeight)) return;
        _note.Left = Left;
        _note.Top = Top;
        _note.Width = ActualWidth;
        _note.Height = Math.Max(MinHeight, ActualHeight - _chromeExtra);
        _note.FontSize = Editor.FontSize;
        _note.Rtf = IsEditorEmpty() ? "" : CaptureBody();
        _onChanged(_note);
    }

    private void OpenMenu(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = true;
    }

    private void CopySticker(object sender, RoutedEventArgs e)
    {
        PersistNow();
        MenuPopup.IsOpen = false;
        _onCopy(_note);
    }

    private void DeleteSticker(object sender, RoutedEventArgs e)
    {
        var question = _note.Role == GuidesSticker.Role
            ? "Ви хочете видалити дуже корисну нотатку! Тут зібрані посилання на керівні документи які потрібні для розуміння процесів та регулюють роботу сержанта з МТЗ."
            : "Видалити цей стікер?";
        var ask = MessageBox.Show(
            this,
            question,
            "Стікери",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (ask != MessageBoxResult.Yes) return;
        // Не закривати стікер у стеку діалога: інакше закривається і головне вікно.
        _allowClose = true;
        _saveTimer?.Stop();
        StickerShell.SafeHide(this);
        Dispatcher.BeginInvoke(() => _onDelete(_note));
    }

    private void CollapseClick(object sender, RoutedEventArgs e) => StickerShell.SafeHide(this);

    private void Place(double left, double top, double width, double height)
    {
        var area = SystemParameters.WorkArea;
        if (width < MinWidth || width > area.Width * 0.92) width = 300;
        if (height < MinHeight || height > area.Height * 0.92) height = 300;
        var screenLeft = SystemParameters.VirtualScreenLeft;
        var screenTop = SystemParameters.VirtualScreenTop;
        var screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
        var screenBottom = screenTop + SystemParameters.VirtualScreenHeight;
        if (left < screenLeft || left > screenRight - 48) left = area.Left + 48;
        if (top < screenTop || top > screenBottom - 48) top = area.Top + 48;
        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    private void FitGuidesOnce(object sender, RoutedEventArgs e)
    {
        Loaded -= FitGuidesOnce;
        if (AppStore.Instance.Data.Settings.GuidesFitted) return;

        const double size = 13;
        _note.FontSize = size;
        Editor.FontSize = size;
        FontSizeLabel.Text = "13";
        TightenGuideList();

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var culture = CultureInfo.GetCultureInfo("uk-UA");
        var family = new FontFamily("Segoe UI");
        var regular = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var bold = new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        double maxW = 0;
        double totalH = 0;
        foreach (var line in GuideLines())
        {
            if (string.IsNullOrWhiteSpace(line.Text)) continue;
            var ft = new FormattedText(
                line.Text,
                culture,
                FlowDirection.LeftToRight,
                line.Bold ? bold : regular,
                size,
                Brushes.Black,
                dpi);
            var w = ft.Width + (line.Bullet ? 22 : 0);
            if (w > maxW) maxW = w;
            totalH += ft.Height + (line.Bullet ? 2 : 4);
        }

        Place(Left, Top, Math.Ceiling(maxW + 32), Math.Ceiling(totalH + 46));
        Dispatcher.BeginInvoke(SaveFittedGuides, DispatcherPriority.ApplicationIdle);
    }

    private void SaveFittedGuides()
    {
        AppStore.Instance.Data.Settings.GuidesFitted = true;
        _note.Width = ActualWidth;
        _note.Height = ActualHeight;
        PersistNow();
    }

    private void TightenGuideList()
    {
        foreach (var block in Editor.Document.Blocks)
        {
            if (block is not List list) continue;
            list.Margin = new Thickness(0);
            list.Padding = new Thickness(0);
            list.MarkerOffset = 12;
            foreach (var item in list.ListItems)
            {
                item.Margin = new Thickness(0, 1, 0, 1);
                item.Padding = new Thickness(0);
                foreach (var child in item.Blocks)
                    child.Margin = new Thickness(0);
            }
        }
    }

    private IEnumerable<(string Text, bool Bold, bool Bullet)> GuideLines()
    {
        foreach (var block in Editor.Document.Blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    yield return (LineText(paragraph), paragraph.FontWeight == FontWeights.Bold || HasBold(paragraph), false);
                    break;
                case List list:
                    foreach (var item in list.ListItems)
                    {
                        foreach (var child in item.Blocks)
                        {
                            if (child is Paragraph paragraph)
                                yield return (LineText(paragraph), false, true);
                        }
                    }
                    break;
            }
        }
    }

    private static bool HasBold(Paragraph paragraph) =>
        paragraph.Inlines.OfType<Run>().Any(r => r.FontWeight == FontWeights.Bold)
        || paragraph.Inlines.OfType<Hyperlink>().SelectMany(h => h.Inlines.OfType<Run>()).Any(r => r.FontWeight == FontWeights.Bold);

    private static string LineText(Paragraph paragraph) =>
        new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text
            .Replace("\r", "").Replace("\n", "").Trim();

    private void FontMinus(object sender, RoutedEventArgs e) => ChangeFont(-1);

    private void FontPlus(object sender, RoutedEventArgs e) => ChangeFont(1);

    private void ChangeFont(int delta)
    {
        var next = Math.Clamp(Editor.FontSize + delta, 11, 36);
        Editor.FontSize = next;
        _note.FontSize = next;
        FontSizeLabel.Text = ((int)Math.Round(next)).ToString();
        PersistSoon();
    }

    private void FmtBold(object sender, RoutedEventArgs e) =>
        EditingCommands.ToggleBold.Execute(null, Editor);

    private void FmtItalic(object sender, RoutedEventArgs e) =>
        EditingCommands.ToggleItalic.Execute(null, Editor);

    private void FmtUnderline(object sender, RoutedEventArgs e) =>
        EditingCommands.ToggleUnderline.Execute(null, Editor);

    private void FmtStrike(object sender, RoutedEventArgs e)
    {
        var sel = Editor.Selection;
        var current = sel.GetPropertyValue(Inline.TextDecorationsProperty);
        if (current == DependencyProperty.UnsetValue || current is not TextDecorationCollection td || td.Count == 0)
            sel.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
        else
            sel.ApplyPropertyValue(Inline.TextDecorationsProperty, null);
        PersistSoon();
    }

    private void FmtList(object sender, RoutedEventArgs e) =>
        EditingCommands.ToggleBullets.Execute(null, Editor);

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            EditLink(this, e);
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private Point _linkPress;
    private bool _linkArmed;

    private void EditorMouseDown(object sender, MouseButtonEventArgs e)
    {
        _linkPress = e.GetPosition(Editor);
        _linkArmed = LinkFromPoint(_linkPress) != null;
    }

    private void EditorMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_linkArmed) return;
        _linkArmed = false;
        var point = e.GetPosition(Editor);
        if (Math.Abs(point.X - _linkPress.X) > 4 || Math.Abs(point.Y - _linkPress.Y) > 4) return;
        var link = LinkFromPoint(point);
        if (link?.NavigateUri == null) return;
        OpenAddress(link.NavigateUri);
    }

    private Hyperlink? LinkFromPoint(Point point)
    {
        var pos = Editor.GetPositionFromPoint(point, true);
        if (pos == null) return null;
        var link = StickerText.HyperlinkOf(pos);
        if (link == null) return null;
        // Найближчий текст знаходиться і в порожньому полі. Відкриваємо лише межі самого напису.
        var bounds = LinkBounds(link);
        if (bounds.IsEmpty || !bounds.Contains(point)) return null;
        return link;
    }

    private static Rect LinkBounds(Hyperlink link)
    {
        var start = link.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        var last = link.ContentEnd.GetNextInsertionPosition(LogicalDirection.Backward);
        var end = last?.GetCharacterRect(LogicalDirection.Backward) ?? Rect.Empty;
        Rect bounds;
        if (start.IsEmpty && end.IsEmpty) return Rect.Empty;
        if (start.IsEmpty) bounds = end;
        else if (end.IsEmpty) bounds = start;
        else bounds = Rect.Union(start, end);
        bounds.Inflate(3, 4);
        return bounds;
    }

    private void EditLink(object sender, RoutedEventArgs e)
    {
        var sel = Editor.Selection;
        Hyperlink? link = null;
        string text;
        string address;
        if (sel.IsEmpty)
        {
            link = StickerText.HyperlinkOf(sel.Start)
                ?? sel.Start?.GetAdjacentElement(LogicalDirection.Backward) as Hyperlink
                ?? sel.Start?.GetAdjacentElement(LogicalDirection.Forward) as Hyperlink;
            text = link == null ? "" : StickerText.Plain(link);
            address = link?.NavigateUri?.AbsoluteUri ?? "";
        }
        else
        {
            var startLink = StickerText.HyperlinkOf(sel.Start);
            var endAt = sel.End?.GetInsertionPosition(LogicalDirection.Backward);
            var endLink = StickerText.HyperlinkOf(endAt);
            if (startLink != null && startLink == endLink)
            {
                link = startLink;
                text = StickerText.Plain(link);
                address = link.NavigateUri?.AbsoluteUri ?? "";
            }
            else
            {
                text = sel.Text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
                address = "";
            }
        }

        // Покажчики лишаються дійсними, навіть якщо діалог зніме виділення.
        var rangeStart = sel.Start;
        var rangeEnd = sel.End;
        var dlg = new LinkEditWindow(text, address, link != null) { Owner = this };
        if (dlg.ShowDialog() != true || rangeStart == null || rangeEnd == null) return;

        if (dlg.Removed)
        {
            if (link != null)
            {
                var plain = StickerText.Plain(link);
                new TextRange(link.ElementStart, link.ElementEnd).Text = plain;
            }
        }
        else if (link != null && dlg.Address != null)
        {
            link.Inlines.Clear();
            link.Inlines.Add(new Run(dlg.LinkText));
            link.NavigateUri = dlg.Address;
            link.ToolTip = dlg.Address.AbsoluteUri;
            link.Foreground = StickerText.LinkBrush;
        }
        else if (dlg.Address != null)
        {
            var span = new TextRange(rangeStart, rangeEnd);
            if (!span.IsEmpty) span.Text = "";
            var pos = span.Start.GetInsertionPosition(LogicalDirection.Forward) ?? span.Start;
            var created = new Hyperlink(new Run(dlg.LinkText), pos)
            {
                NavigateUri = dlg.Address,
                ToolTip = dlg.Address.AbsoluteUri,
                Foreground = StickerText.LinkBrush,
                TextDecorations = TextDecorations.Underline
            };
            _ = created;
        }

        UpdatePlaceholder();
        Editor.Focus();
        PersistSoon();
    }

    private void OpenAddress(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            MessageBox.Show(this, "Не вдалося відкрити посилання.", "Стікери",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
