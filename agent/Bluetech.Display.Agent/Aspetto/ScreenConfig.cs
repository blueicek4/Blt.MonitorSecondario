using System.Text.Json.Nodes;
using Bluetech.Display.Agent.Cassa;
using Bluetech.Display.Agent.Impostazioni;
using Bluetech.Display.Agent.Vendita;
using Net.Codecrete.QrCodeGenerator;

namespace Bluetech.Display.Agent.Aspetto;

/// <summary>
/// Il messaggio <c>cfg</c> (protocol.ts del design, iterazione 2): la <c>ScreenConfig</c> già piatta, con l'eredità
/// marchio → cassa → schermo risolta qui e non nello schermo, e quello che va in onda in ogni posto
/// (<c>ShowcaseByPlace</c>), già scelto da <see cref="Showcases.Choose"/> per la cassa, il giorno e l'ora. Le aggiunte
/// al protocollo del design sono in design/ALLINEAMENTO.md §8: <c>sequenza</c> in ogni vetrina (l'ordine lo fa
/// l'Agent, anche con "un QR ogni N"), <c>marks</c> (i loghi dei kit) e <c>dicitura</c> nello scontrino digitale.
/// </summary>
public static class ScreenConfig
{
    /// <summary>Per l'anteprima del back office: una vetrina in onda nei suoi posti a qualunque ora, o una promozione sola.</summary>
    public sealed record Preview(string? Showcase = null, string? Promo = null);

