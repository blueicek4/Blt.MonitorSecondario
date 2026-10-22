namespace Bluetech.Display.Agent.Aspetto;

/// <summary>
/// Uno dei dieci temi del design (iterazione 2, src/tokens/themes.ts): gli stessi nomi e gli stessi valori. Se il design
/// cambia un tema, va cambiato anche qui; i rapporti di contrasto nei commenti li ricontrolla un test.
/// </summary>
public sealed record Palette
{
    public required string Id { get; init; }
    public required string Nome { get; init; }
    /// <summary>Per che sala è pensato: "pizzeria, trattoria".</summary>
    public required string Per { get; init; }
    public required bool Scuro { get; init; }
    /// <summary>Il tema con cui si scambia con giorno e notte automatici; null = nessuno, resta fisso.</summary>
    public required string? Gemello { get; init; }
    /// <summary>Fondo dello schermo.</summary>
    public required string Bg { get; init; }
    /// <summary>Card dello scontrino: può essere rgba sul fondo quando c'è il vetro.</summary>
    public required string Surface { get; init; }
    /// <summary>La card opaca, per i fondi che non possono essere trasparenti.</summary>
    public required string SurfaceSolid { get; init; }
    public required string Line { get; init; }
    public required string Text { get; init; }
    public required string Muted { get; init; }
    /// <summary>Righe stornate, didascalie minori: almeno 4,6:1 sul fondo e sulla card.</summary>
    public required string Faint { get; init; }
    public required string Primary { get; init; }
    public required string OnPrimary { get; init; }
    /// <summary>Sconti e maggiorazioni.</summary>
    public required string Accent { get; init; }
    public required string Secondary { get; init; }
    public required string FontDisplay { get; init; }
    public required string FontText { get; init; }
    public required int Radius { get; init; }
    /// <summary>0–100. Sui temi chiari è 0: al posto del vetro c'è un'ombra.</summary>
    public required int Glass { get; init; }
    /// <summary>Fondo del blocco totale: colore, gradiente o "transparent".</summary>
    public required string TotalFill { get; init; }
    public required string TotalBorder { get; init; }
    public required string TotalText { get; init; }
    public required string Glow { get; init; }
    public required string Glow2 { get; init; }
    /// <summary>"light": la firma chiara, per i fondi scuri; "dark": quella scura, per i fondi chiari.</summary>
    public required string FooterVariant { get; init; }
}

/// <summary>I dieci temi del design, cinque scuri e cinque chiari, e i quattordici font che il monitor ha con sé.</summary>
public static class Palettes
{
    /// <summary>Famiglia → pila CSS, con un ripiego di sistema dalle metriche vicine (come nel design).</summary>
    public static readonly IReadOnlyDictionary<string, string> Fonts = new Dictionary<string, string>
    {
        ["Archivo"] = "'Archivo', 'Arial Narrow', sans-serif",
        ["IBM Plex Sans"] = "'IBM Plex Sans', 'Segoe UI', sans-serif",
        ["Oswald"] = "'Oswald', 'Arial Narrow', sans-serif",
        ["Syne"] = "'Syne', 'Segoe UI', sans-serif",
        ["Space Grotesk"] = "'Space Grotesk', 'Segoe UI', sans-serif",
        ["Cormorant Garamond"] = "'Cormorant Garamond', Georgia, serif",
        ["Jost"] = "'Jost', 'Segoe UI', sans-serif",
        ["Spectral"] = "'Spectral', Georgia, serif",
        ["Karla"] = "'Karla', 'Segoe UI', sans-serif",
        ["Newsreader"] = "'Newsreader', Georgia, serif",
        ["Alegreya Sans"] = "'Alegreya Sans', 'Segoe UI', sans-serif",
        ["Lora"] = "'Lora', Georgia, serif",
        ["Work Sans"] = "'Work Sans', 'Segoe UI', sans-serif",
        ["Outfit"] = "'Outfit', 'Segoe UI', sans-serif",
    };

    static string Font(string family) => Fonts[family];

    /// <summary>La famiglia di una pila CSS del design ("'Archivo', 'Arial Narrow', sans-serif" → Archivo).</summary>
    public static string FamilyOf(string stack) => stack.Split(',')[0].Trim().Trim('\'', '"');

