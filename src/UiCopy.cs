using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Obozny;

/// <summary>
/// Копіювання видимого тексту: Ctrl+C і пункт «Копіювати» правим кліком.
/// Працює на написах і в будь-якій таблиці, не чіпаючи поля вводу.
/// </summary>
internal static class UiCopy
{
    public static void Install()
    {
        EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown));
        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewMouseRightButtonUpEvent, new MouseButtonEventHandler(OnRightClick));
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.ContextMenuOpeningEvent, new ContextMenuEventHandler(OnMenuOpening));
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.C || Keyboard.Modifiers != ModifierKeys.Control) return;
        var readOnly = ReadOnlyBox(e.OriginalSource as DependencyObject)
                       ?? ReadOnlyBox(Keyboard.FocusedElement as DependencyObject);
        if (readOnly != null)
        {
            if (readOnly.SelectionLength > 0) return;
            if (!Useful(readOnly.Text)) return;
            SetClipboard(readOnly.Text);
            e.Handled = true;
            return;
        }
        if (IsTextEditor(e.OriginalSource as DependencyObject) || IsTextEditor(Keyboard.FocusedElement as DependencyObject))
            return;

        var origin = e.OriginalSource as DependencyObject
                     ?? Keyboard.FocusedElement as DependencyObject
                     ?? Mouse.DirectlyOver as DependencyObject;
        var copy = Describe(origin);
        if (copy == null) return;
        SetClipboard(copy.Text);
        e.Handled = true;
    }

    private static void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src) return;
        if (FindAncestor<ContextMenu>(src) != null) return;
        if (FindAncestor<ScrollBar>(src) != null || FindAncestor<Thumb>(src) != null) return;
        if (HasContextMenu(src)) return;

        var readOnly = ReadOnlyBox(src);
        if (readOnly != null)
        {
            var selected = readOnly.SelectionLength > 0 ? readOnly.SelectedText : readOnly.Text;
            if (!Useful(selected)) return;
            var menu = new ContextMenu { FontSize = 15, PlacementTarget = readOnly, Placement = PlacementMode.MousePoint };
            menu.Items.Add(MakeItem("Копіювати", "Ctrl+C", selected));
            var selectAll = new MenuItem { Header = "Виділити все" };
            selectAll.Click += (_, _) =>
            {
                readOnly.Focus();
                readOnly.SelectAll();
            };
            menu.Items.Add(selectAll);
            OpenMenu(menu);
            e.Handled = true;
            return;
        }

        if (IsTextEditor(src)) return;

        var copy = Describe(src);
        if (copy == null) return;

        var gridMenu = new ContextMenu { FontSize = 15, Placement = PlacementMode.MousePoint };
        gridMenu.Items.Add(MakeItem("Копіювати", "Ctrl+C", copy.Text));
        if (!string.IsNullOrWhiteSpace(copy.RowText) && copy.RowText != copy.Text)
            gridMenu.Items.Add(MakeItem("Копіювати рядок", null, copy.RowText));
        OpenMenu(gridMenu);
        e.Handled = true;
    }

    private static void OpenMenu(ContextMenu menu)
    {
        menu.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => menu.IsOpen = true);
    }

    private static void OnMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.ContextMenu == null) return;
        // Службові меню дій (напр. «Виконати дії») — без пунктів «Копіювати».
        if (Equals(fe.ContextMenu.Tag, "no-ui-copy")) return;
        var ours = fe.ContextMenu.Items.OfType<MenuItem>().Any(i => Equals(i.Tag, "ui-copy"));
        if (!ours && MenuHasCopy(fe.ContextMenu)) return;

        var copy = Describe(e.OriginalSource as DependencyObject);
        if (copy == null) return;
        Upsert(fe.ContextMenu, "ui-copy", "Копіювати", "Ctrl+C", copy.Text, 0);
        if (!string.IsNullOrWhiteSpace(copy.RowText) && copy.RowText != copy.Text)
            Upsert(fe.ContextMenu, "ui-copy-row", "Копіювати рядок", null, copy.RowText, 1);
    }

    private static void Upsert(ContextMenu menu, string tag, string header, string? gesture, string text, int index)
    {
        var item = menu.Items.OfType<MenuItem>().FirstOrDefault(i => Equals(i.Tag, tag));
        if (item == null)
        {
            item = MakeItem(header, gesture, text);
            item.Tag = tag;
            menu.Items.Insert(Math.Min(index, menu.Items.Count), item);
            return;
        }
        item.CommandParameter = text;
    }

    private static MenuItem MakeItem(string header, string? gesture, string text)
    {
        var item = new MenuItem
        {
            Header = header,
            InputGestureText = gesture,
            CommandParameter = text
        };
        item.Click += (_, args) =>
        {
            if (args.Source is MenuItem mi && mi.CommandParameter is string s && !string.IsNullOrWhiteSpace(s))
                SetClipboard(s);
        };
        return item;
    }

    private static bool MenuHasCopy(ContextMenu menu) =>
        menu.Items.OfType<MenuItem>().Any(i => i.Header is string h && h.Contains("Копіювати", StringComparison.Ordinal));

    private sealed record CopyPayload(string Text, string? RowText);

    private static CopyPayload? Describe(DependencyObject? origin)
    {
        if (origin == null) return null;

        var cell = FindAncestor<DataGridCell>(origin);
        var row = FindAncestor<DataGridRow>(origin);
        var cellText = cell != null ? Clean(VisualText(cell)) : null;
        var rowText = row != null ? RowLine(row) : null;
        var text = Useful(cellText) ? cellText! : NearestText(origin);
        if (!Useful(text)) return null;
        if (!Useful(rowText) || rowText == text) rowText = null;
        return new CopyPayload(text!, rowText);
    }

    private static string? NearestText(DependencyObject origin)
    {
        for (var d = origin; d != null && d is not Window; d = Parent(d))
        {
            switch (d)
            {
                case TextBlock tb when Useful(tb.Text) && FindAncestor<Button>(tb) == null:
                    return tb.Text.Trim();
                case ContentControl cc when cc.Content is string s && Useful(s) && cc is not Button:
                    return s.Trim();
                case Button btn:
                    var buttonText = btn.Content is string bs ? bs : Clean(VisualText(btn));
                    if (Useful(buttonText) && buttonText!.Trim().Length > 1)
                        return buttonText.Trim();
                    break;
                case ComboBox combo when Useful(combo.Text):
                    return combo.Text.Trim();
            }
        }
        return null;
    }

    private static string? RowLine(DataGridRow row)
    {
        var grid = FindAncestor<DataGrid>(row);
        if (grid == null) return null;
        var parts = new List<string>();
        foreach (var col in grid.Columns.OrderBy(c => c.DisplayIndex))
        {
            if (col.Visibility != Visibility.Visible) continue;
            var content = col.GetCellContent(row);
            var text = content == null ? "" : Clean(VisualText(content));
            if (!Useful(text)) continue;
            parts.Add(text!);
        }
        return parts.Count == 0 ? null : string.Join("\t", parts);
    }

    private static string VisualText(DependencyObject root)
    {
        var sb = new StringBuilder();
        Collect(root, sb);
        return sb.ToString();
    }

    private static void Collect(DependencyObject d, StringBuilder sb)
    {
        switch (d)
        {
            case TextBlock tb when Useful(tb.Text):
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(tb.Text.Trim());
                return;
            case TextBox tb when Useful(tb.Text) && FindAncestor<Button>(tb) == null:
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(tb.Text.Trim());
                return;
        }

        var count = VisualTreeHelper.GetChildrenCount(d);
        for (var i = 0; i < count; i++)
            Collect(VisualTreeHelper.GetChild(d, i), sb);
    }

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var sb = new StringBuilder();
        var space = false;
        foreach (var ch in text.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                space = sb.Length > 0;
                continue;
            }
            if (space)
            {
                sb.Append(' ');
                space = false;
            }
            sb.Append(ch);
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    private static bool Useful(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (t.Length == 1 && !char.IsLetterOrDigit(t[0])) return false;
        return true;
    }

    private static TextBox? ReadOnlyBox(DependencyObject? d)
    {
        for (; d != null && d is not Window; d = Parent(d))
        {
            if (d is not TextBox box) continue;
            return box.IsReadOnly ? box : null;
        }
        return null;
    }

    private static bool IsTextEditor(DependencyObject? d)
    {
        for (; d != null && d is not Window; d = Parent(d))
        {
            if (d is TextBox or RichTextBox or PasswordBox)
                return true;
        }
        return false;
    }

    private static bool HasContextMenu(DependencyObject start)
    {
        for (var d = start; d != null && d is not Window; d = Parent(d))
        {
            if (d is FrameworkElement fe && fe.ContextMenu != null)
                return true;
        }
        return false;
    }

    private static DependencyObject? Parent(DependencyObject d)
    {
        if (d is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            var visual = VisualTreeHelper.GetParent(d);
            if (visual != null) return visual;
        }
        return LogicalTreeHelper.GetParent(d);
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        for (; d != null; d = Parent(d))
        {
            if (d is T found) return found;
        }
        return null;
    }

    private static void SetClipboard(string text)
    {
        try
        {
            Clipboard.SetDataObject(text);
        }
        catch
        {
            // буфер зайнятий іншою програмою
        }
    }
}