    /// <summary>
    /// La configurazione della cassa, uguale per tutti i suoi schermi; le eccezioni di ogni schermo le aggiunge
    /// <see cref="ForScreen"/> quando il messaggio parte.
    /// </summary>
    public static JsonObject Build(AgentSettings s, string cashPoint, DateTime localNow, Func<string, string?> qrPng, Preview? preview = null)
    {
        var cash = s.CashPoint(cashPoint);
        var brand = s.BrandOf(cash);
        var look = brand.Aspetto;
        var fixedMode = look.Modo != "auto";
        var theme = Palettes.IsTheme(look.Tema) ? look.Tema : Palettes.Fallback;
        var texts = brand.Scritte;
        var own = cash?.Override?.Scritte;
        string Text(string? cashText, string brandText, string fallback) =>
            (cashText ?? brandText).Trim() is { Length: > 0 } text ? text : fallback;

        var screen = new JsonObject
        {
            ["tema"] = theme,
            ["modo"] = fixedMode ? "fisso" : "auto",
            ["giornoDalle"] = AgentSettings.ParseTime(look.GiornoDalle)?.ToString("HH:mm") ?? "07:00",
            ["giornoAlle"] = AgentSettings.ParseTime(look.GiornoAlle)?.ToString("HH:mm") ?? "19:00",
        };
        if (AgentSettings.LocaleColors(brand) is { } colors)
        {
            screen["colori"] = new JsonObject
            {
                ["bg"] = colors.Bg,
                ["surface"] = colors.Surface,
                ["text"] = colors.Text,
                ["muted"] = colors.Muted,
                ["primary"] = colors.Primary,
                ["onPrimary"] = colors.OnPrimary,
                ["accent"] = colors.Accent,
                ["secondary"] = colors.Secondary,
            };
        }
        if (look.FontTitoli != null && Palettes.Fonts.ContainsKey(look.FontTitoli))
            screen["fontTitoli"] = look.FontTitoli;
        if (look.FontTesto != null && Palettes.Fonts.ContainsKey(look.FontTesto))
            screen["fontTesto"] = look.FontTesto;
        if (look.Raggio is { } radius)
            screen["raggio"] = Math.Clamp(radius, 0, 32);
        if (fixedMode && look.Vetro is { } glass)
            screen["vetro"] = Math.Clamp(glass, 0, 100);
        if (fixedMode && look.Footer is "light" or "dark")
            screen["footerVariant"] = look.Footer;   // con giorno e notte la sceglie il tema di quel momento

        var background = brand.Media(look.FondoMedia);
        screen["fondo"] = look.Fondo switch
        {
            "tinta" or "sfumato" => look.Fondo,
            "immagine" when background is { IsVideo: false } => "immagine",
            "video" when background is { IsVideo: true } => "video",
            _ => "mesh",
        };
        if (screen["fondo"]!.GetValue<string>() is "immagine" or "video")
            screen["fondoMedia"] = MediaJson(brand, background!);

        var identity = new JsonObject { ["nomeLocale"] = brand.Identita.NomeLocale.Trim(), ["sottotitolo"] = brand.Identita.Sottotitolo.Trim() };
        if (brand.Media(brand.Identita.Logo) is { IsVideo: false } logo)
            identity["logo"] = Url(logo.Id);
        if (brand.Media(brand.Identita.LogoChiaro) is { IsVideo: false } light)
            identity["logoChiaro"] = Url(light.Id);
        screen["identita"] = identity;

        var behaviour = brand.Schermo;
        screen["attrazione"] = behaviour.Attrazione;
        screen["animazioni"] = behaviour.Animazioni;
        screen["colonneConto"] = Math.Clamp(behaviour.ColonneConto, 9, 17);
        screen["colonneContoPagamento"] = Math.Clamp(behaviour.ColonneContoPagamento, 8, 14);
        screen["colonnaAllineamento"] = behaviour.ColonnaAllineamento;
        screen["qrPagamento"] = behaviour.QrPagamento;
        screen["prezziSoloSulConto"] = behaviour.PrezziSoloSulConto;
        screen["righeCompatte"] = behaviour.RigheCompatte;
        screen["showLastItemPhoto"] = false;   // il file della cassa non ha le foto degli articoli
        screen["scritte"] = new JsonObject
        {
            ["benvenuto"] = (own?.Benvenuto ?? texts.Benvenuto).Trim(),
            ["titoloConto"] = Text(own?.TitoloConto, texts.TitoloConto, "Il tuo conto"),
            ["grazie"] = Text(own?.Grazie, texts.Grazie, "Grazie e arrivederci"),
            ["secondiGrazie"] = Math.Clamp(texts.SecondiGrazie, 10, 120),
        };

        var receipt = brand.Vendita.ScontrinoDigitale;
        screen["receipt"] = new JsonObject
        {
            ["attivo"] = receipt.Attivo || s.Installazione.Demo,
            ["blocchi"] = new JsonArray(receipt.Blocchi.Select(b => (JsonNode)new JsonObject
            {
                ["tipo"] = b.Tipo,
                ["attivo"] = b.Attivo,
                ["colonna"] = b.Colonna,
                ["riga"] = b.Riga,
            }).ToArray()),
            ["secondi"] = Math.Clamp(receipt.Secondi, 10, 600),
            ["copiaCarta"] = receipt.CopiaCarta || s.Installazione.Demo,   // lo schermo lo spegne se non ha una stampante
            ["dicitura"] = receipt.Dicitura.Trim(),
        };

        // Una promozione o un'immagine che compare in più posti si scrive una volta per posto: i posti sono tre.
        var showcases = new JsonObject();
        foreach (var place in ShowcaseConfig.Places)
        {
            ShowcaseConfig? winner;
            List<Showcases.Entry> items;
            if (preview?.Promo is { } promoId)
            {
                var only = brand.Promo(promoId);
                winner = only == null ? null : new ShowcaseConfig { Id = "anteprima", Nome = only.Nome, Posti = [place], Elementi = [new ShowcaseItemConfig { Id = only.Id }] };
                items = winner == null ? [] : Showcases.Usable(brand, winner, DateOnly.FromDateTime(localNow));
            }
            else
            {
                var choice = Showcases.Choose(brand, cashPoint, place, localNow, preview?.Showcase);
                (winner, items) = (choice.Winner, choice.Items);
            }
            if (winner == null || items.Count == 0)
                continue;
            var elements = new JsonObject();
            foreach (var item in items)
                elements[item.Id] ??= Item(brand, item, qrPng);
            showcases[place] = new JsonObject
            {
                ["vetrinaId"] = winner.Id,
                ["nome"] = winner.Nome,
                ["qrOgni"] = Math.Clamp(winner.QrOgni, 0, 20),
                ["elementi"] = new JsonArray(elements.Select(e => e.Value!.DeepClone()).ToArray()),
                ["sequenza"] = new JsonArray(Showcases.Sequence(winner, items).Select(i => (JsonNode)JsonValue.Create(i.Id)!).ToArray()),
            };
        }

        // I marchi dai kit ufficiali, se caricati: la firma Bluetech scura e chiara, il logo di Satispay (ALLINEAMENTO §8).
        var marks = new JsonObject();
        foreach (var (key, id) in new[]
        {
            ("bluetech", s.Installazione.Loghi.Bluetech), ("bluetechLight", s.Installazione.Loghi.BluetechChiaro),
            ("satispay", s.Installazione.Loghi.Satispay), ("satispayLight", s.Installazione.Loghi.SatispayChiaro),
        })
        {
            if (id != null && s.Installazione.Media.Find(m => m.Id == id) is { IsVideo: false } image)
                marks[key] = Url(image.Id);
        }

        var config = new JsonObject
        {
            ["screen"] = screen,
            ["showcase"] = showcases,
            ["marks"] = marks,
        };
        var ticker = (own?.Scorrevole ?? texts.Scorrevole).Trim();
        if (ticker.Length > 0)
            config["ticker"] = ticker;
        return config;
    }

