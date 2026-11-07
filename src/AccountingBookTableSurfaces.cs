using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Obozny;

/// <summary>
/// Геометрія колонок Книги (додаток 14). Спільна для sticky-шапки й тіла.
/// QtyColW — фіксована MinQtyColW (без розтягу на широкому вікні);
/// вузьку таблицю центрують через ContentLeftPad. Sticky і тіло — одне QtyColW.
/// </summary>
internal static class AccountingBookLayout
{
    public const double MetaColW = 78;
    public const double DocColW = 132;
    public const double NumColW = 92;
    public const double PartyColW = 128;
    public const double MinQtyColW = 72;
    public const double HeaderMidRowH = 26;
    public const double NameRowH = 64;
    public const double MoveRowH = 28;
    public const double DataRowH = 56;
    public const int MetaCols = 4;
    public const int MidHeaderRows = 6;

    /// <summary>Поточна ширина колонки кількості (надійшло/вибуло/становить).</summary>
    public static double QtyColW { get; private set; } = MinQtyColW;

    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("uk-UA");

    public static double MetaBlockWidth => MetaColW + DocColW + NumColW + PartyColW;

    public static double ContentWidth(int columnCount) =>
        MetaBlockWidth + columnCount * 3 * QtyColW;

    public static double MinContentWidth(int columnCount) =>
        MetaBlockWidth + columnCount * 3 * MinQtyColW;

    /// <summary>Середній блок повної шапки (код / О.В. / ціна) — у тілі, під назвою.</summary>
    public static double MidBlockH => MidHeaderRows * HeaderMidRowH;

    /// <summary>Y трійки в тілі (після mid). У повній шапці трійка знизу, не поруч із назвою.</summary>
    public static double BodyTripletTop => MidBlockH;

    /// <summary>Y першого рядка даних у тілі.</summary>
    public static double BodyDataTop => MidBlockH + MoveRowH;

    public static double StickySurfaceHeight(bool tripletPinned) =>
        tripletPinned ? NameRowH + MoveRowH : NameRowH;

    /// <summary>Нижня межа 4 лівих sticky-заголовків — лише коли трійка закріплена (V-скрол).</summary>
    public static bool StickyMetaDrawsBottomBorder(bool tripletPinned) => tripletPinned;

    /// <summary>
    /// Трійку в sticky показуємо, коли mid повністю пішов з в’юпорта.
    /// Після pin offset компенсується на MoveRowH (тіло-трійка ховається під/над в’юпортом).
    /// </summary>
    public static bool ShouldPinTriplet(double verticalOffset, bool currentlyPinned)
    {
        if (currentlyPinned)
            return verticalOffset >= MidBlockH + MoveRowH - 0.5;
        return verticalOffset >= MidBlockH - 0.5;
    }

    public static double BodyHeight(int rowCount) =>
        BodyDataTop + rowCount * DataRowH;

    /// <summary>
    /// Ширина qty-колонки завжди MinQtyColW — без розтягу на широкому вікні.
    /// Параметри лишаються для спільного виклику sticky/тіло; viewportWidth
    /// використовує <see cref="ContentLeftPad"/>.
    /// </summary>
    public static void UpdateQtyColWidth(int propertyColumnCount, double viewportWidth)
    {
        _ = propertyColumnCount;
        _ = viewportWidth;
        QtyColW = MinQtyColW;
    }

    /// <summary>
    /// Лівий відступ, щоб таблиця була по центру, коли контент вужчий за в’юпорт.
    /// Якщо потрібен H-скрол (контент ≥ в’юпорт) — 0.
    /// Важно: viewportWidth = ScrollViewer.ViewportWidth (без вертикального бігунка),
    /// не ActualWidth — інакше sticky/тіло роз’їдуться на ~sb/2 на широкому вікні.
    /// </summary>
    public static double ContentLeftPad(int columnCount, double viewportWidth)
    {
        if (columnCount < 0 || viewportWidth <= 0) return 0;
        var content = ContentWidth(columnCount);
        if (content >= viewportWidth) return 0;
        return Math.Floor((viewportWidth - content) / 2);
    }

