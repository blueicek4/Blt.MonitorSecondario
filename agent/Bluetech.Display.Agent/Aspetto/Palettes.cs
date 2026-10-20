namespace Bluetech.Display.Agent.Aspetto;

/// <summary>
/// Una delle otto palette del design: gli stessi nomi e gli stessi valori di display/src/tokens/themes.ts (ramo del
/// design). Se il design cambia una palette, va cambiata anche qui.
/// </summary>
public sealed record Palette
{
    /// <summary>Fondo dello schermo.</summary>
    public required string Bg { get; init; }
    /// <summary>Card dello scontrino: può essere rgba sul fondo quando c'è il vetro.</summary>
    public required string Surface { get; init; }
    /// <summary>La card opaca, per i fondi che non possono essere trasparenti.</summary>
    public required string SurfaceSolid { get; init; }
    public required string Line { get; init; }
    public required string Text { get; init; }
    public required string Muted { get; init; }
    public required string Faint { get; init; }
    public required string Primary { get; init; }
    public required string OnPrimary { get; init; }
    /// <summary>Sconti e maggiorazioni.</summary>
    public required string Accent { get; init; }
    public required string Secondary { get; init; }
    public required string FontDisplay { get; init; }
    public required string FontText { get; init; }
    public required int Radius { get; init; }
    public required int Glass { get; init; }
    /// <summary>Fondo del blocco totale: colore, gradiente o "transparent".</summary>
    public required string TotalFill { get; init; }
    public required string TotalBorder { get; init; }
    public required string TotalText { get; init; }
    public required string Glow { get; init; }
    public required string Glow2 { get; init; }
    /// <summary>"light" (footer per fondi chiari) o "dark".</summary>
    public required string FooterVariant { get; init; }
}

/// <summary>I quattro temi del design, ognuno di notte e di giorno, e gli otto font che il monitor ha con sé.</summary>
public static class Palettes
{
    public sealed record ThemeInfo(string Id, string Name, string Character);

    public static readonly IReadOnlyList<ThemeInfo> Themes =
    [
        new("bluetech", "Bluetech", "neutro, moderno, tecnologico"),
        new("trattoria", "Trattoria", "caldo, carta e terracotta, serif elegante"),
        new("fine", "Fine dining", "oro, minimale, molto respiro, angoli vivi"),
        new("cocktail", "Cocktail bar", "neon, gradienti vivi"),
    ];

    public static readonly IReadOnlyList<string> Modes = ["notte", "giorno"];

    /// <summary>Famiglia → pila CSS, con un ripiego di sistema dalle metriche vicine (come nel design).</summary>
    public static readonly IReadOnlyDictionary<string, string> Fonts = new Dictionary<string, string>
    {
        ["Archivo"] = "'Archivo', 'Arial Narrow', sans-serif",
        ["IBM Plex Sans"] = "'IBM Plex Sans', 'Segoe UI', sans-serif",
        ["Newsreader"] = "'Newsreader', Georgia, serif",
        ["Alegreya Sans"] = "'Alegreya Sans', 'Segoe UI', sans-serif",
        ["Cormorant Garamond"] = "'Cormorant Garamond', Georgia, serif",
        ["Jost"] = "'Jost', 'Segoe UI', sans-serif",
        ["Syne"] = "'Syne', sans-serif",
        ["Space Grotesk"] = "'Space Grotesk', 'Segoe UI', sans-serif",
    };

    static string Font(string family) => Fonts[family];

    /// <summary>La famiglia di una pila CSS del design ("'Archivo', 'Arial Narrow', sans-serif" → Archivo).</summary>
    public static string FamilyOf(string stack) => stack.Split(',')[0].Trim().Trim('\'', '"');

