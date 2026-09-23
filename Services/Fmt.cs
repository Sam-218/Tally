using System.Globalization;

namespace Tally.Services;

/// <summary>Number and money formatting (always German, regardless of Windows settings).</summary>
public static class Fmt
{
    public static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>12,50 €</summary>
    public static string Money(decimal value) => value.ToString("C2", De);

    /// <summary>Two decimal places without currency, e.g. "38,97" (for input fields).</summary>
    public static string Fixed2(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("F2", De);

    /// <summary>Quantity: whole numbers with no decimals ("24"), otherwise up to 2 digits ("2,5").</summary>
    public static string Qty(decimal value) => value.ToString("0.##", De);

    /// <summary>
    /// Reads a number that may be written with a comma OR a dot ("1,5" / "1.5" / "1.234,50" / "€ 3,20").
    /// A single dot counts as the decimal separator (as in the old input fields).
    /// </summary>
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Replace("€", "").Replace(" ", "").Replace(" ", "").Trim();
        if (s.Length == 0) return false;

        var lastComma = s.LastIndexOf(',');
        var lastDot = s.LastIndexOf('.');

        if (lastComma >= 0 && lastDot >= 0)
        {
            // both present: the last character is the decimal separator, the other is the thousands separator
            if (lastComma > lastDot) s = s.Replace(".", "").Replace(',', '.');
            else s = s.Replace(",", "");
        }
        else if (lastComma >= 0)
        {
            if (s.IndexOf(',') != lastComma) s = s.Replace(",", "");   // "1,234,567" -> thousands separator
            else s = s.Replace(',', '.');
        }
        else if (lastDot >= 0 && s.IndexOf('.') != lastDot)
        {
            s = s.Replace(".", "");                                    // "1.234.567" -> thousands separator
        }

        return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);
    }

    public static decimal ParseOrZero(string? text) => TryParse(text, out var v) ? v : 0m;
}
