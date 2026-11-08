using System.Windows;
using System.Windows.Controls;

namespace Obozny;

public partial class TypePickWindow : Window
{
    public WriteOffChoice? Selected { get; private set; }

    public TypePickWindow()
        : this(
            "Підготувати документи на списання майна",
            "",
            WriteOffChoices.All)
    {
    }

    public TypePickWindow(string title, string prompt, IReadOnlyList<WriteOffChoice> choices)
    {
        InitializeComponent();
        Title = title;
        var p = (prompt ?? "").Trim();
        PromptText.Text = p;
        PromptText.Visibility = p.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (choices.Count <= 3)
        {
            Height = 420;
            MinHeight = 360;
        }
        foreach (var choice in choices)
        {
            var btn = new Button
            {
                Content = choice.WorkTypeName,
                Tag = choice,
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(16, 14, 16, 14)
            };
            btn.Click += Pick;
            ChoicesPanel.Children.Add(btn);
        }
    }

    private void Pick(object sender, RoutedEventArgs e)
    {
        Selected = (WriteOffChoice)((Button)sender).Tag;
        DialogResult = true;
    }

    private void Cancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
