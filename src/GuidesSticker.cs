using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Obozny;

/// <summary>Збереження тексту стікера. Старі нотатки лишаються RTF, нові з посиланнями — XAML.</summary>
internal static class StickerText
{
    public const string XamlMark = "xaml:";

    public static readonly Brush LinkBrush = Freeze(Color.FromRgb(0x1B, 0x4F, 0x8A));

    public static string Save(FlowDocument doc)
    {
        var range = new TextRange(doc.ContentStart, doc.ContentEnd);
        using var ms = new MemoryStream();
        range.Save(ms, DataFormats.Xaml);
        return XamlMark + Convert.ToBase64String(ms.ToArray());
    }

    public static Hyperlink MakeLink(string text, Uri uri) =>
        new(new Run(text))
        {
            NavigateUri = uri,
            Foreground = LinkBrush,
            TextDecorations = TextDecorations.Underline
        };

    public static bool TryAddress(string raw, out Uri uri)
    {
        uri = null!;
        var s = raw.Trim();
        if (s.Length == 0) return false;
        if (!s.Contains("://", StringComparison.Ordinal))
        {
            if (!s.Contains('.')) return false;
            s = "https://" + s;
        }
        if (!Uri.TryCreate(s, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;
        uri = parsed;
        return true;
    }

    public static Hyperlink? HyperlinkOf(TextPointer? pointer)
    {
        DependencyObject? node = pointer?.Parent;
        while (node != null)
        {
            if (node is Hyperlink link) return link;
            node = node is FrameworkContentElement element ? element.Parent : null;
        }
        return null;
    }

    public static string Plain(Hyperlink link) =>
        new TextRange(link.ContentStart, link.ContentEnd).Text.Trim().TrimEnd('\r', '\n');

    /// <summary>
    /// Збереження XAML записує типові властивості документа (Georgia, вирівнювання, чорний колір).
    /// Прибираємо їх, щоб лишились шрифт і колір самого стікера, а жирний текст і посилання — свої.
    /// </summary>
    public static void ReleaseChrome(FlowDocument doc)
    {
        doc.ClearValue(TextElement.FontFamilyProperty);
        doc.ClearValue(TextElement.FontSizeProperty);
        doc.ClearValue(TextElement.FontStyleProperty);
        doc.ClearValue(TextElement.FontWeightProperty);
        doc.ClearValue(TextElement.FontStretchProperty);
        doc.ClearValue(TextElement.ForegroundProperty);
        doc.ClearValue(Block.TextAlignmentProperty);
        doc.ClearValue(Block.LineHeightProperty);
        doc.ClearValue(FlowDocument.IsHyphenationEnabledProperty);
        doc.ClearValue(FrameworkContentElement.LanguageProperty);
        doc.PagePadding = new Thickness(0);
        foreach (var block in doc.Blocks)
            ReleaseBlock(block);
    }

    /// <summary>Прибирає шрифт і колір, які XAML записує в кожен абзац. Жирний і колір посилання лишаються.</summary>
    private static void ReleaseBlock(Block block)
    {
        ReleaseLook(block);
        block.ClearValue(Block.TextAlignmentProperty);
        block.ClearValue(Block.LineHeightProperty);
        switch (block)
        {
            case Paragraph paragraph:
                foreach (var inline in paragraph.Inlines)
                    ReleaseInline(inline);
                break;
            case Section section:
                foreach (var child in section.Blocks)
                    ReleaseBlock(child);
                break;
            case List list:
                foreach (var item in list.ListItems)
                {
                    ReleaseLook(item);
                    foreach (var child in item.Blocks)
                        ReleaseBlock(child);
                }
                break;
        }
    }

    private static void ReleaseInline(Inline inline)
    {
        switch (inline)
        {
            case Hyperlink link:
                link.ClearValue(TextElement.FontFamilyProperty);
                link.ClearValue(TextElement.FontSizeProperty);
                foreach (var child in link.Inlines)
                    ReleaseInline(child);
                break;
            case Span span:
                ReleaseLook(span);
                foreach (var child in span.Inlines)
                    ReleaseInline(child);
                break;
            default:
                ReleaseLook(inline);
                break;
        }
    }

    private static void ReleaseLook(TextElement element)
    {
        element.ClearValue(TextElement.FontFamilyProperty);
        element.ClearValue(TextElement.FontSizeProperty);
        element.ClearValue(TextElement.FontStyleProperty);
        element.ClearValue(TextElement.FontStretchProperty);
        element.ClearValue(TextElement.ForegroundProperty);
    }

    public static void RememberAddresses(FlowDocument doc)
    {
        foreach (var block in doc.Blocks)
            RememberBlock(block);
    }

    private static void RememberBlock(Block block)
    {
        switch (block)
        {
            case Paragraph paragraph:
                RememberInlines(paragraph.Inlines);
                break;
            case Section section:
                foreach (var child in section.Blocks) RememberBlock(child);
                break;
            case List list:
                foreach (var item in list.ListItems)
                    foreach (var child in item.Blocks) RememberBlock(child);
                break;
        }
    }

    private static void RememberInlines(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Hyperlink link:
                    RememberInlines(link.Inlines);
                    break;
                case Span span:
                    RememberInlines(span.Inlines);
                    break;
            }
        }
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

internal static class GuidesSticker
{
    public const string Role = "guides";

    private static readonly (string Text, string Url)[] Links =
    {
        ("Інструкція з обліку майна (Наказ 440)", "https://zakon.rada.gov.ua/laws/show/z1192-17#Text"),
        ("Порядок списання військового майна (Наказ 81)", "https://zakon.rada.gov.ua/laws/show/z0655-21#Text"),
        ("Порядок використання інженерного майна (Наказ 35)", "https://zakon.rada.gov.ua/laws/show/z0274-18#Text"),
        ("Інструкція по документам ДСК (Постанова 736)", "https://zakon.rada.gov.ua/laws/show/736-2016-%D0%BF#n11"),
        ("Закон про відчуження майна", "https://zakon.rada.gov.ua/laws/show/4765-17"),
        ("Порядок погодження рішень про відчуження (Наказ 685)", "https://zakon.rada.gov.ua/laws/show/z1715-24#Text"),
        ("Методика визначення залишкової вартості майна (Постанова 759)", "https://zakon.rada.gov.ua/laws/show/759-98-%D0%BF#Text"),
        ("Порядок передачі ІФС у тимчасове користування (Наказ 139)", "https://zakon.rada.gov.ua/laws/show/z0371-25#n54")
    };

    public static StickerNote Create(double left, double top)
    {
        var doc = new FlowDocument { PagePadding = new Thickness(0) };
        var title = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
        title.Inlines.Add(new Run("Керівні документи") { FontWeight = FontWeights.Bold });
        doc.Blocks.Add(title);
        var list = new List
        {
            MarkerStyle = TextMarkerStyle.Disc,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            MarkerOffset = 12
        };
        foreach (var (text, url) in Links)
        {
            list.ListItems.Add(new ListItem(new Paragraph(StickerText.MakeLink(text, new Uri(url)))
            {
                Margin = new Thickness(0)
            })
            {
                Margin = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(0)
            });
        }
        doc.Blocks.Add(list);

        return new StickerNote
        {
            Left = left,
            Top = top,
            Width = 420,
            Height = 248,
            ColorKey = "teal",
            FontSize = 13,
            Role = Role,
            Rtf = StickerText.Save(doc)
        };
    }
}
