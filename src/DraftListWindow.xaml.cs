using System.Windows;
using System.Windows.Input;

namespace Obozny;

public partial class DraftListWindow : Window
{
    private readonly WriteOffChoice _choice;
    public Document? Selected { get; private set; }
    public bool CreateNew { get; private set; }

    public DraftListWindow(WriteOffChoice choice)
    {
        _choice = choice;
        InitializeComponent();
        Title = choice.WorkTypeName;
        Reload();
    }

    private void Reload()
    {
        var items = AppStore.Instance.Data.Documents
            .Where(d => Matches(d, _choice))
            .OrderByDescending(d => d.Date)
            .ThenByDescending(d => d.CreatedAt)
            .Select(d => new DraftListItem(d))
            .ToList();
        Grid.ItemsSource = items;
        Hint.Text = items.Count == 0
            ? "Поки немає збережених документів цього виду. Можна створити новий."
            : "Відкрийте документ подвійним натисканням або створіть новий. Проведення робиться вже в самому документі.";
    }

    public static int Count(WriteOffChoice choice) =>
        AppStore.Instance.Data.Documents.Count(d => Matches(d, choice));

    public static bool Matches(Document doc, WriteOffChoice choice)
    {
        if (doc.Status != DocStatus.Draft || doc.Type != choice.DocType) return false;
        if (choice.DocType == DocType.Loss) return true;
        return string.Equals(doc.WorkTypeName?.Trim(), choice.WorkTypeName.Trim(), StringComparison.Ordinal);
    }

    private void CreateClick(object sender, RoutedEventArgs e)
    {
        CreateNew = true;
        DialogResult = true;
    }

    private void GridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is not DraftListItem item) return;
        Selected = item.Document;
        DialogResult = true;
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private sealed class DraftListItem
    {
        public Document Document { get; }
        public string Number => Document.Number;
        public string DateText => Document.Date.ToString("dd.MM.yyyy");
        public string Note => Document.Note;

        public DraftListItem(Document document) => Document = document;
    }
}
