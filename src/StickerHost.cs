using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Obozny;

/// <summary>Керує вікнами стікерів поверх головного вікна.</summary>
internal sealed class StickerHost
{
    private readonly Window _owner;
    private readonly Dictionary<Guid, StickerWindow> _windows = new();
    private StockSummaryStickerWindow? _summary;
    private bool _visible = true;
    private bool _restoreAfterMinimize;
    private bool _summaryVisibleBeforeMinimize = true;
    private readonly HashSet<Guid> _notesVisibleBeforeMinimize = new();

    public bool Visible => _visible;

    public StickerHost(Window owner) => _owner = owner;

    public void Start()
    {
        EnsureGuidesSticker();
        // За вимогою: при кожному відкритті програми стікери завжди видимі.
        _visible = true;
        ShowAll();
    }

    private void EnsureGuidesSticker()
    {
        var data = AppStore.Instance.Data;
        if (data.Settings.GuidesStickerRemoved) return;
        if (data.Stickers.Any(s => s.Role == GuidesSticker.Role)) return;
        var area = SystemParameters.WorkArea;
        var left = _owner.Left + 360;
        var top = _owner.Top + 72;
        if (double.IsNaN(left) || left < area.Left) left = area.Left + 80;
        if (left + 440 > area.Right) left = Math.Max(area.Left + 16, area.Right - 456);
        if (double.IsNaN(top) || top < area.Top) top = area.Top + 72;
        data.Stickers.Add(GuidesSticker.Create(left, top));
        AppStore.Instance.Save(false);
    }

    public void Toggle()
    {
        // Якщо якусь нотатку згорнули, кнопка стікерів знову показує всі.
        // Ховає всі лише тоді, коли на екрані вже є кожна з них.
        if (_visible && AllShown()) HideAll();
        else ShowAll();
    }

    private bool AllShown()
    {
        if (_summary is not { IsVisible: true }) return false;
        return _windows.Values.All(w => w.IsVisible);
    }

    public void ShowAll()
    {
        _visible = true;
        EnsureSummaryWindow();
        var data = AppStore.Instance.Data;
        foreach (var note in data.Stickers.ToList())
            EnsureWindow(note);
        if (_summary != null)
        {
            _summary.Refresh();
            if (!_summary.IsVisible) _summary.Show();
        }
        foreach (var win in _windows.Values)
        {
            if (!win.IsVisible) win.Show();
        }
    }

    public void HideAll()
    {
        _visible = false;
        if (_summary != null) StickerShell.SafeHide(_summary);
        foreach (var win in _windows.Values)
            StickerShell.SafeHide(win);
    }

    public void RefreshSummary()
    {
        _summary?.Refresh();
    }

    public StickerNote AddNew()
    {
        var ownerLeft = _owner.Left;
        var ownerTop = _owner.Top;
        var note = new StickerNote
        {
            Left = ownerLeft + 80 + _windows.Count * 24,
            Top = ownerTop + 100 + _windows.Count * 24,
            Width = 300,
            Height = 300,
            ColorKey = StickerColors.All[AppStore.Instance.Data.Stickers.Count % StickerColors.All.Length].Key,
            FontSize = 16
        };
        AppStore.Instance.Data.Stickers.Add(note);
        AppStore.Instance.Save(false);
        if (_visible)
        {
            var win = EnsureWindow(note);
            win.Show();
            win.Activate();
        }
        return note;
    }

    private void EnsureSummaryWindow()
    {
        if (_summary != null) return;
        var state = AppStore.Instance.Data.StockSummarySticker;
        // якщо координати ще «нульові» зі старих даних — поруч із головним вікном
        if (state.Left < 8 && state.Top < 8)
        {
            state.Left = _owner.Left + 48;
            state.Top = _owner.Top + 120;
        }
        _summary = new StockSummaryStickerWindow(state, OnSummaryChanged)
        {
            Owner = _owner
        };
    }

    private void OnSummaryChanged(StockSummaryStickerState state)
    {
        AppStore.Instance.Data.StockSummarySticker = state;
        AppStore.Instance.Save(false);
    }

    private StickerWindow EnsureWindow(StickerNote note)
    {
        if (_windows.TryGetValue(note.Id, out var existing))
            return existing;

        var win = new StickerWindow(note, OnChanged, OnDelete, OnCopy)
        {
            Owner = _owner
        };
        _windows[note.Id] = win;
        return win;
    }

    private void OnChanged(StickerNote note)
    {
        var list = AppStore.Instance.Data.Stickers;
        var i = list.FindIndex(x => x.Id == note.Id);
        if (i >= 0) list[i] = note;
        AppStore.Instance.Save(false);
    }

    private void OnDelete(StickerNote note)
    {
        if (note.Role == GuidesSticker.Role)
            AppStore.Instance.Data.Settings.GuidesStickerRemoved = true;
        AppStore.Instance.Data.Stickers.RemoveAll(x => x.Id == note.Id);
        AppStore.Instance.Save(false);
        if (_windows.Remove(note.Id, out var win))
            CloseOwned(win);
    }

