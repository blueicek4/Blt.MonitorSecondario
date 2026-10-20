using System.Text.Json.Nodes;
using Bluetech.Display.Agent.Cassa;
using Bluetech.Display.Agent.Impostazioni;
using Bluetech.Display.Agent.Vendita;
using Net.Codecrete.QrCodeGenerator;

namespace Bluetech.Display.Agent.Aspetto;

/// <summary>
/// Il messaggio <c>cfg</c> per i display di una cassa: i token del tema di protocol.ts del design (<c>theme</c>, <c>ads</c>,
/// <c>ticker</c>) più quello che il design non ha ancora (design/ALLINEAMENTO.md §8): il tema di partenza, le ore del
/// giorno, il locale, le scritte, la scheda accanto al conto e il QR del Grazie. Le promozioni arrivano già scelte per
/// la cassa, il giorno e l'ora: il display non decide niente, l'Agent rimanda il messaggio quando cambia qualcosa.
/// </summary>
public static class DisplayConfig
{
    /// <param name="ignoreSchedule">Per l'anteprima: tutte le promozioni attive della cassa, a qualunque giorno e ora.</param>
    public static JsonObject Build(AgentSettings s, string cashPoint, DateTime localNow, Func<string, string?> thanksPng, bool ignoreSchedule = false)
    {
        var a = s.Aspetto;
        var auto = a.Modo == "auto";
        var theme = Palettes.IsTheme(a.Tema) ? a.Tema : "bluetech";
        var palette = Palettes.Get(theme, auto ? "notte" : a.Modo);   // con l'automatico i token partono dalla notte (presetConfig)
        var colors = s.LocaleColors() ?? LocaleColors.Of(palette);
        string Font(string? family, string fallback) => family != null && Palettes.Fonts.TryGetValue(family, out var stack) ? stack : fallback;

        var tokens = new JsonObject
        {
            ["cPrimary"] = colors.Primary,
            ["cSecondary"] = colors.Secondary,
            ["cAccent"] = colors.Accent,
            ["cBg"] = colors.Bg,
            ["cSurface"] = colors.Surface,
            ["cText"] = colors.Text,
            ["cMuted"] = colors.Muted,
            ["cOnPrimary"] = colors.OnPrimary,
            ["fontDisplay"] = Font(a.FontTitoli, palette.FontDisplay),
            ["fontText"] = Font(a.FontTesto, palette.FontText),
            ["radius"] = Math.Clamp(a.Raggio ?? palette.Radius, 0, 32),
            ["bgStyle"] = a.Fondo switch
            {
                "tinta" => "solid",
                "sfumato" => "gradient",
                "immagine" when s.Media(a.FondoMedia) is { IsVideo: false } => "image",
                "video" when s.Media(a.FondoMedia) is { IsVideo: true } => "video",
                _ => "mesh",
            },
            ["glass"] = Math.Clamp((auto ? null : a.Vetro) ?? palette.Glass, 0, 100),
            ["motion"] = a.Animazioni switch { "piene" => "full", "ridotte" => "eco", _ => "light" },
            ["mode"] = auto ? "auto" : a.Modo,
            ["qrLayout"] = a.QrPagamento == "schermo" ? "fullscreen" : "beside",
            ["compactRows"] = a.RigheCompatte,
            ["showLastItemPhoto"] = false,   // il file della cassa non ha le foto degli articoli
            ["footerVariant"] = (auto ? null : a.Footer) switch { "chiaro" => "light", "scuro" => "dark", _ => palette.FooterVariant },
        };
        if (tokens["bgStyle"]!.GetValue<string>() is "image" or "video")
            tokens["bgAsset"] = Url(a.FondoMedia!);

        var ads = new JsonArray();
        var promos = new JsonArray();
        foreach (var promo in a.Promozioni)
        {
            if (ignoreSchedule ? !promo.Attiva || (promo.Casse.Count > 0 && !promo.Casse.Contains(cashPoint)) : !Active(promo, cashPoint, localNow))
                continue;
            var media = s.Media(promo.Media);
            if (promo.NelleSlide && media != null)
            {
                var slide = new JsonObject { ["id"] = promo.Id, ["media"] = media.IsVideo ? "video" : "image", ["src"] = Url(media.Id) };
                if (!media.IsVideo)
                    slide["durationMs"] = Math.Clamp(promo.SecondiImmagine, 3, 60) * 1000;
                ads.Add(slide);
            }
            if (promo.NelConto)
            {
                var card = new JsonObject { ["id"] = promo.Id, ["title"] = promo.Titolo.Trim(), ["text"] = promo.Testo.Trim() };
                if (media is { IsVideo: false })
                    card["image"] = Url(media.Id);
                promos.Add(card);
            }
        }

        var venue = new JsonObject { ["name"] = a.NomeLocale.Trim(), ["tagline"] = a.Sottotitolo.Trim() };
        if (s.Media(a.Logo) is { IsVideo: false } logo)
            venue["logo"] = Url(logo.Id);
        if (s.Media(a.LogoChiaro) is { IsVideo: false } light)
            venue["logoLight"] = Url(light.Id);   // al posto del logo sui fondi chiari

        var config = new JsonObject
        {
            ["preset"] = theme,
            ["theme"] = tokens,
            ["dayHours"] = new JsonObject { ["start"] = Hours(a.GiornoDalle, 7), ["end"] = Hours(a.GiornoAlle, 19) },
            ["ads"] = ads,
            ["venue"] = venue,
            ["texts"] = new JsonObject
            {
                ["welcome"] = a.Benvenuto.Trim(),
                ["receipt"] = string.IsNullOrWhiteSpace(a.TitoloConto) ? "Il tuo conto" : a.TitoloConto.Trim(),
                ["thanks"] = string.IsNullOrWhiteSpace(a.Grazie) ? "Grazie e arrivederci" : a.Grazie.Trim(),
            },
            ["promos"] = promos,
            ["pricesOnlyOnBill"] = a.PrezziSoloSulConto,
        };
        if (!string.IsNullOrWhiteSpace(a.Scorrevole))
            config["ticker"] = a.Scorrevole.Trim();
        if (a.QrGrazie is { Attivo: true } thanks && thanksPng(thanks.Indirizzo.Trim()) is { } png)
        {
            config["thanks"] = new JsonObject
            {
                ["png"] = png,
                ["title"] = thanks.Titolo.Trim(),
                ["caption"] = thanks.Didascalia.Trim(),
                ["seconds"] = Math.Clamp(thanks.Secondi, 10, 120),
            };
        }
        return config;
    }

