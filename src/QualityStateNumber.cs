using System.Globalization;
using System.Text.RegularExpressions;

namespace Obozny;

internal static class QualityStateNumber
{
    // 1125 "БАТОН" №343/3/6/15281
    // 180, "ВОРОН"№343/3/2/3201
    private static readonly Regex QuotedPattern = new(
        @"^\s*(?<num>\d+(?:\s*/\s*\d+)*)\s*[,;]?\s*[""«„“]\s*(?<name>.+?)\s*[""»“”]\s*№?\s*(?<reg>\d+(?:\s*[./]\s*\d+)*)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // 2426/1284, Штурм, 343/3/6/18273
    // 2426/1358, Смерека 2.0, 343/3/6/18287
    private static readonly Regex CommaPattern = new(
        @"^\s*(?<num>\d+(?:\s*/\s*\d+)*)\s*,\s*(?<name>[^,]+?)\s*,\s*№?\s*(?<reg>\d+(?:\s*[./]\s*\d+)*)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParse(string? raw, out string actNumber, out string strongpointName, out string regNumber)
    {
        actNumber = "";
        strongpointName = "";
        regNumber = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var text = raw.Trim();
        var m = QuotedPattern.Match(text);
        if (!m.Success) m = CommaPattern.Match(text);
        if (!m.Success) return false;

        actNumber = CompactPath(m.Groups["num"].Value);
        strongpointName = Caps(StripQuotes(m.Groups["name"].Value));
        regNumber = CompactPath(m.Groups["reg"].Value).TrimStart('№', 'N', 'n');
        return actNumber.Length > 0 && strongpointName.Length > 0 && regNumber.Length > 0;
    }

    public static bool LooksLike(string? raw) => TryParse(raw, out _, out _, out _);

    public static void ApplyTo(Document doc, string? rawNumber = null)
    {
        var source = rawNumber ?? doc.Number;
        if (!TryParse(source, out var act, out var name, out var reg)) return;
        doc.Type = DocType.QualityState;
        doc.Number = act;
        doc.StrongpointName = Caps(name);
        doc.StrongpointRegNumber = reg;
        doc.CommissioningDate ??= doc.Date;
    }

    public static string Caps(string? name)
    {
        var t = (name ?? "").Trim();
        return t.Length == 0 ? "" : Uk.TextInfo.ToUpper(t);
    }

    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("uk-UA");

    private static string CompactPath(string s) =>
        Regex.Replace((s ?? "").Trim(), @"\s+", "");

    private static string StripQuotes(string s)
    {
        var t = (s ?? "").Trim();
        foreach (var q in new[] { '"', '«', '»', '„', '“', '”', '\'', '`' })
            t = t.Replace(q.ToString(), "");
        while (t.Contains("  ")) t = t.Replace("  ", " ");
        return t.Trim();
    }
}
