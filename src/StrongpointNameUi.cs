using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Obozny;

/// <summary>Відображення назви ОП з курсивним суфіксом *ЧЕРНЕТКА* до проведення.</summary>
internal static class StrongpointNameUi
{
    public const string DraftSuffix = "*ЧЕРНЕТКА*";

    public static string BaseName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Опорний пункт" : name.Trim();

    public static void Apply(TextBlock target, string? name, bool isDraft)
    {
        var fg = target.Foreground;
        target.Inlines.Clear();
        target.Inlines.Add(new Run(BaseName(name)) { Foreground = fg });
        if (!isDraft) return;
        target.Inlines.Add(new Run(" "));
        target.Inlines.Add(new Run(DraftSuffix)
        {
            FontStyle = FontStyles.Italic,
            Foreground = fg
        });
    }

    /// <summary>Для CopyText (read-only TextBox): назва + суфікс *ЧЕРНЕТКА* як виділюваний текст.</summary>
    public static void Apply(TextBox target, string? name, bool isDraft) =>
        target.Text = isDraft ? $"{BaseName(name)} {DraftSuffix}" : BaseName(name);
}

/// <summary>Комірка таблиці ОП: назва + курсивний *ЧЕРНЕТКА* для чернетки.</summary>
public sealed class StrongpointNameBlock : TextBlock
{
    public StrongpointNameBlock()
    {
        TextWrapping = TextWrapping.Wrap;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(4, 0, 8, 0);
        DataContextChanged += (_, _) => Refresh();
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        if (DataContext is StrongpointRow row)
        {
            if (TryFindResource("TextPrimary") is Brush brush)
                Foreground = brush;
            StrongpointNameUi.Apply(this, row.Name, row.IsDraft);
        }
    }
}
