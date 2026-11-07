using System.Windows;
using System.Windows.Controls;

namespace Obozny;

public partial class CalculatorWindow : Window
{
    private decimal _value;
    private decimal _acc;
    private string? _op;
    private bool _fresh = true;

    public decimal Result { get; private set; }

    public CalculatorWindow(decimal start)
    {
        InitializeComponent();
        _value = start;
        Display.Text = Format(start);
        _fresh = true;
    }

    private void Key(object sender, RoutedEventArgs e)
    {
        var tag = (string)((Button)sender).Tag;
        if (tag.Length == 1 && (char.IsDigit(tag[0]) || tag is "." or ","))
        {
            var sep = ",";
            if (_fresh) { Display.Text = tag is "." or "," ? "0," : tag; _fresh = false; }
            else if (tag is not ("." or ",") || (!Display.Text.Contains(',') && !Display.Text.Contains('.')))
                Display.Text += tag is "." or "," ? sep : tag;
            decimal.TryParse(Display.Text, System.Globalization.NumberStyles.Number, Ua.Culture, out _value);
            return;
        }

        if (tag == "C") { _value = 0; _acc = 0; _op = null; Display.Text = "0"; _fresh = true; return; }
        if (tag == "B")
        {
            if (Display.Text.Length <= 1) { Display.Text = "0"; _value = 0; _fresh = true; }
            else
            {
                Display.Text = Display.Text[..^1];
                decimal.TryParse(Display.Text, System.Globalization.NumberStyles.Number, Ua.Culture, out _value);
            }
            return;
        }

        ApplyPending();
        if (tag == "=")
        {
            Display.Text = Format(_value);
            _op = null;
            _fresh = true;
            return;
        }
        _acc = _value;
        _op = tag;
        _fresh = true;
    }

    private void ApplyPending()
    {
        if (_op == null) return;
        _value = _op switch
        {
            "+" => _acc + _value,
            "-" => _acc - _value,
            "*" => _acc * _value,
            "/" => _value == 0 ? _acc : _acc / _value,
            _ => _value
        };
        Display.Text = Format(_value);
    }

    private void Ok(object sender, RoutedEventArgs e)
    {
        ApplyPending();
        Result = Math.Round(_value, 3);
        if (Result < 0) Result = 0;
        DialogResult = true;
    }

    private static string Format(decimal v) => Ua.Num(v, "#,##0.###");
}
