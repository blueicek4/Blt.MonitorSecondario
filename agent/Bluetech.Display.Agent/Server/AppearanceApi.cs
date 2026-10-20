using System.Text.Json;
using System.Text.Json.Nodes;
using Bluetech.Display.Agent.Aspetto;
using Bluetech.Display.Agent.Impostazioni;
using Microsoft.AspNetCore.Http.Features;
using static Bluetech.Display.Agent.Server.BackOfficeApi;

namespace Bluetech.Display.Agent.Server;

/// <summary>
/// L'aspetto dello schermo del cliente dal back office: tema, logo, scritte, le gallerie (foto, video, promozioni) e i
/// set di reclame. L'anteprima compone il messaggio cfg di una bozza senza salvarla.
/// </summary>
public static class AppearanceApi
{
    public static void Map(RouteGroupBuilder bo)
    {
        bo.MapGet("/aspetto", (SettingsStore settings) =>
        {
            var now = settings.Current;
            var clock = DateTime.Now;
            // Cosa va in onda adesso, cassa per cassa e posto per posto, e com'è ogni set: il back office non rifà i conti.
            var onAir = new JsonObject();
            foreach (var cash in now.Casse)
                onAir[cash.Id] = new JsonObject(DisplayConfig.OnAir(now, cash.Id, clock).Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value?.Id)));
            return Json(new JsonObject
            {
                ["Aspetto"] = JsonSerializer.SerializeToNode(now.Aspetto, AgentSettings.Json),
                ["InOnda"] = onAir,
                ["StatoSet"] = new JsonObject(DisplayConfig.States(now, clock).Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))),
                ["Temi"] = new JsonArray(Palettes.Themes.Select(t => (JsonNode)new JsonObject { ["Id"] = t.Id, ["Nome"] = t.Name, ["Carattere"] = t.Character }).ToArray()),
                ["Font"] = new JsonArray(Palettes.Fonts.Keys.Select(f => (JsonNode)f).ToArray()),
                ["Casse"] = new JsonArray(now.Casse.Select(c => (JsonNode)new JsonObject { ["Id"] = c.Id, ["Nome"] = c.Nome }).ToArray()),
                ["LimitiMb"] = new JsonObject { ["Immagine"] = MediaLibrary.MaxImageBytes / 1024 / 1024, ["Video"] = MediaLibrary.MaxVideoBytes / 1024 / 1024 },
            });
        });

        bo.MapPut("/aspetto", async (HttpContext context, SettingsStore settings, ILogger<SettingsStore> log) =>
        {
            if (await Read<AppearanceSettings>(context) is not { } draft)
                return Error("Dati non validi.");
            var error = settings.Update(s =>
            {
                s.Aspetto = Tidy(draft, s.Aspetto.Media);
                return null;
            });
            if (error != null)
                return Error(error);
            log.LogInformation("Aspetto cambiato dal back office ({Remote})", AgentApp.Remote(context));
            return Ok();
        });

        // La bozza com'è: il cfg che riceverebbero adesso i display della cassa e gli errori che la farebbero rifiutare.
        // Con ?set= quel set nei suoi posti a qualunque ora (si sta provando), con ?promo= solo quella promozione.
        bo.MapPost("/aspetto/anteprima", async (HttpContext context, SettingsStore settings, AgentEngine engine) =>
        {
            if (await Read<AppearanceSettings>(context) is not { } draft)
                return Error("Dati non validi.");
            var preview = settings.Current.Clone();
            preview.Aspetto = Tidy(draft, preview.Aspetto.Media);
            var cash = context.Request.Query["cassa"].ToString();
            if (preview.CashPoint(cash) == null)
                cash = preview.Casse.FirstOrDefault()?.Id ?? "";
            var set = context.Request.Query["set"].ToString() is { Length: > 0 } forced ? forced : null;
            var promo = context.Request.Query["promo"].ToString() is { Length: > 0 } only ? only : null;
            var clock = DateTime.Now;
            var config = DisplayConfig.Build(preview, cash, clock, engine.CachedQr, set, promo);
            var onAir = DisplayConfig.OnAir(preview, cash, clock, set);
            return Json(new JsonObject
            {
                ["cfg"] = new ConfigMessage(config).ToJson(),
                ["problemi"] = new JsonArray(preview.AppearanceProblems().Select(p => (JsonNode)p).ToArray()),
                ["inOnda"] = new JsonObject(onAir.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value?.Nome))),
                // Il QR del pagamento finto dell'anteprima: porta a example.com, come quello della stampa di prova.
                ["anteprimaQr"] = engine.CachedQr("https://example.com/bluetech-display/anteprima"),
            });
        });

        bo.MapPost("/media", async (HttpContext context, MediaLibrary media) =>
        {
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                limit.MaxRequestBodySize = MediaLibrary.MaxVideoBytes + 1024 * 1024;
            if (context.Request.ContentLength > MediaLibrary.MaxVideoBytes)
                return Error($"Il file è troppo grande: un video arriva al massimo a {MediaLibrary.MaxVideoBytes / 1024 / 1024} MB.", 413);
            try
            {
                var (item, error) = await media.SaveAsync(context.Request.Body, context.Request.Query["nome"], context.RequestAborted);
                return item == null
                    ? Error(error ?? "Non caricato.")
                    : Ok(new JsonObject { ["media"] = JsonSerializer.SerializeToNode(item, AgentSettings.Json) });
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                return Error($"Il file è troppo grande: un video arriva al massimo a {MediaLibrary.MaxVideoBytes / 1024 / 1024} MB.", 413);
            }
            catch (IOException ex)
            {
                return Error($"Caricamento interrotto: {ex.Message}");
            }
        });

        bo.MapDelete("/media/{id}", (string id, MediaLibrary media) => Result(media.Delete(id)));
    }

    /// <summary>La bozza pulita: testi senza spazi in più, liste mai nulle, l'elenco dei file sempre quello dell'Agent.</summary>
    static AppearanceSettings Tidy(AppearanceSettings draft, List<MediaItem> media)
    {
        draft.Media = media.ToList();
        draft.NomeLocale = (draft.NomeLocale ?? "").Trim();
        draft.Sottotitolo = (draft.Sottotitolo ?? "").Trim();
        draft.Benvenuto = (draft.Benvenuto ?? "").Trim();
        draft.Scorrevole = (draft.Scorrevole ?? "").Trim();
        draft.TitoloConto = (draft.TitoloConto ?? "").Trim();
        draft.Grazie = (draft.Grazie ?? "").Trim();
        draft.GiornoDalle = (draft.GiornoDalle ?? "").Trim();
        draft.GiornoAlle = (draft.GiornoAlle ?? "").Trim();
        draft.QrGrazie = null;
        if (draft.Colori is { } c)
        {
            foreach (var property in typeof(ThemeColorSettings).GetProperties())
                property.SetValue(c, ((string?)property.GetValue(c) ?? "").Trim());
        }

        // Le promozioni e i set nuovi arrivano con l'id già dato dal back office (un set nuovo può contenere una
        // promozione nuova); senza, o doppio, il prossimo libero, come per casse e stampanti.
        draft.Promozioni ??= [];
        draft.Promozioni.RemoveAll(p => p == null);
        var promoIds = new HashSet<string>();
        foreach (var promo in draft.Promozioni)
        {
            promo.Vecchio = null;
            promo.Nome = (promo.Nome ?? "").Trim();
            promo.Titolo = (promo.Titolo ?? "").Trim();
            promo.Testo = (promo.Testo ?? "").Trim();
            promo.QrDidascalia = (promo.QrDidascalia ?? "").Trim();
            promo.Qr = string.IsNullOrWhiteSpace(promo.Qr) ? null : promo.Qr.Trim();
            promo.Immagine = string.IsNullOrWhiteSpace(promo.Immagine) ? null : promo.Immagine;
            if (string.IsNullOrWhiteSpace(promo.Id) || !AgentSettings.IdPattern().IsMatch(promo.Id) || !promoIds.Add(promo.Id))
            {
                promo.Id = AgentSettings.NextId("promo", draft.Promozioni.Select(p => p.Id ?? "").Concat(promoIds));
                promoIds.Add(promo.Id);
            }
        }
        draft.Set ??= [];
        draft.Set.RemoveAll(x => x == null);
        var setIds = new HashSet<string>();
        foreach (var set in draft.Set)
        {
            set.Nome = (set.Nome ?? "").Trim();
            set.Ordine = (set.Ordine ?? "").Trim();
            set.Dove = (set.Dove ?? []).Where(d => d != null).Distinct().ToList();
            set.Elementi = (set.Elementi ?? []).Where(e => e is { Id: not null }).ToList();
            set.Casse = (set.Casse ?? []).Distinct().ToList();
            set.Giorni = (set.Giorni ?? []).Distinct().Order().ToList();
            set.Dalle = string.IsNullOrWhiteSpace(set.Dalle) ? null : set.Dalle.Trim();
            set.Alle = string.IsNullOrWhiteSpace(set.Alle) ? null : set.Alle.Trim();
            if (string.IsNullOrWhiteSpace(set.Id) || !AgentSettings.IdPattern().IsMatch(set.Id) || !setIds.Add(set.Id))
            {
                set.Id = AgentSettings.NextId("set", draft.Set.Select(x => x.Id ?? "").Concat(setIds));
                setIds.Add(set.Id);
            }
        }
        return draft;
    }
}
