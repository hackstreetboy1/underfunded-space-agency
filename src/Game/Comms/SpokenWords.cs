using System.Globalization;
using System.Text;

namespace Usa.Game.Comms;

/// <summary>
/// Numbers, units and labels the way a crew member says them on the radio: <c>140 °C</c> is "one-forty degrees",
/// <c>0.42 kW</c> "point four two kilowatts", <c>tandem/oams/thruster_08 condition</c> "thruster 8 condition".
/// </summary>
public static class SpokenWords
{
    private static readonly string[] Ones =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
    ];

    private static readonly string[] Tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    private static readonly Dictionary<string, string> Units = new(StringComparer.Ordinal)
    {
        ["°C"] = "degrees",
        ["K"] = "kelvin",
        ["°/s"] = "degrees a second",
        ["°"] = "degrees",
        ["kg"] = "kilos",
        ["kg/h"] = "kilos an hour",
        ["kW"] = "kilowatts",
        ["kWh"] = "kilowatt-hours",
        ["W"] = "watts",
        ["V"] = "volts",
        ["A"] = "amps",
        ["kPa"] = "kilopascals",
        ["mmHg"] = "millimetres",
        ["%"] = "percent",
        ["L"] = "litres",
        ["kcal"] = "kilocalories",
        ["g"] = "g",
        ["m/s"] = "metres a second",
        ["km"] = "kilometres",
        ["m"] = "metres",
        ["s"] = "seconds",
        ["min"] = "minutes",
        ["N"] = "newtons",
    };

    /// <summary>A whole count in words ("sixteen"); digits above a million.</summary>
    public static string Count(int count) => count is >= 0 and < 1_000_000 ? Integer(count, radio: false) : count.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A formatted value with its unit in words: <c>140 °C</c> → "one-forty degrees", <c>+3.2 °/s</c> → "plus three point two
    /// degrees a second", <c>15%</c> → "fifteen percent". Text that is not a number (a qualitative reading) is returned as is.
    /// </summary>
    public static string Value(string formatted)
    {
        ArgumentNullException.ThrowIfNull(formatted);
        var text = formatted.Trim();
        var end = 0;
        while (end < text.Length && (char.IsDigit(text[end]) || text[end] is '.' or '+' or '-' or '−')) end++;
        if (end == 0 || !text[..end].Any(char.IsDigit)) return text;
        var number = Number(text[..end]);
        if (number is null) return text;
        var unit = text[end..].Trim();
        if (unit.Length == 0) return number;
        return $"{number} {Units.GetValueOrDefault(unit, unit)}";
    }

    /// <summary>
    /// A catalog label for speech: a component id path keeps its last part, underscores become spaces and leading zeros go
    /// (<c>tandem/oams/thruster_08 condition</c> → "thruster 8 condition").
    /// </summary>
    public static string Label(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        var words = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var spoken = new StringBuilder(label.Length);
        foreach (var raw in words)
        {
            var word = raw;
            var slash = word.LastIndexOf('/');
            if (slash >= 0) word = word[(slash + 1)..];
            foreach (var part in word.Split('_', StringSplitOptions.RemoveEmptyEntries))
            {
                if (spoken.Length > 0) spoken.Append(' ');
                spoken.Append(part.All(char.IsDigit) ? part.TrimStart('0') is { Length: > 0 } trimmed ? trimmed : "0" : part);
            }
        }
        return spoken.ToString();
    }

    /// <summary>"one-forty", "point four", "minus twelve point five"; null when <paramref name="text"/> is not a number.</summary>
    private static string? Number(string text)
    {
        var sign = "";
        if (text[0] is '-' or '−')
        {
            sign = "minus ";
            text = text[1..];
        }
        else if (text[0] == '+')
        {
            sign = "plus ";
            text = text[1..];
        }
        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var whole = dot < 0 ? text : text[..dot];
        var fraction = dot < 0 ? "" : text[(dot + 1)..];
        if (!whole.All(char.IsDigit) || !fraction.All(char.IsDigit) || whole.Length + fraction.Length == 0) return null;
        if (!long.TryParse(whole.Length == 0 ? "0" : whole, NumberStyles.None, CultureInfo.InvariantCulture, out var integer) || integer >= 1_000_000)
            return null;

        var words = new StringBuilder(sign);
        if (integer > 0 || fraction.Length == 0) words.Append(Integer((int)integer, radio: true));
        if (fraction.Length > 0 && fraction.Any(c => c != '0'))
        {
            if (integer > 0) words.Append(' ');
            words.Append("point");
            foreach (var digit in fraction.TrimEnd('0')) words.Append(' ').Append(Ones[digit - '0']);
        }
        else if (integer == 0)
        {
            words.Append(Ones[0]);
        }
        return words.ToString();
    }

    /// <param name="radio">Read 100–9999 in pairs, as on the radio ("one-forty", "twelve-fifty", "one-oh-five").</param>
    private static string Integer(int value, bool radio)
    {
        if (value < 20) return Ones[value];
        if (value < 100) return value % 10 == 0 ? Tens[value / 10] : $"{Tens[value / 10]}-{Ones[value % 10]}";
        if (value % 1000 == 0 && value < 1_000_000) return $"{Integer(value / 1000, radio: false)} thousand";
        if (value < 10_000 && radio)
        {
            var high = Integer(value / 100, radio: false);
            var low = value % 100;
            if (low == 0) return $"{high} hundred";
            return low < 10 ? $"{high}-oh-{Ones[low]}" : $"{high}-{Integer(low, radio: false)}";
        }
        if (value < 1000)
        {
            var rest = value % 100;
            return rest == 0 ? $"{Ones[value / 100]} hundred" : $"{Ones[value / 100]} hundred and {Integer(rest, radio: false)}";
        }
        var thousands = Integer(value / 1000, radio: false);
        var remainder = value % 1000;
        return $"{thousands} thousand {Integer(remainder, radio: false)}";
    }
}