    /// <summary>
    /// Il cfg per uno schermo: le sue eccezioni (disposizione, animazioni, attrazione, larghezza e allineamento del conto)
    /// sopra quelle del marchio, e il pulsante della copia di carta solo se ha una stampante. Lo schermo non sa da quale
    /// livello viene un valore.
    /// </summary>
    public static JsonObject ForScreen(JsonObject config, ScreenOverride? screenOverride, bool canPrint)
    {
        var copy = config.DeepClone().AsObject();
        if (copy["screen"] is not JsonObject screen)
            return copy;
        if (screenOverride is { } o)
        {
            if (o.Disposizione != null)
                screen["disposizione"] = o.Disposizione;
            if (o.Animazioni != null)
                screen["animazioni"] = o.Animazioni;
            if (o.Attrazione != null)
                screen["attrazione"] = o.Attrazione;
            if (o.ColonneConto is { } columns)
                screen["colonneConto"] = Math.Clamp(columns, 9, 17);
            if (o.ColonnaAllineamento != null)
                screen["colonnaAllineamento"] = o.ColonnaAllineamento;
        }
        if (!canPrint && screen["receipt"] is JsonObject receipt)
            receipt["copiaCarta"] = false;
        return copy;
    }

    /// <summary>Un elemento di una vetrina come lo vuole lo schermo (ShowcaseItem): tutto quello che serve per disegnarlo.</summary>
    static JsonObject Item(BrandConfig brand, Showcases.Entry entry, Func<string, string?> qrPng)
    {
        var json = new JsonObject { ["id"] = entry.Id };
        if (entry.Media is { } media)
        {
            json["tipo"] = media.IsVideo ? "video" : "foto";
            json["modello"] = "B";   // una foto o un video della galleria parla da sé: il sistema non aggiunge titoli
            json["media"] = MediaJson(brand, media);
        }
        else if (entry.Promo is { } promo)
        {
            json["tipo"] = "promozione";
            json["modello"] = PromoConfig.Models.Contains(promo.Modello) ? promo.Modello : PromoConfig.Suggested(promo.HasQr, brand.Media(promo.Immagine));
            if (brand.Media(promo.Immagine) is { IsVideo: false } image)
                json["media"] = MediaJson(brand, image);
            if (promo.Etichetta is { } label && label != "nessuna" && PromoConfig.Labels.Contains(label))
                json["etichetta"] = label;
            if (promo.Titolo.Trim() is { Length: > 0 } title)
                json["titolo"] = title;
            if (promo.Testo.Trim() is { Length: > 0 } text)
                json["testo"] = text;
            if (promo.HasQr && qrPng(promo.Qr!.Trim()) is { } png)
                json["qr"] = new JsonObject { ["png"] = png, ["didascalia"] = promo.QrDidascalia.Trim() };
        }
        if (entry.Seconds is { } seconds)
            json["durataMs"] = seconds * 1000;   // un video dura quanto dura
        return json;
    }

    static JsonObject MediaJson(BrandConfig brand, MediaItem media)
    {
        var json = new JsonObject { ["src"] = Url(media.Id) };
        if (media.IsVideo)
        {
            if (brand.Media(media.Poster) is { } poster)
                json["poster"] = Url(poster.Id);
            if (brand.Media(media.PosterSfocato) is { } blurred)
                json["posterBlur"] = Url(blurred.Id);
        }
        if (media.Focus is { } focus)
            json["focus"] = new JsonObject { ["x"] = Math.Round(Math.Clamp(focus.X, 0, 1), 3), ["y"] = Math.Round(Math.Clamp(focus.Y, 0, 1), 3) };
        if (media.HaScritte)
            json["scritte"] = true;   // ha già le scritte dentro: sui formati stretti fasce, non ritaglio (SPEC §16)
        return json;
    }

    public static string Url(string mediaId) => "/media/" + mediaId;

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

/// <summary>Il messaggio cfg, già composto per la cassa; <see cref="ScreenConfig.ForScreen"/> lo adatta a ogni schermo.</summary>
public sealed record ConfigMessage(JsonObject Body) : CashIn
{
    public override string Type => "cfg";

    protected override void Fill(JsonObject json)
    {
        foreach (var (key, value) in Body)
            json[key] = value?.DeepClone();
    }
}
