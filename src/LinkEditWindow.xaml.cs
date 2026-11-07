using System.Windows;

namespace Obozny;

public partial class LinkEditWindow : Window
{
    public bool Removed { get; private set; }
    public string LinkText => TextBox.Text.Trim();
    public Uri? Address { get; private set; }

    public LinkEditWindow(string text, string address, bool canRemove)
    {
        InitializeComponent();
        TextBox.Text = text;
        AddressBox.Text = address;
        RemoveBtn.Visibility = canRemove ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) =>
        {
            TextBox.Focus();
            TextBox.SelectAll();
        };
    }

    private void Ok(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TextBox.Text))
        {
            MessageBox.Show(this, "Вкажіть текст посилання.", "Стікери",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!StickerText.TryAddress(AddressBox.Text, out var uri))
        {
            MessageBox.Show(this, "Адреса має бути посиланням http або https.", "Стікери",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Address = uri;
        DialogResult = true;
    }

    private void Remove(object sender, RoutedEventArgs e)
    {
        Removed = true;
        DialogResult = true;
    }
}
