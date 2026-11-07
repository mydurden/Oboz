using System.Windows;

namespace Obozny;

public partial class TextReportWindow : Window
{
    public TextReportWindow(string text, string? title = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(title))
            Title = title;
        Body.Text = text ?? "";
    }

    private void CopyAll(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(Body.Text)) return;
        try
        {
            Clipboard.SetDataObject(Body.Text);
        }
        catch
        {
            // буфер зайнятий іншою програмою
        }
    }

    private void Ok(object sender, RoutedEventArgs e) => DialogResult = true;
}
