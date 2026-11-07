using System.Windows;

namespace Obozny;

/// <summary>Ввід назви користувацького виду майна.</summary>
public partial class NewCustomVydWindow : Window
{
    private readonly IReadOnlyList<string> _existingCustom;

    public string? CreatedName { get; private set; }

    public NewCustomVydWindow(IEnumerable<string>? existingCustom)
    {
        InitializeComponent();
        _existingCustom = (existingCustom ?? Array.Empty<string>()).ToList();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        var name = NomenclatureVydCatalog.NormalizeNewVydName(NameBox.Text, _existingCustom);
        if (name == null)
        {
            MessageBox.Show(
                "Вкажіть унікальну назву виду (не порожню і не з каталогу).",
                "Обозний", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CreatedName = name;
        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