    public static readonly IReadOnlyList<Palette> Themes =
    [
        // BLUETECH: il default. Neutro e tecnologico.
        new()
        {
            Id = "bluetech", Nome = "Bluetech", Per = "il default", Scuro = true, Gemello = "giorno",
            Bg = "#0B1017", Surface = "rgba(255,255,255,0.06)", SurfaceSolid = "#141B26", Line = "rgba(255,255,255,0.1)",
            Text = "#E6EAF2", Muted = "#9AA6BA", Faint = "#7B8596", Primary = "#1F6FEB", OnPrimary = "#FFFFFF",
            Accent = "#3DDC97", Secondary = "#8FA3C0", FontDisplay = Font("Archivo"), FontText = Font("IBM Plex Sans"),
            Radius = 10, Glass = 40,
            TotalFill = "linear-gradient(180deg, rgba(31,111,235,0.24), rgba(31,111,235,0.06))",
            TotalBorder = "rgba(31,111,235,0.5)", TotalText = "#E6EAF2",
            Glow = "#1F6FEB", Glow2 = "#3DDC97", FooterVariant = "light",
        },
        // BRACE: griglia, carne, steakhouse.
        new()
        {
            Id = "brace", Nome = "Brace", Per = "griglia, carne", Scuro = true, Gemello = "forno",
            Bg = "#121010", Surface = "rgba(242,232,224,0.06)", SurfaceSolid = "#1C1817", Line = "rgba(242,232,224,0.13)",
            Text = "#F2E8E0", Muted = "#B5A69C", Faint = "#8C8179", Primary = "#BC4A17", OnPrimary = "#FFFFFF",
            Accent = "#7FC98F", Secondary = "#B5A69C", FontDisplay = Font("Oswald"), FontText = Font("IBM Plex Sans"),
            Radius = 6, Glass = 40,
            TotalFill = "#BC4A17", TotalBorder = "#BC4A17", TotalText = "#FFFFFF",
            Glow = "#BC4A17", Glow2 = "#7FC98F", FooterVariant = "light",
        },
        // NOTTURNO: cocktail bar e locali serali. Nessuna variante giorno.
        new()
        {
            Id = "notturno", Nome = "Notturno", Per = "cocktail bar, locali serali", Scuro = true, Gemello = null,
            Bg = "#0A0616", Surface = "rgba(240,234,255,0.07)", SurfaceSolid = "#16102B", Line = "rgba(240,234,255,0.15)",
            Text = "#F0EAFF", Muted = "#A99BC8", Faint = "#867AA1", Primary = "#C06CFF", OnPrimary = "#0A0616",
            Accent = "#35F5C8", Secondary = "#A99BC8", FontDisplay = Font("Syne"), FontText = Font("Space Grotesk"),
            Radius = 9, Glass = 60,
            TotalFill = "linear-gradient(180deg, rgba(192,108,255,0.3), rgba(53,245,200,0.08))",
            TotalBorder = "rgba(192,108,255,0.6)", TotalText = "#F0EAFF",
            Glow = "#C06CFF", Glow2 = "#35F5C8", FooterVariant = "light",
        },
        // RISERVA: fine dining. Nessun vetro, nessun gradiente.
        new()
        {
            Id = "riserva", Nome = "Riserva", Per = "fine dining", Scuro = true, Gemello = "sale",
            Bg = "#0C0B09", Surface = "rgba(237,230,215,0.05)", SurfaceSolid = "#16140F", Line = "rgba(237,230,215,0.14)",
            Text = "#EDE6D7", Muted = "#A39781", Faint = "#897F6D", Primary = "#C9A227", OnPrimary = "#0C0B09",
            Accent = "#8FB39B", Secondary = "#A39781", FontDisplay = Font("Cormorant Garamond"), FontText = Font("Jost"),
            Radius = 2, Glass = 0,
            TotalFill = "transparent", TotalBorder = "#C9A227", TotalText = "#EDE6D7",
            Glow = "#C9A227", Glow2 = "#8FB39B", FooterVariant = "light",
        },
        // ONDA: cucina di mare.
        new()
        {
            Id = "onda", Nome = "Onda", Per = "cucina di mare", Scuro = true, Gemello = "menta",
            Bg = "#06151A", Surface = "rgba(230,242,242,0.06)", SurfaceSolid = "#0D2129", Line = "rgba(230,242,242,0.13)",
            Text = "#E6F2F2", Muted = "#94B0B4", Faint = "#738C91", Primary = "#15707F", OnPrimary = "#FFFFFF",
            Accent = "#F2B544", Secondary = "#94B0B4", FontDisplay = Font("Spectral"), FontText = Font("Karla"),
            Radius = 16, Glass = 40,
            TotalFill = "#15707F", TotalBorder = "#15707F", TotalText = "#FFFFFF",
            Glow = "#15707F", Glow2 = "#F2B544", FooterVariant = "light",
        },
        // GIORNO: il default chiaro. Ombra al posto del vetro.
        new()
        {
            Id = "giorno", Nome = "Giorno", Per = "il default chiaro", Scuro = false, Gemello = "bluetech",
            Bg = "#F4F6FA", Surface = "#FFFFFF", SurfaceSolid = "#FFFFFF", Line = "rgba(16,21,29,0.1)",
            Text = "#10151D", Muted = "#5A6679", Faint = "#657082", Primary = "#1757C2", OnPrimary = "#FFFFFF",
            Accent = "#0E7A52", Secondary = "#5A6679", FontDisplay = Font("Archivo"), FontText = Font("IBM Plex Sans"),
            Radius = 10, Glass = 0,
            TotalFill = "#1757C2", TotalBorder = "#1757C2", TotalText = "#FFFFFF",
            Glow = "#1757C2", Glow2 = "#0E7A52", FooterVariant = "dark",
        },
        // FORNO: pizzeria e trattoria.
        new()
        {
            Id = "forno", Nome = "Forno", Per = "pizzeria, trattoria", Scuro = false, Gemello = "brace",
            Bg = "#F6EEE2", Surface = "#FFFBF4", SurfaceSolid = "#FFFBF4", Line = "rgba(43,33,26,0.13)",
            Text = "#2B211A", Muted = "#6B5B49", Faint = "#786857", Primary = "#9C3A1F", OnPrimary = "#FFFFFF",
            Accent = "#1E6B4F", Secondary = "#6B5B49", FontDisplay = Font("Newsreader"), FontText = Font("Alegreya Sans"),
            Radius = 6, Glass = 0,
            TotalFill = "#9C3A1F", TotalBorder = "#9C3A1F", TotalText = "#FFFFFF",
            Glow = "#9C3A1F", Glow2 = "#1E6B4F", FooterVariant = "dark",
        },
        // LATTE: caffetteria, forneria, gastronomia. Nessuna variante notte.
        new()
        {
            Id = "latte", Nome = "Latte", Per = "caffetteria, forneria", Scuro = false, Gemello = null,
            Bg = "#FBF7F2", Surface = "#FFFFFF", SurfaceSolid = "#FFFFFF", Line = "rgba(42,33,26,0.1)",
            Text = "#2A211A", Muted = "#6E6155", Faint = "#7B6E63", Primary = "#7A5230", OnPrimary = "#FFFFFF",
            Accent = "#1F6E52", Secondary = "#6E6155", FontDisplay = Font("Lora"), FontText = Font("Work Sans"),
            Radius = 12, Glass = 0,
            TotalFill = "#7A5230", TotalBorder = "#7A5230", TotalText = "#FFFFFF",
            Glow = "#7A5230", Glow2 = "#1F6E52", FooterVariant = "dark",
        },
        // SALE: fine dining di giorno.
        new()
        {
            Id = "sale", Nome = "Sale", Per = "fine dining di giorno", Scuro = false, Gemello = "riserva",
            Bg = "#F7F4EC", Surface = "#FFFDF7", SurfaceSolid = "#FFFDF7", Line = "rgba(26,23,18,0.13)",
            Text = "#1A1712", Muted = "#6E6454", Faint = "#766D5D", Primary = "#8A6D14", OnPrimary = "#FFFFFF",
            Accent = "#3F6B55", Secondary = "#6E6454", FontDisplay = Font("Cormorant Garamond"), FontText = Font("Jost"),
            Radius = 2, Glass = 0,
            TotalFill = "transparent", TotalBorder = "#8A6D14", TotalText = "#1A1712",
            Glow = "#8A6D14", Glow2 = "#3F6B55", FooterVariant = "dark",
        },
        // MENTA: bistrot e bar moderni.
        new()
        {
            Id = "menta", Nome = "Menta", Per = "bistrot, bar moderni", Scuro = false, Gemello = "onda",
            Bg = "#F2F7F4", Surface = "#FFFFFF", SurfaceSolid = "#FFFFFF", Line = "rgba(16,35,28,0.1)",
            Text = "#10231C", Muted = "#51665E", Faint = "#5F736C", Primary = "#116B4F", OnPrimary = "#FFFFFF",
            Accent = "#B4451A", Secondary = "#51665E", FontDisplay = Font("Outfit"), FontText = Font("Karla"),
            Radius = 14, Glass = 0,
            TotalFill = "#116B4F", TotalBorder = "#116B4F", TotalText = "#FFFFFF",
            Glow = "#116B4F", Glow2 = "#B4451A", FooterVariant = "dark",
        },
    ];

