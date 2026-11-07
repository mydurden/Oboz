using System.Windows;

namespace Obozny;

public partial class SummaryInfoWindow : Window
{
    public SummaryInfoWindow()
    {
        InitializeComponent();
        var summary = AppStore.Instance.GetStartupSummary();
        ReservedCountText.Text =
            $"Зарезервоване до списання майно: {summary.ReservedPositions}";
        ReservedValueText.Text =
            $"Вартість майна, що зарезервоване до списання: {Ua.Num(summary.ReservedValue, "#,##0.00")}";
        DraftCountText.Text =
            $"Підготовлено до списання чернеток документів: {summary.DraftCount}";
    }

    private void OkClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
