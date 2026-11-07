using System.Globalization;
using System.Windows.Data;

namespace Obozny;

/// <summary>Кількість у форматі стовпця «Кількість» головної таблиці.</summary>
public sealed class QtyFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d) return Ua.FormatQty(d);
        if (value is IFormattable) return Ua.FormatQty(System.Convert.ToDecimal(value, CultureInfo.InvariantCulture));
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

internal static class Ua
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("uk-UA");

    public static Binding Number(string path, string format) => new(path)
    {
        // явний {0:...} — інакше мінус інколи «губиться» у StringFormat
        StringFormat = "{0:" + format + "}",
        ConverterCulture = Culture
    };

    public static string Num(decimal value, string format) =>
        value.ToString(format, Culture);

    public static string DecimalFormat(int places) =>
        places <= 0 ? "#,##0" : "#,##0." + new string('0', places);

    public static int PriceDecimals() => DecimalsOf("UnitPrice", 4);

    public static int QtyDecimals() => DecimalsOf("Quantity", 3);

    public static string FormatPrice(decimal value) => Num(value, DecimalFormat(PriceDecimals()));

    public static string FormatQty(decimal value) => Num(value, DecimalFormat(QtyDecimals()));

    private static int DecimalsOf(string key, int fallback)
    {
        var st = AppStore.Instance.Data.Settings.Columns.FirstOrDefault(c => c.Key == key);
        if (st?.DecimalPlaces is >= 0 and <= 8) return st.DecimalPlaces.Value;
        return fallback;
    }
}
