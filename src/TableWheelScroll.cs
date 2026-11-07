using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Obozny;

/// <summary>
/// Колесо/жест над таблицею не «залипає» в комірці редагування:
/// скролить саму таблицю покроково, а якщо вона вже на краю — батьківський ScrollViewer.
/// Дельта нормалізується, щоб жест/тачпад не стрибав одразу в кінець списку.
/// Важливо: при CanContentScroll=true VerticalOffset у «рядках», не в DIP —
/// піксельна дельта там стрибає в кінець; для item-режиму рахуємо в рядках.
/// </summary>
internal static class TableWheelScroll
{
    private static bool _installed;

    /// <summary>Висота «рядка» для порції скролу (близько до RowHeight таблиць).</summary>
    private const double LineHeight = 44;

    /// <summary>Максимум «щілин» коліщатка за одну подію — захист від стрибка в кінець.</summary>
    private const double MaxNotchesPerEvent = 3;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnPreviewMouseWheel),
            handledEventsToo: true);
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta == 0 || sender is not DataGrid grid) return;

        var inner = FindChildScrollViewer(grid);
        if (inner != null)
        {
            var delta = ComputeVerticalScrollDelta(e.Delta, inner.CanContentScroll);
            if (delta != 0 && CanScrollVertically(inner, delta))
            {
                inner.ScrollToVerticalOffset(ClampOffset(inner, inner.VerticalOffset + delta));
                e.Handled = true;
                return;
            }
        }

        // Батьківський ScrollViewer (не внутрішній шаблону DataGrid).
        var outer = FindAncestorScrollViewer(VisualTreeHelper.GetParent(grid));
        if (outer != null)
        {
            var delta = ComputeVerticalScrollDelta(e.Delta, outer.CanContentScroll);
            if (delta != 0)
            {
                outer.ScrollToVerticalOffset(ClampOffset(outer, outer.VerticalOffset + delta));
                e.Handled = true;
                return;
            }
        }

        // Немає зовнішнього — примусово скролимо таблицю (TextBox у комірці не з’їдає жест).
        if (inner == null) return;
        var forced = ComputeVerticalScrollDelta(e.Delta, inner.CanContentScroll);
        if (forced == 0) return;
        inner.ScrollToVerticalOffset(ClampOffset(inner, inner.VerticalOffset + forced));
        e.Handled = true;
    }

    /// <summary>
    /// Зміна VerticalOffset за одну подію колеса/жесту.
    /// itemBasedScrolling=true → одиниці = рядки (CanContentScroll); false → DIP/пікселі.
    /// </summary>
    internal static double ComputeVerticalScrollDelta(int mouseDelta, bool itemBasedScrolling)
    {
        var lines = SystemParameters.WheelScrollLines;
        if (lines == 0) return 0;
        // -1 у системі = «сторінка»; для таблиць тримаємо помірну порцію.
        if (lines < 0) lines = 3;

        var notches = mouseDelta / 120.0;
        if (notches > MaxNotchesPerEvent) notches = MaxNotchesPerEvent;
        else if (notches < -MaxNotchesPerEvent) notches = -MaxNotchesPerEvent;

        // Додатний mouseDelta = вгору = зменшення offset.
        var signed = -notches * lines;
        return itemBasedScrolling ? signed : signed * LineHeight;
    }

    private static bool CanScrollVertically(ScrollViewer sv, double offsetDelta)
    {
        if (offsetDelta < 0)
            return sv.VerticalOffset > 0.5;
        return sv.VerticalOffset + sv.ViewportHeight < sv.ExtentHeight - 0.5;
    }

    private static double ClampOffset(ScrollViewer sv, double offset)
    {
        var max = Math.Max(0, sv.ExtentHeight - sv.ViewportHeight);
        if (offset < 0) return 0;
        if (offset > max) return max;
        return offset;
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject? start)
    {
        for (var d = start; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is ScrollViewer sv) return sv;
        }
        return null;
    }

    private static ScrollViewer? FindChildScrollViewer(DependencyObject root)
    {
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            var nested = FindChildScrollViewer(child);
            if (nested != null) return nested;
        }
        return null;
    }
}