    /// <summary>
    /// Ширина «жолоба» бігунка: ActualWidth − ViewportWidth (0, якщо бігунка немає).
    /// Sticky-хост має бути шириною в’юпорта, не ActualWidth SV.
    /// </summary>
    public static double ScrollBarGutter(double actualWidth, double viewportWidth)
    {
        if (viewportWidth <= 0 || actualWidth <= viewportWidth) return 0;
        return actualWidth - viewportWidth;
    }

    /// <summary>Canvas.Left для sticky: pad центру мінус H-зсув тіла.</summary>
    public static double StickyCanvasLeft(double contentLeftPad, double horizontalOffset) =>
        contentLeftPad - Math.Max(0, horizontalOffset);

    /// <summary>
    /// Чи межа qty-колонки є межею групи майна (після «становить» / перед «надійшло»).
    /// Внутрішні межі трійки в mid-блоці не малюємо — mid = одна клітинка на 3 колонки.
    /// </summary>
    public static bool IsPropertyGroupBoundary(int colIndex)
    {
        if (colIndex <= MetaCols) return true;
        return (colIndex - MetaCols) % 3 == 0;
    }

    public static double ColLeft(int colIndex)
    {
        return colIndex switch
        {
            0 => 0,
            1 => MetaColW,
            2 => MetaColW + DocColW,
            3 => MetaColW + DocColW + NumColW,
            _ => MetaBlockWidth + (colIndex - MetaCols) * QtyColW
        };
    }

    public static double ColWidth(int colIndex) =>
        colIndex switch
        {
            0 => MetaColW,
            1 => DocColW,
            2 => NumColW,
            3 => PartyColW,
            _ => QtyColW
        };

    /// <summary>Лівий край групи з 3 qty-колонок для i-ї позиції майна.</summary>
    public static double PropertyGroupLeft(int propertyIndex) =>
        MetaBlockWidth + propertyIndex * 3 * QtyColW;

    /// <summary>Ширина групи надійшло/вибуло/становить (= ширина назви над нею).</summary>
    public static double PropertyGroupWidth => 3 * QtyColW;

    /// <summary>
    /// X у координатах контенту для H-freeze мета-колонки:
    /// при Canvas.Left/ScrollOffset = −hOffset візуально лишається на ColLeft.
    /// </summary>
    public static double FrozenMetaContentX(int colIndex, double hOffset) =>
        ColLeft(colIndex) + hOffset;

    public static int TotalColumns(int propertyColumns) => MetaCols + propertyColumns * 3;

    /// <summary>Прочерк «—» як у решті UI Обозного, коли в рядку руху не було.</summary>
    public const string QtyDash = "—";

    public static string QtyText(decimal? q) =>
        q is decimal d ? Ua.FormatQty(d) : QtyDash;

    public static Typeface UiTypeface { get; } = new("Segoe UI");

    public static FormattedText MakeText(
        string text, double size, Brush brush, double pixelsPerDip,
        FontWeight? weight = null, TextAlignment align = TextAlignment.Left, double? maxWidth = null)
    {
        var ft = new FormattedText(
            text ?? "",
            Uk,
            FlowDirection.LeftToRight,
            weight is FontWeight w ? new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, w, FontStretches.Normal) : UiTypeface,
            size,
            brush,
            pixelsPerDip);
        if (maxWidth is double mw && mw > 0)
            ft.MaxTextWidth = mw;
        ft.Trimming = TextTrimming.CharacterEllipsis;
        ft.TextAlignment = align;
        return ft;
    }
}