    public static readonly IReadOnlyDictionary<string, Palette> All = new Dictionary<string, Palette>
    {
        // BLUETECH: neutro, moderno, tecnologico. Il default.
        ["bluetech-notte"] = new()
        {
            Bg = "#0B1017", Surface = "rgba(255,255,255,0.055)", SurfaceSolid = "#141B26", Line = "rgba(255,255,255,0.09)",
            Text = "#E6EAF2", Muted = "#9AA6BA", Faint = "#6F7E95", Primary = "#1F6FEB", OnPrimary = "#FFFFFF",
            Accent = "#3DDC97", Secondary = "#8FA3C0", FontDisplay = Font("Archivo"), FontText = Font("IBM Plex Sans"),
            Radius = 24, Glass = 50,
            TotalFill = "linear-gradient(180deg, rgba(31,111,235,0.20), rgba(31,111,235,0.05))",
            TotalBorder = "rgba(31,111,235,0.42)", TotalText = "#E6EAF2",
            Glow = "rgba(31,111,235,0.30)", Glow2 = "rgba(61,220,151,0.14)", FooterVariant = "dark",
        },
        ["bluetech-giorno"] = new()
        {
            Bg = "#F4F6FA", Surface = "#FFFFFF", SurfaceSolid = "#FFFFFF", Line = "rgba(16,21,29,0.10)",
            Text = "#10151D", Muted = "#5A6679", Faint = "#7C8798", Primary = "#1757C2", OnPrimary = "#FFFFFF",
            Accent = "#0E7A52", Secondary = "#7C8798", FontDisplay = Font("Archivo"), FontText = Font("IBM Plex Sans"),
            Radius = 24, Glass = 0,
            TotalFill = "#1757C2", TotalBorder = "#1757C2", TotalText = "#FFFFFF",
            Glow = "rgba(23,87,194,0.14)", Glow2 = "rgba(14,122,82,0.10)", FooterVariant = "light",
        },

        // TRATTORIA: caldo, carta e terracotta, serif elegante.
        ["trattoria-notte"] = new()
        {
            Bg = "#1C1512", Surface = "rgba(242,231,217,0.06)", SurfaceSolid = "#261D18", Line = "rgba(242,231,217,0.13)",
            Text = "#F2E7D9", Muted = "#B8A694", Faint = "#8E7D6A", Primary = "#D4714A", OnPrimary = "#1C1512",
            Accent = "#7FC4A3", Secondary = "#8E7D6A", FontDisplay = Font("Newsreader"), FontText = Font("Alegreya Sans"),
            Radius = 14, Glass = 30,
            TotalFill = "#D4714A", TotalBorder = "#D4714A", TotalText = "#1C1512",
            Glow = "rgba(212,113,74,0.22)", Glow2 = "rgba(127,196,163,0.10)", FooterVariant = "dark",
        },
        ["trattoria-giorno"] = new()
        {
            Bg = "#F6EEE2", Surface = "#FFFBF4", SurfaceSolid = "#FFFBF4", Line = "rgba(43,33,26,0.12)",
            Text = "#2B211A", Muted = "#6B5B49", Faint = "#8D7F6E", Primary = "#9C3A1F", OnPrimary = "#FFFFFF",
            Accent = "#1E6B4F", Secondary = "#7A6A58", FontDisplay = Font("Newsreader"), FontText = Font("Alegreya Sans"),
            Radius = 14, Glass = 0,
            TotalFill = "#9C3A1F", TotalBorder = "#9C3A1F", TotalText = "#FFFFFF",
            Glow = "rgba(201,138,94,0.30)", Glow2 = "rgba(30,107,79,0.08)", FooterVariant = "light",
        },

        // FINE DINING: oro, minimale, molto respiro, angoli vivi.
        ["fine-notte"] = new()
        {
            Bg = "#0C0B09", Surface = "rgba(237,230,215,0.05)", SurfaceSolid = "#16140F", Line = "rgba(237,230,215,0.13)",
            Text = "#EDE6D7", Muted = "#A39781", Faint = "#7A7061", Primary = "#C9A227", OnPrimary = "#141210",
            Accent = "#8FB39B", Secondary = "#8A8070", FontDisplay = Font("Cormorant Garamond"), FontText = Font("Jost"),
            Radius = 4, Glass = 0,
            TotalFill = "transparent", TotalBorder = "#C9A227", TotalText = "#EDE6D7",
            Glow = "rgba(201,162,39,0.18)", Glow2 = "rgba(143,179,155,0.08)", FooterVariant = "dark",
        },
        ["fine-giorno"] = new()
        {
            Bg = "#F7F4EC", Surface = "#FFFDF7", SurfaceSolid = "#FFFDF7", Line = "rgba(26,23,18,0.12)",
            Text = "#1A1712", Muted = "#6E6454", Faint = "#8A8070", Primary = "#8A6D14", OnPrimary = "#FFFFFF",
            Accent = "#3F6B55", Secondary = "#8A8070", FontDisplay = Font("Cormorant Garamond"), FontText = Font("Jost"),
            Radius = 4, Glass = 0,
            TotalFill = "transparent", TotalBorder = "#8A6D14", TotalText = "#1A1712",
            Glow = "rgba(138,109,20,0.14)", Glow2 = "rgba(63,107,85,0.08)", FooterVariant = "light",
        },

        // COCKTAIL BAR: neon, gradienti vivi. Il più aggressivo dei quattro.
        ["cocktail-notte"] = new()
        {
            Bg = "#0A0616", Surface = "rgba(240,234,255,0.06)", SurfaceSolid = "#16102B", Line = "rgba(240,234,255,0.14)",
            Text = "#F0EAFF", Muted = "#A99BC8", Faint = "#7E6FA0", Primary = "#C06CFF", OnPrimary = "#0A0616",
            Accent = "#35F5C8", Secondary = "#FF4D9D", FontDisplay = Font("Syne"), FontText = Font("Space Grotesk"),
            Radius = 20, Glass = 70,
            TotalFill = "linear-gradient(180deg, rgba(192,108,255,0.26), rgba(53,245,200,0.08))",
            TotalBorder = "rgba(192,108,255,0.55)", TotalText = "#F0EAFF",
            Glow = "rgba(192,108,255,0.30)", Glow2 = "rgba(53,245,200,0.14)", FooterVariant = "dark",
        },
        ["cocktail-giorno"] = new()
        {
            Bg = "#F6F2FD", Surface = "#FFFFFF", SurfaceSolid = "#FFFFFF", Line = "rgba(23,15,35,0.10)",
            Text = "#170F23", Muted = "#5C5270", Faint = "#7A6E92", Primary = "#7A2BC4", OnPrimary = "#FFFFFF",
            Accent = "#0C7A6B", Secondary = "#B0246B", FontDisplay = Font("Syne"), FontText = Font("Space Grotesk"),
            Radius = 20, Glass = 0,
            TotalFill = "#7A2BC4", TotalBorder = "#7A2BC4", TotalText = "#FFFFFF",
            Glow = "rgba(122,43,196,0.14)", Glow2 = "rgba(12,122,107,0.08)", FooterVariant = "light",
        },
    };

    public const string FallbackKey = "bluetech-notte";

    public static bool IsTheme(string? id) => Themes.Any(t => t.Id == id);

    public static Palette Get(string theme, string mode) =>
        All.TryGetValue($"{theme}-{mode}", out var palette) ? palette : All[FallbackKey];
}
