using System.Windows;
using System.Windows.Media;

namespace Obozny;

internal sealed class AppTheme
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required bool Dark { get; init; }
    public required string Page { get; init; }
    public required string Header { get; init; }
    public required string HeaderText { get; init; }
    public required string HeaderMuted { get; init; }
    public required string Button { get; init; }
    public required string ButtonText { get; init; }
    public required string Menu { get; init; }
    public required string Ghost { get; init; }
    public required string GhostText { get; init; }
    public required string Card { get; init; }
    public required string Text { get; init; }
    public required string Muted { get; init; }
    public required string Line { get; init; }
    public required string Head { get; init; }
    public required string Filter { get; init; }
    public required string Mark { get; init; }
    public required string Danger { get; init; }
    public required string Link { get; init; }
    public required string SwatchDark { get; init; }
    public required string SwatchMid { get; init; }
    public required string SwatchLight { get; init; }
    public required string SwatchInk { get; init; }
}

internal static class ThemeService
{
    public const string Light = "Light";
    public const string Dark = "Dark";

    /// <summary>Спільний UI-шрифт усіх тем (і нових також).</summary>
    public const string AppFontName = "Bahnschrift";

    private static readonly AppTheme[] Themes =
    [
        // Muted підкручено для явного контрасту з TextPrimary у списках (номенклатура тощо),
        // без системного сірого; підписи полів лишаються читабельні на Page/Card.
        Theme("lon", "Льон", false,
            page: "#F3F1E8", header: "#2E3A2C", headerText: "#F4F1E6", headerMuted: "#C5CDB4",
            button: "#4E6244", buttonText: "#F7F6EF", menu: "#E4E6D4",
            ghost: "#D5D8C4", ghostText: "#2C3828", card: "#FBFBF6", text: "#1E281E", muted: "#6A7A66",
            line: "#D5D8C8", head: "#EEF0E4", filter: "#D5D0A4", mark: "#9A4450", danger: "#9A4450", link: "#3E5A34",
            swatchDark: "#4E6244", swatchMid: "#D5D0A4", swatchLight: "#F3F1E8", swatchInk: "#1E281E"),
        Theme("tuman", "Туман", false,
            page: "#F2F5F7", header: "#243640", headerText: "#F4F7F8", headerMuted: "#B7C6CE",
            button: "#3A6478", buttonText: "#F4F8F8", menu: "#E3EAEE",
            ghost: "#D5E0E6", ghostText: "#243640", card: "#FBFCFD", text: "#162028", muted: "#687A84",
            line: "#D5DEE4", head: "#EEF3F5", filter: "#D5E2EA", mark: "#9A4454", danger: "#9A4454", link: "#1E5878",
            swatchDark: "#3A6478", swatchMid: "#D5E2EA", swatchLight: "#F2F5F7", swatchInk: "#162028"),
        Theme("pshenytsia", "Пшениця", false,
            page: "#FBF6EC", header: "#4A3418", headerText: "#FFF8EE", headerMuted: "#E2CBA6",
            button: "#A86B1C", buttonText: "#FFF8EE", menu: "#F3E6CC",
            ghost: "#EAD7B0", ghostText: "#4A3418", card: "#FFFCF6", text: "#241C12", muted: "#7A6850",
            line: "#E6D5B4", head: "#F6EDDC", filter: "#F0D48A", mark: "#9A3E48", danger: "#9A3E48", link: "#7A3E16",
            swatchDark: "#A86B1C", swatchMid: "#F0D48A", swatchLight: "#FBF6EC", swatchInk: "#241C12"),
        Theme("grafit", "Графіт", true,
            page: "#1A1C1E", header: "#121416", headerText: "#ECEEEF", headerMuted: "#9AA2A8",
            button: "#6E8CA8", buttonText: "#0E1418", menu: "#24282C",
            ghost: "#2C3238", ghostText: "#E2E6EA", card: "#22262A", text: "#F0F2F4", muted: "#7A848C",
            line: "#343A40", head: "#2A3036", filter: "#4A6278", mark: "#E098A4", danger: "#9A3040", link: "#9EC0E4",
            swatchDark: "#121416", swatchMid: "#2A3038", swatchLight: "#6E8CA8", swatchInk: "#F0F2F4"),
        Theme("smola", "Смола", true,
            page: "#1A1814", header: "#12110E", headerText: "#F4E8D4", headerMuted: "#C4B090",
            button: "#D4A24A", buttonText: "#1A1408", menu: "#242018",
            ghost: "#322C22", ghostText: "#F4E8D4", card: "#221E18", text: "#F8ECD8", muted: "#9A8868",
            line: "#3A3428", head: "#2A261E", filter: "#C4923A", mark: "#E0A0A4", danger: "#9A3040", link: "#E8C878",
            swatchDark: "#12110E", swatchMid: "#3A3018", swatchLight: "#D4A24A", swatchInk: "#F8ECD8"),
        Theme("varta", "Варта", true,
            page: "#101612", header: "#0C120E", headerText: "#E4F4EA", headerMuted: "#8FB8A0",
            button: "#3E9A6A", buttonText: "#F2FFF6", menu: "#18241C",
            ghost: "#1E3026", ghostText: "#D4EEE0", card: "#16201A", text: "#EAF8F0", muted: "#6A9078",
            line: "#24382C", head: "#1C2C24", filter: "#2E6A4A", mark: "#E098A4", danger: "#9A3040", link: "#9EDCB8",
            swatchDark: "#0C120E", swatchMid: "#243428", swatchLight: "#3E9A6A", swatchInk: "#EAF8F0"),
    ];