/// <summary>
/// Закріплена шапка: найменування позицій + надійшло/вибуло/становить.
/// H-зсув через Canvas.Left (поза ScrollViewer). Ліві 4 мета-колонки — H-freeze:
/// малюються на ContentX = ColLeft + hOffset (після Canvas.Left лишаються зліва).
/// Без H-cull і без layout-clip по слоту. Ширина = ContentWidth. Текст — PushClip.
/// </summary>
internal sealed class AccountingBookStickySurface : FrameworkElement
{
    private AccountingBookSection? _section;
    private double _hOffset;
    private bool _showTriplet;

    public AccountingBookStickySurface()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Clip = null;
    }

    public void Bind(AccountingBookSection section, bool showTriplet)
    {
        _section = section;
        _showTriplet = showTriplet;
        Width = AccountingBookLayout.ContentWidth(section.Columns.Count);
        Height = AccountingBookLayout.StickySurfaceHeight(showTriplet);
        Clip = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SetShowTriplet(bool showTriplet)
    {
        if (_showTriplet == showTriplet) return;
        _showTriplet = showTriplet;
        Height = AccountingBookLayout.StickySurfaceHeight(showTriplet);
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Горизонтальний зсув тіла — щоб мета-колонки лишались на місці.</summary>
    public void SetHorizontalOffset(double hOffset)
    {
        if (Math.Abs(_hOffset - hOffset) < 0.01) return;
        _hOffset = Math.Max(0, hOffset);
        InvalidateVisual();
    }

    public void Clear()
    {
        _section = null;
        _hOffset = 0;
        Width = 0;
        Height = 0;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsNaN(Width) ? 0 : Width;
        var h = double.IsNaN(Height) ? 0 : Height;
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var w = double.IsNaN(Width) ? finalSize.Width : Width;
        var h = double.IsNaN(Height) ? finalSize.Height : Height;
        return new Size(w, h);
    }

    /// <summary>
    /// Не кліпати до layout-slot до зсуву — інакше права частина шапки (найменування)
    /// не потрапляє в кадр. Візуальний кліп — StickyClip (ClipToBounds).
    /// </summary>
    protected override Geometry? GetLayoutClip(Size layoutSlotSize) => null;

    protected override void OnRender(DrawingContext dc)
    {
        if (_section == null || _section.IsEmpty) return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var bg = BrushOr(TryFindResource("CardBg") as Brush, Brushes.White);
        var line = BrushOr(TryFindResource("GridLine") as Brush, Brushes.Gray);
        var text = BrushOr(TryFindResource("TextPrimary") as Brush, Brushes.Black);
        var pen = new Pen(line, 0.5);
        if (pen.CanFreeze) pen.Freeze();

        var cols = _section.Columns;
        var contentW = AccountingBookLayout.ContentWidth(cols.Count);
        var bottomBorder = AccountingBookLayout.StickyMetaDrawsBottomBorder(_showTriplet);

        dc.DrawRectangle(bg, null, new Rect(0, 0, contentW, Height));

        // Qty спочатку; мета зверху — H-freeze накриває qty, що під’їхали під лівий блок.
        for (var i = 0; i < cols.Count; i++)
        {
            var baseCol = AccountingBookLayout.MetaCols + i * 3;
            var groupX = AccountingBookLayout.PropertyGroupLeft(i);
            var groupW = AccountingBookLayout.PropertyGroupWidth;
            DrawNameCell(dc, cols[i].Key.DisplayTitle, groupX, groupW, text, pen, dpi, bg);
            if (!_showTriplet) continue;
            DrawHeaderCell(dc, "надійшло", 1, baseCol, 1, AccountingBookLayout.MoveRowH,
                text, pen, dpi, bg, fontSize: 12);
            DrawHeaderCell(dc, "вибуло", 1, baseCol + 1, 1, AccountingBookLayout.MoveRowH,
                text, pen, dpi, bg, fontSize: 12);
            DrawHeaderCell(dc, "становить", 1, baseCol + 2, 1, AccountingBookLayout.MoveRowH,
                text, pen, dpi, bg, fontSize: 12);
        }

        dc.DrawLine(pen,
            new Point(AccountingBookLayout.MetaBlockWidth, AccountingBookLayout.NameRowH),
            new Point(contentW, AccountingBookLayout.NameRowH));
        if (_showTriplet)
        {
            dc.DrawLine(pen,
                new Point(AccountingBookLayout.MetaBlockWidth, Height),
                new Point(contentW, Height));
        }

        DrawMetaHeader(dc, "Дата запису", 0, text, pen, dpi, bg, bottomBorder);
        DrawMetaHeader(dc, "Найменування документа", 1, text, pen, dpi, bg, bottomBorder);
        DrawMetaHeader(dc, "Номер і дата документа", 2, text, pen, dpi, bg, bottomBorder);
        DrawMetaHeader(dc, "Постачальник (одержувач)", 3, text, pen, dpi, bg, bottomBorder);
    }

    private void DrawMetaHeader(DrawingContext dc, string label, int col,
        Brush text, Pen pen, double dpi, Brush bg, bool bottomBorder)
    {
        var x = AccountingBookLayout.FrozenMetaContentX(col, _hOffset);
        var w = AccountingBookLayout.ColWidth(col);
        var h = Height;
        var cell = new Rect(x, 0, w, h);
        dc.DrawRectangle(bg, null, cell);
        dc.DrawLine(pen, new Point(x, 0), new Point(x + w, 0));
        dc.DrawLine(pen, new Point(x, 0), new Point(x, h));
        dc.DrawLine(pen, new Point(x + w, 0), new Point(x + w, h));
        if (bottomBorder)
            dc.DrawLine(pen, new Point(x, h), new Point(x + w, h));
        DrawClippedText(dc, label, cell, 12, text, dpi, FontWeights.Normal,
            TextAlignment.Center, maxHeight: h - 4);
    }

    private static void DrawNameCell(
        DrawingContext dc, string label, double x, double w,
        Brush text, Pen pen, double dpi, Brush bg)
    {
        var rowH = AccountingBookLayout.NameRowH;
        var cell = new Rect(x, 0, w, rowH);
        dc.DrawRectangle(bg, pen, cell);
        DrawClippedText(dc, label, cell, 12, text, dpi, FontWeights.SemiBold,
            TextAlignment.Center, maxHeight: rowH - 4);
    }

    private static void DrawHeaderCell(
        DrawingContext dc, string label, int row, int col, int colSpan, double rowH,
        Brush text, Pen pen, double dpi, Brush bg, bool bold = false, double fontSize = 12)
    {
        var x = AccountingBookLayout.ColLeft(col);
        var w = 0.0;
        for (var i = 0; i < colSpan; i++)
            w += AccountingBookLayout.ColWidth(col + i);
        var y = row == 0 ? 0 : AccountingBookLayout.NameRowH;
        var cell = new Rect(x, y, w, rowH);
        dc.DrawRectangle(bg, pen, cell);
        DrawClippedText(dc, label, cell, fontSize, text, dpi,
            bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextAlignment.Center, maxHeight: rowH - 4);
    }

    /// <summary>
    /// Текст строго в межах клітинки (довгі слова не вилазять у сусідні назви).
    /// Origin = лівий край MaxTextWidth-боксу — коректно для Center/Left.
    /// </summary>
    private static void DrawClippedText(
        DrawingContext dc, string label, Rect cell, double fontSize, Brush text, double dpi,
        FontWeight weight, TextAlignment align, double maxHeight)
    {
        var pad = 4.0;
        var maxW = Math.Max(8, cell.Width - pad * 2);
        var ft = AccountingBookLayout.MakeText(label, fontSize, text, dpi,
            weight: weight, align: align, maxWidth: maxW);
        if (ft.Height > maxHeight)
            ft.MaxTextHeight = Math.Max(8, maxHeight);
        var ty = cell.Y + Math.Max(2, (cell.Height - Math.Min(ft.Height, maxHeight)) / 2);
        dc.PushClip(new RectangleGeometry(cell));
        dc.DrawText(ft, new Point(cell.X + pad, ty));
        dc.Pop();
    }

    private static Brush BrushOr(Brush? b, Brush fallback) => b ?? fallback;
}

/// <summary>
/// Тіло розділу: код/О.В./ціна + рядки руху. Один FrameworkElement замість десятків тисяч TextBox.
/// Та сама QtyColW / PropertyGroup*, що в sticky. Ліві 4 колонки — H-freeze (малюються
/// на ColLeft + hOffset). Mid+трійка під мета — одна об’єднана клітинка на колонку
/// (без сітки порожніх). Повна шапка в документі: mid, потім трійка, потім дані.
/// </summary>
internal sealed class AccountingBookBodySurface : FrameworkElement
{
    private AccountingBookSection? _section;
    private ScrollViewer? _vScroll;
    private ScrollViewer? _hScroll;

    public AccountingBookBodySurface()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    public void Bind(AccountingBookSection section, ScrollViewer vScroll, ScrollViewer hScroll)
    {
        _section = section;
        _vScroll = vScroll;
        _hScroll = hScroll;
        Width = AccountingBookLayout.ContentWidth(section.Columns.Count);
        Height = AccountingBookLayout.BodyHeight(section.Rows.Count);
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void Clear()
    {
        _section = null;
        _vScroll = null;
        _hScroll = null;
        Width = 0;
        Height = 0;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsNaN(Width) ? 0 : Width;
        var h = double.IsNaN(Height) ? 0 : Height;
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var w = double.IsNaN(Width) ? finalSize.Width : Width;
        var h = double.IsNaN(Height) ? finalSize.Height : Height;
        return new Size(w, h);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_section == null || _section.IsEmpty) return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var bg = BrushOr(TryFindResource("CardBg") as Brush, Brushes.White);
        var line = BrushOr(TryFindResource("GridLine") as Brush, Brushes.Gray);
        var text = BrushOr(TryFindResource("TextPrimary") as Brush, Brushes.Black);
        var muted = BrushOr(TryFindResource("Muted") as Brush, Brushes.Gray);
        var pen = new Pen(line, 0.5);
        if (pen.CanFreeze) pen.Freeze();

        var cols = _section.Columns;
        var rows = _section.Rows;
        var totalCols = AccountingBookLayout.TotalColumns(cols.Count);
        var midH = AccountingBookLayout.MidBlockH;
        var tripletTop = AccountingBookLayout.BodyTripletTop;
        var dataTop = AccountingBookLayout.BodyDataTop;

        var y0 = _vScroll?.VerticalOffset ?? 0;
        var y1 = y0 + (_vScroll?.ViewportHeight > 0 ? _vScroll.ViewportHeight : ActualHeight);
        var hOff = _hScroll?.HorizontalOffset ?? 0;
        var x0 = hOff;
        var x1 = hOff + (_hScroll?.ViewportWidth > 0 ? _hScroll.ViewportWidth : ActualWidth);

        // Cull лише qty; мета завжди малюється окремо з H-freeze.
        var firstCol = Math.Max(AccountingBookLayout.MetaCols, FirstVisibleCol(totalCols, x0));
        var lastCol = LastVisibleCol(totalCols, x1);

        // Фон видимої смуги (повна ширина контенту — H-скрол кліпає).
        dc.DrawRectangle(bg, null, new Rect(0, y0, Width, Math.Max(0, y1 - y0)));

        // Блок код / О.В. / ціна (лише qty; мета — у DrawFrozenMetaStrip).
        if (y0 < midH && y1 > 0)
            DrawMidHeader(dc, cols, firstCol, lastCol, text, muted, pen, dpi, bg);

        if (y0 < dataTop && y1 > tripletTop)
            DrawBodyTriplet(dc, cols, firstCol, lastCol, text, pen, dpi, bg);

        // Рядки даних (лише qty).
        var firstRow = Math.Max(0, (int)Math.Floor((y0 - dataTop) / AccountingBookLayout.DataRowH) - 1);
        var lastRow = Math.Min(rows.Count - 1,
            (int)Math.Ceiling((y1 - dataTop) / AccountingBookLayout.DataRowH) + 1);
        for (var r = firstRow; r <= lastRow && r >= 0; r++)
            DrawDataRow(dc, rows[r], r, cols, firstCol, lastCol, text, pen, dpi, bg);

        // Вертикалі qty: у mid — лише межі груп (span 3); з трійки вниз — усі колонки.
        for (var c = firstCol; c <= lastCol + 1 && c <= totalCols; c++)
        {
            var x = c >= totalCols
                ? AccountingBookLayout.ColLeft(totalCols - 1) + AccountingBookLayout.ColWidth(totalCols - 1)
                : AccountingBookLayout.ColLeft(c);
            var lineTop = AccountingBookLayout.IsPropertyGroupBoundary(c)
                ? y0
                : Math.Max(y0, tripletTop);
            if (lineTop < y1)
                dc.DrawLine(pen, new Point(x, lineTop), new Point(x, y1));
        }

        // H-freeze мета зверху (накриває qty під лівим блоком) + її вертикалі.
        DrawFrozenMetaStrip(dc, y0, y1, hOff, dataTop, rows, firstRow, lastRow, text, pen, dpi, bg);
    }

    private void DrawMidHeader(
        DrawingContext dc, IReadOnlyList<AccountingBookColumn> cols,
        int firstCol, int lastCol, Brush text, Brush muted, Pen pen, double dpi, Brush bg)
    {
        for (var i = 0; i < cols.Count; i++)
        {
            var baseCol = AccountingBookLayout.MetaCols + i * 3;
            if (baseCol + 2 < firstCol || baseCol > lastCol) continue;
            var c = cols[i];
            DrawMidLabel(dc, 0, baseCol, "Код номенклатури", muted, pen, dpi, bg, mutedSize: true);
            DrawMidLabel(dc, 1, baseCol, "", text, pen, dpi, bg);
            DrawMidLabel(dc, 2, baseCol, "Одиниця виміру", muted, pen, dpi, bg, mutedSize: true);
            DrawMidLabel(dc, 3, baseCol, c.Unit, text, pen, dpi, bg);
            DrawMidLabel(dc, 4, baseCol, "Ціна за одиницю", muted, pen, dpi, bg, mutedSize: true);
            DrawMidLabel(dc, 5, baseCol, Ua.FormatPrice(c.Key.UnitPrice), text, pen, dpi, bg);
        }
    }

    private static void DrawBodyTriplet(
        DrawingContext dc, IReadOnlyList<AccountingBookColumn> cols,
        int firstCol, int lastCol, Brush text, Pen pen, double dpi, Brush bg)
    {
        var y = AccountingBookLayout.BodyTripletTop;
        var h = AccountingBookLayout.MoveRowH;
        for (var i = 0; i < cols.Count; i++)
        {
            var baseCol = AccountingBookLayout.MetaCols + i * 3;
            if (baseCol + 2 < firstCol || baseCol > lastCol) continue;
            DrawBodyTripletCell(dc, "надійшло", baseCol, y, h, text, pen, dpi, bg);
            DrawBodyTripletCell(dc, "вибуло", baseCol + 1, y, h, text, pen, dpi, bg);
            DrawBodyTripletCell(dc, "становить", baseCol + 2, y, h, text, pen, dpi, bg);
        }
    }

    private static void DrawBodyTripletCell(
        DrawingContext dc, string label, int col, double y, double h,
        Brush text, Pen pen, double dpi, Brush bg)
    {
        var x = AccountingBookLayout.ColLeft(col);
        var w = AccountingBookLayout.ColWidth(col);
        dc.DrawRectangle(bg, pen, new Rect(x, y, w, h));
        var ft = AccountingBookLayout.MakeText(label, 12, text, dpi,
            align: TextAlignment.Center, maxWidth: Math.Max(8, w - 8));
        var ty = y + Math.Max(1, (h - ft.Height) / 2);
        dc.DrawText(ft, new Point(x + 4, ty));
    }

    private void DrawFrozenMetaStrip(
        DrawingContext dc, double y0, double y1, double hOff, double midH,
        IReadOnlyList<AccountingBookRow> rows, int firstRow, int lastRow,
        Brush text, Pen pen, double dpi, Brush bg)
    {
        // Непрозорий фон лівого блоку на видимій смузі (під ним можуть бути qty).
        dc.DrawRectangle(bg, null,
            new Rect(hOff, y0, AccountingBookLayout.MetaBlockWidth, Math.Max(0, y1 - y0)));

        // Mid+трійка: одна клітинка на колонку, без верхньої лінії (зливається зі sticky-назвою).
        if (y0 < midH && y1 > 0)
        {
            for (var col = 0; col < AccountingBookLayout.MetaCols; col++)
            {
                var x = AccountingBookLayout.FrozenMetaContentX(col, hOff);
                var w = AccountingBookLayout.ColWidth(col);
                dc.DrawRectangle(bg, null, new Rect(x, 0, w, midH));
                dc.DrawLine(pen, new Point(x, 0), new Point(x, midH));
                dc.DrawLine(pen, new Point(x + w, 0), new Point(x + w, midH));
                dc.DrawLine(pen, new Point(x, midH), new Point(x + w, midH));
            }
        }

        for (var r = firstRow; r <= lastRow && r >= 0; r++)
            DrawFrozenMetaDataCells(dc, rows[r], r, hOff, midH, text, pen, dpi, bg);

        for (var c = 0; c <= AccountingBookLayout.MetaCols; c++)
        {
            var x = c >= AccountingBookLayout.MetaCols
                ? hOff + AccountingBookLayout.MetaBlockWidth
                : AccountingBookLayout.FrozenMetaContentX(c, hOff);
            dc.DrawLine(pen, new Point(x, y0), new Point(x, y1));
        }
    }

    private static void DrawFrozenMetaDataCells(
        DrawingContext dc, AccountingBookRow row, int rowIndex, double hOff, double midH,
        Brush text, Pen pen, double dpi, Brush bg)
    {
        var y = midH + rowIndex * AccountingBookLayout.DataRowH;
        var h = AccountingBookLayout.DataRowH;

        void Cell(string value, int col, bool wrap = false)
        {
            var x = AccountingBookLayout.FrozenMetaContentX(col, hOff);
            var w = AccountingBookLayout.ColWidth(col);
            dc.DrawRectangle(bg, pen, new Rect(x, y, w, h));
            if (string.IsNullOrEmpty(value)) return;
            var pad = 6.0;
            var maxW = Math.Max(8, w - pad * 2);
            var ft = AccountingBookLayout.MakeText(value, 13, text, dpi,
                align: TextAlignment.Left, maxWidth: maxW);
            if (wrap || ft.Height > h - 8)
                ft.MaxTextHeight = Math.Max(8, h - 8);
            var ty = y + Math.Max(4, (h - Math.Min(ft.Height, h - 8)) / 2);
            dc.DrawText(ft, new Point(x + pad, ty));
        }

        Cell(row.RecordDate.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("uk-UA")), 0);
        Cell(row.DocumentTitle, 1, wrap: true);
        Cell(row.NumberAndDate, 2, wrap: true);
        Cell(row.Counterparty, 3, wrap: true);
    }

    private static void DrawMidLabel(
        DrawingContext dc, int row, int baseCol, string label,
        Brush brush, Pen pen, double dpi, Brush bg, bool mutedSize = false)
    {
        var y = row * AccountingBookLayout.HeaderMidRowH;
        var propIndex = (baseCol - AccountingBookLayout.MetaCols) / 3;
        var x = AccountingBookLayout.PropertyGroupLeft(propIndex);
        var w = AccountingBookLayout.PropertyGroupWidth;
        dc.DrawRectangle(bg, pen, new Rect(x, y, w, AccountingBookLayout.HeaderMidRowH));
        if (string.IsNullOrEmpty(label)) return;
        var ft = AccountingBookLayout.MakeText(label, mutedSize ? 11 : 12, brush, dpi,
            align: TextAlignment.Center, maxWidth: Math.Max(8, w - 8));
        var ty = y + Math.Max(1, (AccountingBookLayout.HeaderMidRowH - ft.Height) / 2);
        dc.DrawText(ft, new Point(x + 4, ty));
    }

    private void DrawDataRow(
        DrawingContext dc, AccountingBookRow row, int rowIndex,
        IReadOnlyList<AccountingBookColumn> cols, int firstCol, int lastCol,
        Brush text, Pen pen, double dpi, Brush bg)
    {
        // Мета-колонки малює DrawFrozenMetaStrip (H-freeze); тут лише qty.
        var y = AccountingBookLayout.BodyDataTop + rowIndex * AccountingBookLayout.DataRowH;
        var h = AccountingBookLayout.DataRowH;

        void Cell(string value, int col, bool right = false, bool center = false)
        {
            if (col < firstCol || col > lastCol) return;
            var x = AccountingBookLayout.ColLeft(col);
            var w = AccountingBookLayout.ColWidth(col);
            dc.DrawRectangle(bg, pen, new Rect(x, y, w, h));
            if (string.IsNullOrEmpty(value)) return;
            var pad = center ? 4 : 6;
            var maxW = Math.Max(8, w - pad * 2);
            var align = center ? TextAlignment.Center
                : right ? TextAlignment.Right
                : TextAlignment.Left;
            var ft = AccountingBookLayout.MakeText(value, 13, text, dpi,
                align: align, maxWidth: maxW);
            if (ft.Height > h - 8)
                ft.MaxTextHeight = Math.Max(8, h - 8);
            var ty = y + Math.Max(4, (h - Math.Min(ft.Height, h - 8)) / 2);
            dc.DrawText(ft, new Point(x + pad, ty));
        }

        void QtyCell(string value, int col)
        {
            if (value == AccountingBookLayout.QtyDash)
                Cell(value, col, center: true);
            else
                Cell(value, col, right: true);
        }

        for (var i = 0; i < cols.Count; i++)
        {
            var baseCol = AccountingBookLayout.MetaCols + i * 3;
            if (baseCol + 2 < firstCol || baseCol > lastCol) continue;
            if (!row.Cells.TryGetValue(cols[i].Key, out var cell))
            {
                // Захист: модель має заповнювати всі колонки; інакше — прочерк + 0.
                QtyCell(AccountingBookLayout.QtyDash, baseCol);
                QtyCell(AccountingBookLayout.QtyDash, baseCol + 1);
                Cell(Ua.FormatQty(0m), baseCol + 2, right: true);
                continue;
            }
            QtyCell(AccountingBookLayout.QtyText(cell.InQty), baseCol);
            QtyCell(AccountingBookLayout.QtyText(cell.OutQty), baseCol + 1);
            Cell(Ua.FormatQty(cell.Balance), baseCol + 2, right: true);
        }
    }

    private static int FirstVisibleCol(int totalCols, double x0)
    {
        for (var c = 0; c < totalCols; c++)
        {
            var right = AccountingBookLayout.ColLeft(c) + AccountingBookLayout.ColWidth(c);
            if (right >= x0 - 2) return c;
        }
        return Math.Max(0, totalCols - 1);
    }

    private static int LastVisibleCol(int totalCols, double x1)
    {
        for (var c = totalCols - 1; c >= 0; c--)
        {
            if (AccountingBookLayout.ColLeft(c) <= x1 + 2) return c;
        }
        return 0;
    }

    private static Brush BrushOr(Brush? b, Brush fallback) => b ?? fallback;
}
