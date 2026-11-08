using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Obozny;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                MessageBox.Show(
                    "Неочікувана помилка:\n" + args.Exception.Message,
                    "Обозний",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { /* ignore */ }
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                var msg = (args.ExceptionObject as Exception)?.Message
                    ?? args.ExceptionObject?.ToString()
                    ?? "невідома помилка";
                MessageBox.Show(
                    "Критична помилка:\n" + msg,
                    "Обозний",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { /* ignore */ }
        };

        // Мінімальне вікно одразу — старт не «висить» без UI.
        var boot = new Window
        {
            Title = "Обозний",
            Width = 360,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = true,
            Content = new TextBlock
            {
                Text = "Завантаження…",
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16)
            }
        };
        boot.Show();

        Dispatcher.BeginInvoke(() => ContinueStartup(boot), DispatcherPriority.ApplicationIdle);
    }

    private void ContinueStartup(Window boot)
    {
        try
        {
            var ua = CultureInfo.GetCultureInfo("uk-UA");
            CultureInfo.DefaultThreadCurrentCulture = ua;
            CultureInfo.DefaultThreadCurrentUICulture = ua;
            Thread.CurrentThread.CurrentCulture = ua;
            Thread.CurrentThread.CurrentUICulture = ua;

            UiCopy.Install();
            TableWheelScroll.Install();
            AppStore.Instance.Load();
            ThemeService.ApplyFromSettings();

            ShutdownMode = ShutdownMode.OnMainWindowClose;
            var main = new MainWindow();
            MainWindow = main;
            main.Show();
            main.Activate();
            try { boot.Close(); } catch { /* ignore */ }
        }
        catch (Exception ex)
        {
            try
            {
                MessageBox.Show(
                    "Помилка запуску:\n" + ex.Message,
                    "Обозний",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { /* ignore */ }
            Shutdown();
        }
    }
}