    public static IReadOnlyList<AppTheme> LightThemes { get; } = Themes.Where(t => !t.Dark).ToArray();
    public static IReadOnlyList<AppTheme> DarkThemes { get; } = Themes.Where(t => t.Dark).ToArray();

    public static AppTheme CurrentTheme { get; private set; } = Themes[0];

    public static string Current => CurrentTheme.Id;

    public static bool IsDark => CurrentTheme.Dark;

    public static event Action? Changed;

    public static Typeface UiTypeface(FontWeight weight) =>
        new(new FontFamily(AppFontName), FontStyles.Normal, weight, FontStretches.Normal);

    public static AppTheme Resolve(string? theme)
    {
        var id = theme?.Trim() ?? "";
        var found = Themes.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
        if (found != null) return found;
        // Старі id (Light/Dark) і видалені теми (polotno, kreida, arkhiv) → безпечний дефолт.
        if (string.Equals(id, Dark, StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, "grafit", StringComparison.OrdinalIgnoreCase))
            return Themes.First(t => t.Id == "grafit");
        return Themes.First(t => t.Id == "lon");
    }

    public static void ApplyFromSettings() => Apply(AppStore.Instance.Data.Settings.Theme);

    public static void Apply(string theme)
    {
        var picked = Resolve(theme);
        CurrentTheme = picked;
        var settings = AppStore.Instance.Data.Settings;
        var remapped = !string.Equals(settings.Theme, picked.Id, StringComparison.OrdinalIgnoreCase);
        settings.Theme = picked.Id;
        Paint(picked);
        if (Application.Current != null)
            Application.Current.Resources["AppFont"] = new FontFamily(AppFontName);
        // Зберегти, якщо з налаштувань прийшов видалений/старий id (arkhiv, polotno, kreida, Light…).
        if (remapped)
            AppStore.Instance.Save(notify: false);
        Changed?.Invoke();
    }

    public static Brush Swatch(AppTheme theme)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };
        brush.GradientStops.Add(new GradientStop(Parse(theme.SwatchDark), 0));
        brush.GradientStops.Add(new GradientStop(Parse(theme.SwatchMid), 0.42));
        brush.GradientStops.Add(new GradientStop(Parse(theme.SwatchLight), 1));
        brush.Freeze();
        return brush;
    }

    public static Uri LogoUri =>
        IsDark
            ? new Uri("pack://application:,,,/Assets/logo_dark.png")
            : new Uri("pack://application:,,,/Assets/logo.png");

    private static void Paint(AppTheme theme)
    {
        Set("HeaderBg", theme.Header);
        Set("HeaderFg", theme.HeaderText);
        Set("HeaderMuted", theme.HeaderMuted);
        Set("Accent", theme.Button);
        Set("ButtonFg", theme.ButtonText);
        Set("AccentDark", theme.Menu);
        Set("PageBg", theme.Page);
        Set("CardBg", theme.Card);
        Set("Muted", theme.Muted);
        Set("GhostBg", theme.Ghost);
        Set("GhostFg", theme.GhostText);
        Set("GridLine", theme.Line);
        Set("GridLineV", theme.Line);
        Set("HeaderCellBg", theme.Head);
        Set("DraftBg", theme.Card);
        Set("DraftPanelSoft", theme.Head);
        Set("FilterActive", theme.Filter);
        Set("PinkMark", theme.Mark);
        Set("Danger", theme.Danger);
        Set("InputBorder", theme.Line);
        Set("TextPrimary", theme.Text);
        Set("Link", theme.Link);
    }

    private static void Set(string key, string hex)
    {
        var brush = new SolidColorBrush(Parse(hex));
        brush.Freeze();
        Application.Current.Resources[key] = brush;
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    private static AppTheme Theme(
        string id, string name, bool dark,
        string page, string header, string headerText, string headerMuted,
        string button, string buttonText, string menu,
        string ghost, string ghostText, string card, string text, string muted,
        string line, string head, string filter, string mark, string danger, string link,
        string swatchDark, string swatchMid, string swatchLight, string swatchInk) => new()
    {
        Id = id, Name = name, Dark = dark,
        Page = page, Header = header, HeaderText = headerText, HeaderMuted = headerMuted,
        Button = button, ButtonText = buttonText, Menu = menu,
        Ghost = ghost, GhostText = ghostText, Card = card, Text = text, Muted = muted,
        Line = line, Head = head, Filter = filter, Mark = mark, Danger = danger, Link = link,
        SwatchDark = swatchDark, SwatchMid = swatchMid, SwatchLight = swatchLight, SwatchInk = swatchInk,
    };
}
