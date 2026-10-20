using System.Globalization;
using System.Text.RegularExpressions;

namespace Bluetech.Display.Agent.Aspetto;

/// <summary>I colori del locale al posto di quelli del tema (solo con giorno o notte fissi, come nel design).</summary>
public sealed record LocaleColors(
    string Bg,
    string Surface,
    string Text,
    string Muted,
    string Primary,
    string OnPrimary,
    string Accent,
    string Secondary)
{
    public static LocaleColors Of(Palette p) => new(p.Bg, p.Surface, p.Text, p.Muted, p.Primary, p.OnPrimary, p.Accent, p.Secondary);
}

/// <summary>
/// Colori #rgb/#rrggbb e rgb()/rgba(), il contrasto WCAG 2.1 e la palette con i colori del locale: lo stesso calcolo
/// di display/src/theme/color.ts, contrast.ts e theme.ts del design, perché il back office rifiuti esattamente quello
/// che il display rifiuterebbe.
/// </summary>
public static partial class ThemeColors
{
    /// <summary>Sotto questa soglia un tema non si applica (SPEC §6).</summary>
    public const double MinContrast = 4.5;

    public static (int R, int G, int B)? ParseHex(string? color)
    {
        var match = HexPattern().Match(color?.Trim() ?? "");
        if (!match.Success)
            return null;
        var hex = match.Groups[1].Value;
        if (hex.Length == 3)
            hex = string.Concat(hex.Select(c => $"{c}{c}"));
        return (Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..], 16));
    }

    /// <summary>#rrggbb, rgb(r,g,b) o rgba(r,g,b,a): colore e trasparenza.</summary>
    public static ((int R, int G, int B) Rgb, double Alpha)? ParseColor(string? color)
    {
        if (ParseHex(color) is { } hex)
            return (hex, 1);
        var match = RgbaPattern().Match(color?.Trim() ?? "");
        if (!match.Success)
            return null;
        var rgb = (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
        var alpha = match.Groups[4].Success ? double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) : 1;
        if (rgb.Item1 > 255 || rgb.Item2 > 255 || rgb.Item3 > 255 || alpha > 1)
            return null;
        return (rgb, alpha);
    }

    public static string ToHex(double r, double g, double b) =>
        "#" + string.Concat(new[] { r, g, b }.Select(c => ((int)Math.Round(c, MidpointRounding.AwayFromZero)).ToString("X2", CultureInfo.InvariantCulture)));

    /// <summary><paramref name="top"/> (anche trasparente) steso su <paramref name="bottom"/> opaco: il colore che si vede.</summary>
    public static string? Composite(string top, string bottom)
    {
        if (ParseColor(top) is not var (over, alpha) || ParseHex(bottom) is not { } under)
            return null;
        return ToHex(over.R * alpha + under.R * (1 - alpha), over.G * alpha + under.G * (1 - alpha), over.B * alpha + under.B * (1 - alpha));
    }

    /// <summary><paramref name="a"/> mescolato con <paramref name="b"/>: 0 dà a, 1 dà b.</summary>
    public static string? Mix(string a, string b, double amount)
    {
        if (ParseHex(a) is not { } from || ParseHex(b) is not { } to)
            return null;
        return ToHex(from.R + (to.R - from.R) * amount, from.G + (to.G - from.G) * amount, from.B + (to.B - from.B) * amount);
    }

    public static double? AlphaOf(string color) => ParseColor(color)?.Alpha;

    public static string? WithAlpha(string color, double alpha) =>
        ParseHex(color) is { } rgb ? $"rgba({rgb.R},{rgb.G},{rgb.B},{alpha.ToString(CultureInfo.InvariantCulture)})" : null;

    /// <summary>
    /// Sostituisce i colori dentro <paramref name="value"/> (anche in un gradiente) in un solo passaggio: #rrggbb diventa il
    /// nuovo #rrggbb, rgba(r,g,b,a) prende il nuovo colore e tiene la sua trasparenza.
    /// </summary>
    public static string Recolor(string value, IReadOnlyDictionary<(int, int, int), (int R, int G, int B)> map) =>
        RecolorPattern().Replace(value, match =>
        {
            var prefix = match.Groups[1];
            (int, int, int)? rgb = prefix.Success
                ? (int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture))
                : ParseHex(match.Value);
            if (rgb is not { } key || !map.TryGetValue(key, out var target))
                return match.Value;
            return prefix.Success ? $"{prefix.Value}{target.R},{target.G},{target.B}" : ToHex(target.R, target.G, target.B);
        });

    /// <summary>Rapporto di contrasto WCAG 2.1, solo tra colori opachi #rgb/#rrggbb (null per gli altri).</summary>
    public static double? Contrast(string foreground, string background)
    {
        if (Luminance(foreground) is not { } a || Luminance(background) is not { } b)
            return null;
        var (light, dark) = a > b ? (a, b) : (b, a);
        return (light + 0.05) / (dark + 0.05);
    }

    static double? Luminance(string color)
    {
        if (ParseHex(color) is not { } rgb)
            return null;
        static double Channel(int value)
        {
            var c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(rgb.R) + 0.7152 * Channel(rgb.G) + 0.0722 * Channel(rgb.B);
    }

    /// <summary>In quest'ordine, se due colori del tema coincidono vince il primo (come nel design).</summary>
    static readonly (Func<LocaleColors, string> Locale, Func<Palette, string> Base)[] RecolorOrder =
    [
        (c => c.Primary, p => p.Primary),
        (c => c.OnPrimary, p => p.OnPrimary),
        (c => c.Text, p => p.Text),
        (c => c.Secondary, p => p.Secondary),
        (c => c.Accent, p => p.Accent),
        (c => c.Bg, p => p.Bg),
        (c => c.Muted, p => p.Muted),
    ];

    /// <summary>
    /// I colori del locale sopra il tema. Quelli che il tema ricava dagli altri (card opaca, filetti, testo tenue,
    /// blocco del totale, bagliori) si ricalcolano: altrimenti il testo chiaro del totale di un tema scuro resterebbe
    /// su un fondo chiaro scelto dal locale.
    /// </summary>
    public static Palette WithLocaleColors(Palette basePalette, LocaleColors colors)
    {
        var map = new Dictionary<(int, int, int), (int R, int G, int B)>();
        foreach (var (locale, original) in RecolorOrder)
        {
            if (ParseHex(original(basePalette)) is { } from && ParseHex(locale(colors)) is { } to
                && locale(colors) != original(basePalette) && !map.ContainsKey(from))
                map[from] = to;
        }
        return basePalette with
        {
            Bg = colors.Bg,
            Surface = colors.Surface,
            Text = colors.Text,
            Muted = colors.Muted,
            Primary = colors.Primary,
            OnPrimary = colors.OnPrimary,
            Accent = colors.Accent,
            Secondary = colors.Secondary,
            SurfaceSolid = colors.Surface == basePalette.Surface && colors.Bg == basePalette.Bg
                ? basePalette.SurfaceSolid
                : Composite(colors.Surface, colors.Bg) ?? Recolor(basePalette.SurfaceSolid, map),
            Line = colors.Text == basePalette.Text
                ? basePalette.Line
                : WithAlpha(colors.Text, AlphaOf(basePalette.Line) ?? 0.12) ?? Recolor(basePalette.Line, map),
            Faint = colors.Muted == basePalette.Muted && colors.Bg == basePalette.Bg
                ? basePalette.Faint
                : Mix(colors.Muted, colors.Bg, 0.35) ?? Recolor(basePalette.Faint, map),
            TotalFill = Recolor(basePalette.TotalFill, map),
            TotalBorder = Recolor(basePalette.TotalBorder, map),
            TotalText = Recolor(basePalette.TotalText, map),
            Glow = Recolor(basePalette.Glow, map),
            Glow2 = Recolor(basePalette.Glow2, map),
        };
    }

    /// <summary>Una coppia di colori che il cliente deve poter leggere.</summary>
    public sealed record Pair(string Label, string Foreground, string Background, double? Ratio)
    {
        public bool Ok => Ratio >= MinContrast;
    }

    /// <summary>
    /// Le coppie critiche: testo sul fondo e sulla card, testo sopra il primario, cifra del totale sul suo blocco (se il
    /// blocco è trasparente o sfumato, sulla card che gli sta sotto).
    /// </summary>
    public static IReadOnlyList<Pair> Pairs(Palette palette)
    {
        var totalBehind = ParseHex(palette.TotalFill) != null ? palette.TotalFill : palette.SurfaceSolid;
        return
        [
            new("il testo sul fondo", palette.Text, palette.Bg, Contrast(palette.Text, palette.Bg)),
            new("il testo sulla card del conto", palette.Text, palette.SurfaceSolid, Contrast(palette.Text, palette.SurfaceSolid)),
            new("il testo sopra il colore primario", palette.OnPrimary, palette.Primary, Contrast(palette.OnPrimary, palette.Primary)),
            new("la cifra del totale", palette.TotalText, totalBehind, Contrast(palette.TotalText, totalBehind)),
        ];
    }

    public static string Ratio(double ratio) => ratio.ToString("0.0", CultureInfo.GetCultureInfo("it-IT")) + ":1";

    /// <summary>
    /// I colori più vicini a quelli del locale che passano il controllo: cambia solo la luminosità del testo (o del testo
    /// sopra il primario), lasciando fondo e primario, che di solito sono i colori del marchio. Null se va già bene o
    /// se un colore non è valido.
    /// </summary>
    public static LocaleColors? Suggest(Palette basePalette, LocaleColors colors)
    {
        if (new[] { colors.Bg, colors.Text, colors.Muted, colors.Primary, colors.OnPrimary, colors.Accent, colors.Secondary }.Any(c => ParseHex(c) == null)
            || ParseColor(colors.Surface) == null)
            return null;
        var current = colors;
        for (var round = 0; round < 4; round++)
        {
            var palette = WithLocaleColors(basePalette, current);
            var failing = Pairs(palette).Where(p => !p.Ok).ToList();
            if (failing.Count == 0)
                return current == colors ? null : current;
            var pair = failing[0];
            if (Same(pair.Foreground, palette.OnPrimary) && Same(pair.Background, palette.Primary))
                current = current with { OnPrimary = Nearest(current.OnPrimary, palette.Primary) ?? current.OnPrimary };
            else
                current = current with { Text = Nearest(current.Text, palette.Bg, palette.SurfaceSolid) ?? current.Text };
        }
        return Pairs(WithLocaleColors(basePalette, current)).All(p => p.Ok) ? current : null;
    }

    /// <summary>Lo stesso colore scritto in due modi (#abc, #AABBCC, #aabbcc).</summary>
    static bool Same(string a, string b) => ParseHex(a) is { } x && ParseHex(b) is { } y && x == y;

    /// <summary>Il colore con la stessa tinta e la luminosità più vicina che si legge su tutti i fondi dati.</summary>
    static string? Nearest(string color, params string[] backgrounds)
    {
        if (ParseHex(color) is not { } rgb)
            return null;
        var (h, s, l) = ToHsl(rgb.R, rgb.G, rgb.B);
        bool Readable(string candidate) => backgrounds.All(b => Contrast(candidate, b) >= MinContrast + 0.05);   // un filo di margine
        for (var step = 1; step <= 100; step++)
        {
            foreach (var next in new[] { l - step / 100.0, l + step / 100.0 })
            {
                if (next is < 0 or > 1)
                    continue;
                var candidate = FromHsl(h, s, next);
                if (Readable(candidate))
                    return candidate;
            }
        }
        return null;
    }

    static (double H, double S, double L) ToHsl(int red, int green, int blue)
    {
        double r = red / 255.0, g = green / 255.0, b = blue / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max == min)
            return (0, 0, l);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h / 6, s, l);
    }

    static string FromHsl(double h, double s, double l)
    {
        if (s == 0)
            return ToHex(l * 255, l * 255, l * 255);
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            return t < 1 / 6.0 ? p + (q - p) * 6 * t : t < 1 / 2.0 ? q : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6 : p;
        }
        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        return ToHex(Hue(p, q, h + 1 / 3.0) * 255, Hue(p, q, h) * 255, Hue(p, q, h - 1 / 3.0) * 255);
    }

    [GeneratedRegex("^#([0-9a-f]{3}|[0-9a-f]{6})$", RegexOptions.IgnoreCase)]
    private static partial Regex HexPattern();

    [GeneratedRegex(@"^rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*(?:,\s*(\d*\.?\d+)\s*)?\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RgbaPattern();

    [GeneratedRegex(@"#[0-9a-f]{6}\b|(rgba?\(\s*)(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex RecolorPattern();
}
