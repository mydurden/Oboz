using System.Windows;
using System.Windows.Controls;

namespace Obozny;

public partial class NoteEditWindow : Window
{
    public string Note => NoteBox.Text.Trim();

    public NoteEditWindow(string? note, string documentTitle)
    {
        InitializeComponent();
        TitleText.Text = string.IsNullOrWhiteSpace(documentTitle)
            ? "Примітка"
            : documentTitle;
        NoteBox.Text = note ?? "";
    }

    public static void ApplyButton(Button button, string? note)
    {
        var has = !string.IsNullOrWhiteSpace(note);
        button.Content = has ? "Є примітка" : "Примітка";
        button.Style = (Style)button.FindResource(has ? typeof(Button) : "GhostButton");
    }

    private void Ok(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
