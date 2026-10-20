using System.Globalization;
using System.Text.RegularExpressions;

namespace Bluetech.Display.Agent.Cassa;

/// <summary>Importi e quantità come li scrive la cassa ("1.234,50", "-0,41", "2", "1,5"). Mai double.</summary>
public static partial class Money
{
    [GeneratedRegex(@"^(?<sign>-)?(?<int>\d{1,3}(?:\.\d{3})+|\d+),(?<dec>\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"^(?<int>\d+)(?:,(?<dec>\d+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex QuantityPattern();

    /// <summary>"1.234,50" → 123450 centesimi.</summary>
    public static bool TryParseCents(string text, out long cents)
    {
        cents = 0;
        var match = AmountPattern().Match(text.Trim());
        if (!match.Success)
            return false;
        var whole = long.Parse(match.Groups["int"].Value.Replace(".", ""), CultureInfo.InvariantCulture);
        cents = whole * 100 + int.Parse(match.Groups["dec"].Value, CultureInfo.InvariantCulture);
        if (match.Groups["sign"].Success)
            cents = -cents;
        return true;
    }

    /// <summary>"2" → 2, "1,5" → 1.5.</summary>
    public static bool TryParseQuantity(string text, out decimal quantity)
    {
        quantity = 0;
        var match = QuantityPattern().Match(text.Trim());
        if (!match.Success)
            return false;
        var value = match.Groups["dec"].Success
            ? $"{match.Groups["int"].Value}.{match.Groups["dec"].Value}"
            : match.Groups["int"].Value;
        return decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out quantity);
    }
}
