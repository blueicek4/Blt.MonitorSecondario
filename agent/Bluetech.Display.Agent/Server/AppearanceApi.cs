using System.Text.Json;
using System.Text.Json.Nodes;
using Bluetech.Display.Agent.Aspetto;
using Bluetech.Display.Agent.Impostazioni;
using Microsoft.AspNetCore.Http.Features;
using static Bluetech.Display.Agent.Server.BackOfficeApi;

namespace Bluetech.Display.Agent.Server;

/// <summary>
/// L'aspetto dello schermo del cliente dal back office: tema, logo, scritte, QR del Grazie, promozioni, e le immagini e
/// i video che usano. L'anteprima compone il messaggio cfg di una bozza senza salvarla.
/// </summary>
public static class AppearanceApi
{
    public static void Map(RouteGroupBuilder bo)
    {
        bo.MapGet("/aspetto", (SettingsStore settings) =>
        {
            var now = settings.Current;
            return Json(new JsonObject
            {
                ["Aspetto"] = JsonSerializer.SerializeToNode(now.Aspetto, AgentSettings.Json),
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

        // La bozza com'è: il cfg che riceverebbero i display della cassa e gli errori che la farebbero rifiutare.
        bo.MapPost("/aspetto/anteprima", async (HttpContext context, SettingsStore settings, AgentEngine engine) =>
        {
            if (await Read<AppearanceSettings>(context) is not { } draft)
                return Error("Dati non validi.");
            var preview = settings.Current.Clone();
            preview.Aspetto = Tidy(draft, preview.Aspetto.Media);
            var cash = context.Request.Query["cassa"].ToString();
            if (preview.CashPoint(cash) == null)
                cash = preview.Casse.FirstOrDefault()?.Id ?? "";
            var everything = context.Request.Query["tutte"] == "1";
            var config = DisplayConfig.Build(preview, cash, DateTime.Now, engine.ThanksPng, ignoreSchedule: everything);
            return Json(new JsonObject
            {
                ["cfg"] = new ConfigMessage(config).ToJson(),
                ["problemi"] = new JsonArray(preview.AppearanceProblems().Select(p => (JsonNode)p).ToArray()),
                // Il QR del pagamento finto dell'anteprima: porta a example.com, come quello della stampa di prova.
                ["anteprimaQr"] = engine.ThanksPng("https://example.com/bluetech-display/anteprima"),
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
        draft.QrGrazie ??= new ThanksQrSettings();
        draft.QrGrazie.Indirizzo = (draft.QrGrazie.Indirizzo ?? "").Trim();
        draft.QrGrazie.Titolo = (draft.QrGrazie.Titolo ?? "").Trim();
        draft.QrGrazie.Didascalia = (draft.QrGrazie.Didascalia ?? "").Trim();
        if (draft.Colori is { } c)
        {
            foreach (var property in typeof(ThemeColorSettings).GetProperties())
                property.SetValue(c, ((string?)property.GetValue(c) ?? "").Trim());
        }
        draft.Promozioni ??= [];
        draft.Promozioni.RemoveAll(p => p == null);
        var ids = new HashSet<string>();
        foreach (var promo in draft.Promozioni)
        {
            promo.Nome = (promo.Nome ?? "").Trim();
            promo.Titolo = (promo.Titolo ?? "").Trim();
            promo.Testo = (promo.Testo ?? "").Trim();
            promo.Casse = (promo.Casse ?? []).Distinct().ToList();
            promo.Giorni = (promo.Giorni ?? []).Distinct().Order().ToList();
            promo.Dalle = string.IsNullOrWhiteSpace(promo.Dalle) ? null : promo.Dalle.Trim();
            promo.Alle = string.IsNullOrWhiteSpace(promo.Alle) ? null : promo.Alle.Trim();
            // Una promozione nuova arriva senza id: il prossimo libero, come per casse e stampanti.
            if (string.IsNullOrWhiteSpace(promo.Id) || !AgentSettings.IdPattern().IsMatch(promo.Id) || !ids.Add(promo.Id))
            {
                promo.Id = AgentSettings.NextId("promo", draft.Promozioni.Select(p => p.Id ?? "").Concat(ids));
                ids.Add(promo.Id);
            }
        }
        return draft;
    }
}