    public static string Url(string mediaId) => "/media/" + mediaId;

    static double Hours(string text, double fallback) =>
        AgentSettings.ParseTime(text) is { } time ? Math.Round(time.Hour + time.Minute / 60.0, 4) : fallback;

    /// <summary>
    /// La promozione vale adesso per questa cassa? Una fascia a cavallo della mezzanotte (22:00-02:00) appartiene al
    /// giorno in cui comincia: il venerdì sera dura fino alle 2 di sabato.
    /// </summary>
    public static bool Active(PromotionConfig promo, string cashPoint, DateTime now)
    {
        if (!promo.Attiva || (promo.Casse.Count > 0 && !promo.Casse.Contains(cashPoint)))
            return false;
        var day = DateOnly.FromDateTime(now);
        var time = TimeOnly.FromDateTime(now);
        if (AgentSettings.ParseTime(promo.Dalle) is { } from && AgentSettings.ParseTime(promo.Alle) is { } to)
        {
            if (from < to)
            {
                if (time < from || time >= to)
                    return false;
            }
            else if (time < to)
            {
                day = day.AddDays(-1);
            }
            else if (time < from)
            {
                return false;
            }
        }
        if (promo.DalGiorno is { } first && day < first)
            return false;
        if (promo.AlGiorno is { } last && day > last)
            return false;
        var weekday = day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)day.DayOfWeek;
        return promo.Giorni.Count == 0 || promo.Giorni.Contains(weekday);
    }

    /// <summary>
    /// Il QR di un indirizzo come lo vuole il display: PNG a un pixel per modulo, senza quiet zone (la aggiunge il display,
    /// come per quello di Satispay). Correzione d'errore media: regge un riflesso sullo schermo.
    /// </summary>
    public static string QrPng(string text)
    {
        var qr = QrCode.EncodeText(text, QrCode.Ecc.Medium);
        var n = qr.Size;
        var modules = new bool[n * n];
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
            modules[y * n + x] = qr.GetModule(x, y);
        return "data:image/png;base64," + Convert.ToBase64String(QrImage.Encode(modules, n));
    }
}

/// <summary>Il messaggio cfg, già composto per la cassa.</summary>
public sealed record ConfigMessage(JsonObject Body) : CashIn
{
    public override string Type => "cfg";

    protected override void Fill(JsonObject json)
    {
        foreach (var (key, value) in Body)
            json[key] = value?.DeepClone();
    }
}
