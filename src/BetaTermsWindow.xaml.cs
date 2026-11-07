using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Obozny;

public partial class BetaTermsWindow : Window
{
    private const double ScrollEpsilon = 2.0;
    private bool _reachedEnd;

    public BetaTermsWindow()
    {
        InitializeComponent();
        BuildBody();
        Loaded += (_, _) =>
        {
            // Після повного layout ExtentHeight уже реальний (не 0).
            Dispatcher.BeginInvoke(UpdateScrollGate, DispatcherPriority.ContextIdle);
        };
        ContentRendered += (_, _) => UpdateScrollGate();
        SizeChanged += (_, _) =>
        {
            if (!_reachedEnd)
                UpdateScrollGate();
        };
    }

    private void BuildBody()
    {
        TermsBody.Children.Clear();
        foreach (var (heading, body) in BetaLicense.Sections)
        {
            TermsBody.Children.Add(new TextBlock
            {
                Text = heading,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = (Brush)FindResource("TextPrimary"),
                TextWrapping = TextWrapping.Wrap,
            });
            TermsBody.Children.Add(new TextBlock
            {
                Text = body,
                FontSize = 15,
                LineHeight = 24,
                Margin = new Thickness(0, 0, 0, 22),
                Foreground = (Brush)FindResource("TextPrimary"),
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    private void TermsScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateScrollGate();

    private void UpdateScrollGate()
    {
        if (_reachedEnd) return;
        var sv = TermsScroll;
        // До вимірювання контенту не відкриваємо кнопку (раніший баг: ExtentHeight==0).
        if (sv.ExtentHeight < 8 || sv.ViewportHeight < 8)
            return;

        var needsScroll = sv.ExtentHeight > sv.ViewportHeight + ScrollEpsilon;
        bool atBottom;
        if (needsScroll)
        {
            atBottom = sv.VerticalOffset + sv.ViewportHeight >= sv.ExtentHeight - ScrollEpsilon;
        }
        else
        {
            // Текст вмістився без скролу — користувач бачить усе; дозволяємо (на практиці текст довший).
            atBottom = true;
        }

        if (!atBottom) return;
        _reachedEnd = true;
        AgreeBtn.Visibility = Visibility.Visible;
        AgreeBtn.IsEnabled = true;
        ScrollHint.Visibility = Visibility.Collapsed;
    }

    private void AgreeClick(object sender, RoutedEventArgs e)
    {
        if (!_reachedEnd) return;
        BetaLicense.Accept();
        DialogResult = true;
        Close();
    }

    private void ExitClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
