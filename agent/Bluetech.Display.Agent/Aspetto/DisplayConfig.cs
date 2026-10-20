using System.Globalization;
using System.Text.Json.Nodes;
using Bluetech.Display.Agent.Cassa;
using Bluetech.Display.Agent.Impostazioni;
using Bluetech.Display.Agent.Vendita;
using Net.Codecrete.QrCodeGenerator;

namespace Bluetech.Display.Agent.Aspetto;

/// <summary>
/// Il messaggio <c>cfg</c> per i display di una cassa: i token del tema di protocol.ts del design (<c>theme</c>, <c>ads</c>,
/// <c>ticker</c>) più quello che il design non ha ancora (design/ALLINEAMENTO.md §8): il tema di partenza, le ore del
/// giorno, il locale, le scritte, e le tre playlist dei set di reclame (attesa, accanto al conto, Grazie) con le schede
/// delle promozioni. Tutto arriva già scelto per la cassa, il giorno e l'ora, e già messo in ordine: il display non
/// decide niente, l'Agent rimanda il messaggio quando cambia qualcosa.
/// </summary>
public static class DisplayConfig
{
    /// <summary>I posti dei set e il loro nome nel messaggio.</summary>
    static readonly (string Place, string Key)[] Places = [("attesa", "idle"), ("conto", "sale"), ("grazie", "thanks")];

    /// <param name="forceSet">Per l'anteprima: questo set nei suoi posti, acceso o spento, a qualunque giorno e ora.</param>
    /// <param name="onlyPromo">Per l'anteprima: solo questa promozione, in tutti e tre i posti.</param>
    public static JsonObject Build(AgentSettings s, string cashPoint, DateTime localNow, Func<string, string?> qrPng, string? forceSet = null, string? onlyPromo = null)
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