    private void OnCopy(StickerNote source)
    {
        var copy = new StickerNote
        {
            Left = source.Left + 28,
            Top = source.Top + 28,
            Width = source.Width,
            Height = source.Height,
            ColorKey = source.ColorKey,
            FontSize = source.FontSize,
            Rtf = source.Rtf
        };
        AppStore.Instance.Data.Stickers.Add(copy);
        AppStore.Instance.Save(false);
        if (_visible)
        {
            var win = EnsureWindow(copy);
            win.Show();
            win.Activate();
        }
    }

    public void CloseAll()
    {
        CloseOwned(_summary);
        _summary = null;
        foreach (var win in _windows.Values.ToList())
            CloseOwned(win);
        _windows.Clear();
    }

    private static void CloseOwned(Window? win)
    {
        if (win == null) return;
        try
        {
            if (win is StockSummaryStickerWindow summary) summary.PrepareClose();
            else if (win is StickerWindow note) note.PrepareClose();
            // Якщо стікер випадково став головним вікном, закриття завершило б увесь Обозний.
            if (ReferenceEquals(Application.Current?.MainWindow, win) && win.Owner != null)
                Application.Current.MainWindow = win.Owner;
            StickerShell.SafeHide(win);
            win.Owner = null;
            win.Close();
        }
        catch
        {
            // Вікно вже закрите (наприклад, після Alt+F4).
        }
    }

    public void SyncWithOwnerState(WindowState state)
    {
        if (state == WindowState.Minimized)
        {
            _summaryVisibleBeforeMinimize = _summary?.IsVisible == true;
            _notesVisibleBeforeMinimize.Clear();
            foreach (var pair in _windows)
            {
                if (pair.Value.IsVisible) _notesVisibleBeforeMinimize.Add(pair.Key);
            }
            _restoreAfterMinimize = true;
            if (_summary != null) StickerShell.SafeHide(_summary);
            foreach (var win in _windows.Values) StickerShell.SafeHide(win);
            return;
        }
        if (!_visible) return;
        // Після розгортання головного вікна прозора нотатка перестає приймати мишу.
        // Повертаємо її у звичайний розмір і наново чіпляємо до вікна програми.
        _owner.Dispatcher.BeginInvoke(RepairOwnedWindows, DispatcherPriority.ApplicationIdle);
    }

    private void RepairOwnedWindows()
    {
        if (!_visible || _owner.WindowState == WindowState.Minimized) return;
        Repair(_summary);
        foreach (var pair in _windows)
            Repair(pair.Value);
        if (!_restoreAfterMinimize) return;
        _restoreAfterMinimize = false;
        if (_summaryVisibleBeforeMinimize && _summary is { IsVisible: false })
            _summary.Show();
        foreach (var pair in _windows)
        {
            if (_notesVisibleBeforeMinimize.Contains(pair.Key) && !pair.Value.IsVisible)
                pair.Value.Show();
        }
    }

    private void Repair(Window? win)
    {
        if (win == null) return;
        try
        {
            if (win.WindowState != WindowState.Normal)
                win.WindowState = WindowState.Normal;
            win.Owner = null;
            win.Owner = _owner;
        }
        catch
        {
            // Вікно вже закрите.
        }
    }
}

/// <summary>Прозоре вікно нотатки не можна розгортати: у повному екрані воно не ловить мишу і падає на закритті.</summary>
internal static class StickerShell
{
    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var source = PresentationSource.FromVisual(window) as HwndSource
                         ?? HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source?.AddHook(BlockMaximize);
        };
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState == WindowState.Normal || !window.IsVisible) return;
            try { window.WindowState = WindowState.Normal; }
            catch { /* вікно вже закривається */ }
        };
    }

    public static void HideInsteadOfClose(Window window, System.ComponentModel.CancelEventArgs e, bool allowClose)
    {
        if (allowClose) return;
        e.Cancel = true;
        window.Dispatcher.BeginInvoke(() => SafeHide(window));
    }

    /// <summary>
    /// Ховає нотатку, не згортаючи головне вікно.
    /// Якщо сховати активне дочірнє вікно, Windows згортає його власника.
    /// </summary>
    public static void SafeHide(Window window)
    {
        var owner = window.Owner;
        try
        {
            // Поки нотатка лишається дочірньою і активною, Hide згортає головне вікно.
            if (owner != null)
                window.Owner = null;
            if (owner != null && owner.IsVisible && owner.WindowState != WindowState.Minimized)
                owner.Activate();
            if (window.IsVisible)
                window.Hide();
        }
        catch
        {
            // Вікно вже закрите.
        }
        finally
        {
            try
            {
                if (owner != null && window.Owner == null)
                    window.Owner = owner;
            }
            catch
            {
                // Вікно вже закрите.
            }
        }
    }

    private static IntPtr BlockMaximize(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_SYSCOMMAND = 0x0112;
        const int SC_MAXIMIZE = 0xF030;
        if (msg == WM_SYSCOMMAND && (wParam.ToInt32() & 0xFFF0) == SC_MAXIMIZE)
            handled = true;
        return IntPtr.Zero;
    }

    public static bool IsUsableSize(double width, double height)
    {
        var area = SystemParameters.WorkArea;
        return width >= 40 && height >= 40
               && width <= area.Width * 0.92
               && height <= area.Height * 0.92;
    }
}