    public static readonly IReadOnlyDictionary<string, Palette> All = Themes.ToDictionary(t => t.Id);

    public const string Fallback = "bluetech";

    public static bool IsTheme(string? id) => id != null && All.ContainsKey(id);

    public static Palette Get(string? id) => id != null && All.TryGetValue(id, out var palette) ? palette : All[Fallback];

    /// <summary>
    /// Il tema di adesso (resolveTheme del design): con giorno e notte automatici, di giorno il gemello chiaro di un tema
    /// scuro e di notte quello scuro di un tema chiaro. Senza gemello resta quello.
    /// </summary>
    public static Palette Resolve(string? id, string? mode, bool daytime)
    {
        var palette = Get(id);
        if (mode != "auto" || palette.Gemello == null)
            return palette;
        return palette.Scuro == !daytime ? palette : All[palette.Gemello];
    }

    /// <summary>
    /// I temi dell'iterazione 1 (tema e giorno/notte) e quello che diventano (LEGACY_THEME). Cocktail di giorno non ha
    /// un equivalente: va su Notturno, e la migrazione lo scrive nel log.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Legacy = new Dictionary<string, string>
    {
        ["bluetech-notte"] = "bluetech",
        ["bluetech-giorno"] = "giorno",
        ["trattoria-notte"] = "brace",
        ["trattoria-giorno"] = "forno",
        ["fine-notte"] = "riserva",
        ["fine-giorno"] = "sale",
        ["cocktail-notte"] = "notturno",
        ["cocktail-giorno"] = "notturno",
    };
}