        // Le playlist hanno gli id, le schede delle promozioni stanno a parte una volta sola: una promozione con il QR
        // può tornare tante volte in una playlist casuale.
        var playlists = new JsonObject();
        var cards = new JsonObject();
        var ads = new JsonArray();
        foreach (var (place, key) in Places)
        {
            var items = onlyPromo != null
                ? s.Promo(onlyPromo) is { } only ? [new Entry(only.Id, only.Nome, null, only, 10)] : []
                : Choose(s, place, cashPoint, localNow, forceSet).Items;
            var list = new JsonArray();
            foreach (var item in items)
            {
                list.Add(item.ToJson());
                if (item.Promo is { } promo && !cards.ContainsKey(promo.Id))
                    cards[promo.Id] = Card(s, promo, qrPng);
                if (key == "idle" && item.Media is { } media)
                {
                    // Le AdSlide di protocol.ts per chi non conosce ancora le promozioni: le foto e i video dell'attesa.
                    var slide = new JsonObject { ["id"] = media.Id, ["media"] = media.IsVideo ? "video" : "image", ["src"] = Url(media.Id) };
                    if (!media.IsVideo)
                        slide["durationMs"] = item.Seconds * 1000;
                    ads.Add(slide);
                }
            }
            playlists[key] = list;
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
            ["playlists"] = playlists,
            ["promoCards"] = cards,
            ["venue"] = venue,
            ["texts"] = new JsonObject
            {
                ["welcome"] = a.Benvenuto.Trim(),
                ["receipt"] = string.IsNullOrWhiteSpace(a.TitoloConto) ? "Il tuo conto" : a.TitoloConto.Trim(),
                ["thanks"] = string.IsNullOrWhiteSpace(a.Grazie) ? "Grazie e arrivederci" : a.Grazie.Trim(),
            },
            ["thanksSeconds"] = Math.Clamp(a.SecondiGrazie, 10, 120),
            ["pricesOnlyOnBill"] = a.PrezziSoloSulConto,
        };
        if (!string.IsNullOrWhiteSpace(a.Scorrevole))
            config["ticker"] = a.Scorrevole.Trim();
        return config;
    }

    /// <summary>La scheda di una promozione: le scritte, l'immagine, il QR già fatto con la sua scritta.</summary>
    static JsonObject Card(AgentSettings s, PromoConfig promo, Func<string, string?> qrPng)
    {
        var card = new JsonObject { ["title"] = promo.Titolo.Trim(), ["text"] = promo.Testo.Trim() };
        if (s.Media(promo.Immagine) is { IsVideo: false } image)
            card["image"] = Url(image.Id);
        if (promo.HasQr && qrPng(promo.Qr!.Trim()) is { } png)
            card["qr"] = new JsonObject { ["png"] = png, ["caption"] = promo.QrDidascalia.Trim() };
        return card;
    }

    /// <summary>Un elemento di una playlist: una foto, un video o una promozione, con quanto resta.</summary>
    public sealed record Entry(string Id, string Name, MediaItem? Media, PromoConfig? Promo, int Seconds)
    {
        public bool HasQr => Promo?.HasQr == true;

        public JsonObject ToJson()
        {
            var json = new JsonObject { ["id"] = Id, ["kind"] = Promo != null ? "promo" : Media!.IsVideo ? "video" : "image" };
            if (Media != null)
                json["src"] = Url(Media.Id);
            if (Media is not { IsVideo: true })
                json["durationMs"] = Seconds * 1000;   // un video dura quanto dura
            return json;
        }
    }

    /// <summary>
    /// Il set in onda in un posto, per la cassa e adesso, con la sua playlist: il primo dell'elenco acceso, con quel posto,
    /// che vale adesso e ha qualcosa da mostrare (le promozioni fuori dalle loro date non contano). Nessuno: niente.
    /// </summary>
    public static (AdSetConfig? Set, List<Entry> Items) Choose(AgentSettings s, string place, string cashPoint, DateTime now, string? forceSet = null)
    {
        if (forceSet != null && s.Aspetto.Set.Find(x => x.Id == forceSet) is { } forced && forced.Dove.Contains(place))
            return (forced, Expand(forced, Usable(s, forced, DateOnly.FromDateTime(now))));
        foreach (var set in s.Aspetto.Set)
        {
            if (!set.Attivo || !set.Dove.Contains(place) || ServiceDay(set, cashPoint, now) is not { } day)
                continue;
            var items = Usable(s, set, day);
            if (items.Count > 0)
                return (set, Expand(set, items));
        }
        return (null, []);
    }

    /// <summary>Il set in onda in ogni posto (attesa, conto, grazie), per il log e il back office.</summary>
    public static Dictionary<string, AdSetConfig?> OnAir(AgentSettings s, string cashPoint, DateTime now, string? forceSet = null) =>
        Places.ToDictionary(p => p.Place, p => Choose(s, p.Place, cashPoint, now, forceSet).Set);

    /// <summary>
    /// Com'è ogni set adesso, per il back office: "in onda" (almeno in un posto di una cassa), "coperto" (vale, ma nei
    /// suoi posti va un set più in alto), "fuori orario", "vuoto" (niente da mostrare, oggi), "spento".
    /// </summary>
    public static Dictionary<string, string> States(AgentSettings s, DateTime now)
    {
        var onAir = s.Casse.SelectMany(c => OnAir(s, c.Id, now).Values).OfType<AdSetConfig>().Select(x => x.Id).ToHashSet();
        var states = new Dictionary<string, string>();
        foreach (var set in s.Aspetto.Set)
        {
            var days = s.Casse.Select(c => ServiceDay(set, c.Id, now)).OfType<DateOnly>().ToList();
            states[set.Id] = !set.Attivo ? "spento"
                : set.Elementi.Count == 0 ? "vuoto"
                : onAir.Contains(set.Id) ? "in onda"
                : days.Count == 0 ? "fuori orario"
                : days.All(d => Usable(s, set, d).Count == 0) ? "vuoto"
                : "coperto";
        }
        return states;
    }

    static List<Entry> Usable(AgentSettings s, AdSetConfig set, DateOnly day)
    {
        var items = new List<Entry>();
        foreach (var element in set.Elementi)
        {
            var seconds = Math.Clamp(element.Secondi ?? set.Secondi, 3, 120);
            if (s.Media(element.Id) is { } media)
                items.Add(new Entry(media.Id, media.Nome, media, null, seconds));
            else if (s.Promo(element.Id) is { } promo && !(promo.DalGiorno > day) && !(promo.AlGiorno < day))
                items.Add(new Entry(promo.Id, promo.Nome, null, promo, seconds));
        }
        return items;
    }

    const int MinRandomLength = 60;
    const int MaxLength = 300;

    /// <summary>
    /// La playlist del set, che il display ripete: come nell'elenco, per nome o a caso (giri mescolati uno dopo l'altro,
    /// senza lo stesso elemento due volte di fila tra un giro e l'altro). Con <see cref="AdSetConfig.QrDopo"/> le
    /// promozioni con il QR vanno a parte: dopo tanti elementi senza QR, la prossima con il QR, e le due liste fanno giri
    /// interi. A caso il seme viene dal set e non dall'ora: lo stesso set dà sempre la stessa playlist, e il messaggio non
    /// cambia a ogni controllo.
    /// </summary>
    public static List<Entry> Expand(AdSetConfig set, List<Entry> items)
    {
        if (items.Count == 0)
            return items;
        if (set.Ordine == "alfabetico")
            items = items.OrderBy(i => i.Name, StringComparer.Create(CultureInfo.GetCultureInfo("it-IT"), CompareOptions.IgnoreCase)).ToList();
        var random = set.Ordine == "casuale" ? new Random(Seed(set, items)) : null;
        var after = Math.Clamp(set.QrDopo, 0, 20);
        var plain = after == 0 ? items : items.Where(i => !i.HasQr).ToList();
        var qr = after == 0 ? [] : items.Where(i => i.HasQr).ToList();
        if (qr.Count == 0 || plain.Count == 0)
        {
            if (random == null)
                return items;
            var rounds = Math.Clamp((MinRandomLength + items.Count - 1) / items.Count, 1, Math.Max(1, MaxLength / items.Count));
            return Rounds(items, rounds * items.Count, random);
        }
        var blockLength = after + 1;
        var blocks = Lcm(plain.Count / Gcd(plain.Count, after), qr.Count);
        if (random != null && blocks * blockLength < MinRandomLength)
            blocks *= (MinRandomLength + blocks * blockLength - 1) / (blocks * blockLength);
        blocks = Math.Min(blocks, Math.Max(1, MaxLength / blockLength));
        var plainStream = Rounds(plain, (int)blocks * after, random);
        var qrStream = Rounds(qr, (int)blocks, random);
        var result = new List<Entry>((int)blocks * blockLength);
        for (var b = 0; b < blocks; b++)
        {
            result.AddRange(plainStream.GetRange(b * after, after));
            result.Add(qrStream[b]);
        }
        return result;
    }

    /// <summary>Tanti elementi quanti ne servono, un giro dopo l'altro; a caso ogni giro è mescolato.</summary>
    static List<Entry> Rounds(List<Entry> items, int count, Random? random)
    {
        var result = new List<Entry>(count);
        while (result.Count < count)
        {
            var round = items.ToArray();
            if (random != null)
            {
                random.Shuffle(round);
                if (round.Length > 1 && result.Count > 0 && round[0].Id == result[^1].Id)
                {
                    var other = 1 + random.Next(round.Length - 1);
                    (round[0], round[other]) = (round[other], round[0]);
                }
            }
            result.AddRange(round.Take(count - result.Count));
        }
        return result;
    }

    static int Seed(AdSetConfig set, List<Entry> items)
    {
        // FNV-1a: string.GetHashCode cambia a ogni avvio, il seme deve restare lo stesso.
        var hash = 2166136261u;
        foreach (var c in set.Id + "|" + string.Join(",", items.Select(i => i.Id)))
            hash = unchecked((hash ^ c) * 16777619u);
        return (int)(hash & 0x7FFFFFFF);
    }

    static long Gcd(long a, long b)
    {
        while (b != 0)
            (a, b) = (b, a % b);
        return a;
    }

    static long Lcm(long a, long b) => a / Gcd(a, b) * b;

    public static string Url(string mediaId) => "/media/" + mediaId;

    static double Hours(string text, double fallback) =>
        AgentSettings.ParseTime(text) is { } time ? Math.Round(time.Hour + time.Minute / 60.0, 4) : fallback;

    /// <summary>
    /// Il giorno a cui appartiene adesso il set, se vale adesso per questa cassa (acceso o no); null se no. Una fascia a
    /// cavallo della mezzanotte (22:00-02:00) appartiene al giorno in cui comincia: il venerdì sera dura fino alle 2 di
    /// sabato, e una promozione valida fino a venerdì c'è ancora all'una.
    /// </summary>
    public static DateOnly? ServiceDay(AdSetConfig set, string cashPoint, DateTime now)
    {
        if (set.Casse.Count > 0 && !set.Casse.Contains(cashPoint))
            return null;
        var day = DateOnly.FromDateTime(now);
        var time = TimeOnly.FromDateTime(now);
        if (AgentSettings.ParseTime(set.Dalle) is { } from && AgentSettings.ParseTime(set.Alle) is { } to)
        {
            if (from < to)
            {
                if (time < from || time >= to)
                    return null;
            }
            else if (time < to)
            {
                day = day.AddDays(-1);
            }
            else if (time < from)
            {
                return null;
            }
        }
        if (set.DalGiorno is { } first && day < first)
            return null;
        if (set.AlGiorno is { } last && day > last)
            return null;
        var weekday = day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)day.DayOfWeek;
        return set.Giorni.Count == 0 || set.Giorni.Contains(weekday) ? day : null;
    }

    public static bool Active(AdSetConfig set, string cashPoint, DateTime now) => set.Attivo && ServiceDay(set, cashPoint, now) != null;

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
