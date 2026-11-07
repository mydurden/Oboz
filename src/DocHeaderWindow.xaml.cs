using System.Windows;

namespace Obozny;

public partial class DocHeaderWindow : Window
{
    private readonly bool _pairedVr;

    /// <summary>Номер ЯС / ЗЯС / звичайного документа; порожнє → безномерний.</summary>
    public string Number => _pairedVr ? YasNumberBox.Text.Trim() : NumberBox.Text.Trim();

    /// <summary>Номер Акта ВР (ЯС або ЗЯС); порожнє → «н/д» у документах.</summary>
    public string VrActNumber => _pairedVr ? VrNumberBox.Text.Trim() : "";

    public DateTime Date => DateBox.SelectedDate ?? DateTime.Today;
    public string Note => NoteBox.Text.Trim();

    public DocHeaderWindow(DocType type, string? workTypeName = null)
    {
        InitializeComponent();
        _pairedVr = type is DocType.QualityState or DocType.QualityChange;

        if (_pairedVr)
        {
            Title = "Підготувати документи";
            DocTypeText.Text = "Підготувати документи";
            WorkTypeText.Visibility = Visibility.Visible;
            SingleNumberPanel.Visibility = Visibility.Collapsed;
            YasVrNumberPanel.Visibility = Visibility.Visible;
            VrNumberBox.Text = "";
            YasNumberBox.Text = "";

            if (type == DocType.QualityChange)
            {
                WorkTypeText.Text = "Акти зміни якісного стану та виконаних робіт";
                PairedMainNumberLabel.Text = "Акт зміни якісного стану";
            }
            else
            {
                WorkTypeText.Text = "Акти якісного стану та виконаних робіт";
                PairedMainNumberLabel.Text = "Акт якісного стану";
            }
        }
        else
        {
            Title = "Підготувати документ";
            DocTypeText.Text = DocTypeNames.Title(type);
            var work = (workTypeName ?? "").Trim();
            if (string.IsNullOrEmpty(work))
                WorkTypeText.Visibility = Visibility.Collapsed;
            else
                WorkTypeText.Text = work;
            NumberBox.Text = "";
        }

        DateBox.SelectedDate = DateTime.Today;
    }

    private void Ok(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
