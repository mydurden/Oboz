using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Obozny;

public partial class ProfileWindow : Window
{
    private string _themeId;
    private bool? _menuDark;

    public ProfileWindow()
    {
        InitializeComponent();
        var s = AppStore.Instance.Data.Settings;
        UnitBox.Text = s.UnitName;
        NameBox.Text = s.UserName;
        _themeId = ThemeService.Resolve(s.Theme).Id;
        UpdateThemeCards();
    }

    private void PickLight(object sender, MouseButtonEventArgs e)
    {
        ToggleMenu(false);
        e.Handled = true;
    }

    private void PickDark(object sender, MouseButtonEventArgs e)
    {
        ToggleMenu(true);
        e.Handled = true;
    }

    private void ToggleMenu(bool dark)
    {
        if (_menuDark == dark && ThemeGrid.Visibility == Visibility.Visible)
        {
            _menuDark = null;
            ThemeGrid.Visibility = Visibility.Collapsed;
            ThemeGrid.Children.Clear();
            UpdateThemeCards();
            return;
        }

        _menuDark = dark;
        ThemeGrid.Children.Clear();
        foreach (var theme in dark ? ThemeService.DarkThemes : ThemeService.LightThemes)
            ThemeGrid.Children.Add(MakeCell(theme));
        ThemeGrid.Visibility = Visibility.Visible;
        UpdateThemeCards();
    }

    private Border MakeCell(AppTheme theme)
    {
        var selected = theme.Id == _themeId;
        var border = new Border
        {
            Height = 72,
            Margin = new Thickness(0),
            Cursor = Cursors.Hand,
            Background = ThemeService.Swatch(theme),
            BorderBrush = selected ? (Brush)FindResource("Accent") : Brushes.Transparent,
            BorderThickness = selected ? new Thickness(3) : new Thickness(0),
            SnapsToDevicePixels = true,
            Child = new TextBlock
            {
                Text = theme.Name,
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                FontFamily = new FontFamily(ThemeService.AppFontName),
                Foreground = new SolidColorBrush(Parse(theme.SwatchInk)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 0, 4, 0),
            },
        };
        border.MouseLeftButtonUp += (_, e) =>
        {
            _themeId = theme.Id;
            ThemeService.Apply(theme.Id);
            AppStore.Instance.Save(false);
            _menuDark = null;
            ThemeGrid.Visibility = Visibility.Collapsed;
            ThemeGrid.Children.Clear();
            UpdateThemeCards();
            e.Handled = true;
        };
        return border;
    }

    private void UpdateThemeCards()
    {
        var dark = ThemeService.Resolve(_themeId).Dark;
        var lightOpen = _menuDark == false;
        var darkOpen = _menuDark == true;
        var accent = (Brush)FindResource("Accent");
        var clear = Brushes.Transparent;
        LightCard.BorderBrush = !dark || lightOpen ? accent : clear;
        DarkCard.BorderBrush = dark || darkOpen ? accent : clear;
        LightCard.Opacity = !dark || lightOpen ? 1.0 : 0.72;
        DarkCard.Opacity = dark || darkOpen ? 1.0 : 0.72;
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    private void Save(object sender, RoutedEventArgs e)
    {
        var s = AppStore.Instance.Data.Settings;
        s.UnitName = UnitBox.Text.Trim();
        s.UserName = NameBox.Text.Trim();
        s.Theme = _themeId;
        ThemeService.Apply(s.Theme);
        AppStore.Instance.Save();
        DialogResult = true;
    }

    private void ClearStock(object sender, RoutedEventArgs e)
    {
        var ans = MessageBox.Show(
            "УВАГА!\n\nБуде видалено все майно зі складу, усі документи і всі рухи.\n" +
            "Налаштування профілю залишаться.\n" +
            "Перед очищенням програма зробить бекап залишку.\n\n" +
            "Точно очистити склад?",
            "Обозний — очистити склад",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (ans != MessageBoxResult.Yes) return;

        AppStore.Instance.ClearWarehouse();
        MessageBox.Show("Склад очищено. Можна завантажувати майно заново.", "Обозний");
        DialogResult = true;
    }

    private void Cancel(object sender, RoutedEventArgs e)
    {
        ThemeService.ApplyFromSettings();
        DialogResult = false;
    }
}
