using System.Text.Json;
using System.Text.Json.Nodes;
using Bluetech.Display.Agent.Accesso;
using Bluetech.Display.Agent.Aspetto;
using Bluetech.Display.Agent.Impostazioni;
using Microsoft.AspNetCore.Http.Features;
using static Bluetech.Display.Agent.Server.BackOfficeApi;

namespace Bluetech.Display.Agent.Server;

/// <summary>
/// Quello che il titolare cambia dal back office (SPEC §20): il locale (identità, aspetto, schermo, scritte, vendita), le
/// vetrine con le loro regole, i contenuti (foto, video, promozioni), gli schermi e le scritte di ogni cassa. Ogni
/// salvataggio passa dalla validazione delle impostazioni: si rifiuta solo una modifica che aggiunge un problema.
/// L'anteprima compone il cfg di una bozza senza salvarla.
/// </summary>
public static class BrandApi
{
    /// <summary>Le parti di un marchio che arrivano dal back office: quelle che ci sono si sostituiscono, le altre restano.</summary>
    sealed class BrandPatch
    {
        public string? Marchio { get; set; }
        public string? Nome { get; set; }
        public BrandIdentity? Identita { get; set; }
        public BrandLook? Aspetto { get; set; }
        public BrandScreen? Schermo { get; set; }
        public BrandTexts? Scritte { get; set; }
        public BrandSale? Vendita { get; set; }
        public ContentPatch? Contenuti { get; set; }
        /// <summary>Solo per l'anteprima: le eccezioni di uno schermo (disposizione, attrazione…) da provare.</summary>
        public ScreenOverride? Override { get; set; }
    }

    sealed class ContentPatch
    {
        public List<PromoConfig>? Promozioni { get; set; }
        public List<RuleConfig>? Regole { get; set; }
        public List<ShowcaseConfig>? Vetrine { get; set; }
        /// <summary>Solo nome, punto di messa a fuoco e "ha già le scritte" dei file che ci sono: si caricano e si tolgono a parte.</summary>
        public List<MediaItem>? Media { get; set; }
    }

    public static void Map(RouteGroupBuilder bo)
    {
        bo.MapGet("/config", (HttpContext context, SettingsStore settings, AdminAccess admin) =>
            Json(ForUi(settings, admin.IsTechnician(context), admin.Role(context))));

        bo.MapPut("/marchi/{id}", async (string id, HttpContext context, SettingsStore settings, ILogger<SettingsStore> log) =>
        {
            if (await Read<BrandPatch>(context, MaxBrandBody) is not { } patch)
                return Error("Dati non validi.");
            var error = settings.Update(s =>
            {
                if (s.Brand(id) is not { } brand)
                    return "Il marchio non c'è più.";
                Apply(brand, patch);
                return null;
            });
            if (error != null)
                return Error(error);
            log.LogInformation("{Brand}: {Parts} cambiati dal back office ({Remote})", settings.Current.Brand(id)?.Nome, Parts(patch), AgentApp.Remote(context));
            return Ok(new JsonObject { ["stati"] = States(settings.Current, settings.Current.Brand(id)!, DateTime.Now) });
        });

        // La bozza com'è: il cfg che riceverebbe adesso uno schermo della cassa e gli errori che la farebbero rifiutare.
        // Con ?vetrina= quella vetrina nei suoi posti a qualunque ora (si sta provando), con ?promo= solo quella promozione,
        // con ?schermo= le eccezioni di quello schermo.
        bo.MapPost("/anteprima", async (HttpContext context, SettingsStore settings, AgentEngine engine) =>
        {
            if (await Read<BrandPatch>(context, MaxBrandBody) is not { } patch)
                return Error("Dati non validi.");
            var preview = settings.Current.Clone();
            var brand = preview.Brand(patch.Marchio) ?? preview.Marchi[0];
            Apply(brand, patch);
            var cash = context.Request.Query["cassa"].ToString();
            if (preview.CashPoint(cash) is not { } cashPoint || preview.BrandOf(cashPoint).Id != brand.Id)
            {
                cash = preview.Casse.Find(c => c.Marchio == brand.Id)?.Id ?? "";
                if (cash.Length == 0)
                {
                    // Un marchio senza casse (appena creato): l'anteprima ha la sua cassa finta.
                    cash = "anteprima";
                    preview.Casse.Add(new CashPointConfig { Id = cash, Nome = "Anteprima", Marchio = brand.Id });
                }
            }
            var screen = preview.Screen(context.Request.Query["schermo"]);
            var clock = DateTime.Now;
            var config = ScreenConfig.Build(preview, cash, clock, engine.CachedQr, new ScreenConfig.Preview(
                Optional(context.Request.Query["vetrina"]), Optional(context.Request.Query["promo"])));
            var message = ScreenConfig.ForScreen(new ConfigMessage(config).ToJson(), patch.Override ?? screen?.Override, canPrint: true);
            return Json(new JsonObject
            {
                ["cfg"] = message,
                ["problemi"] = new JsonArray(preview.BrandProblems(brand).Select(p => (JsonNode)p).ToArray()),
                ["stati"] = States(preview, brand, clock),
                // Il QR del pagamento finto dell'anteprima: porta a example.com, come quello della stampa di prova.
                ["anteprimaQr"] = engine.CachedQr("https://example.com/bluetech-display/anteprima"),
            });
        });

        bo.MapPost("/media", async (HttpContext context, MediaLibrary media, AdminAccess admin) =>
        {
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                limit.MaxRequestBodySize = MediaLibrary.MaxVideoBytes + 1024 * 1024;
            if (context.Request.ContentLength > MediaLibrary.MaxVideoBytes)
                return Error($"Il file è troppo grande: un video arriva al massimo a {MediaLibrary.MaxVideoBytes / 1024 / 1024} MB.", 413);
            var target = Target(context);
            if (target == MediaLibrary.Kit && !admin.IsTechnician(context))
                return Error("I loghi dei kit li carica il tecnico.", 403);
            try
            {
                var (item, error) = await media.SaveAsync(context.Request.Body, context.Request.Query["nome"], target, context.RequestAborted,
                    Optional(context.Request.Query["poster"]), context.Request.Query["sfocato"] == "1");
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
        bo.MapDelete("/media/{id}", (string id, HttpContext context, MediaLibrary media, AdminAccess admin) =>
        {
            var target = Target(context);
            if (target == MediaLibrary.Kit && !admin.IsTechnician(context))
                return Error("I loghi dei kit li toglie il tecnico.", 403);
            return Result(media.Delete(id, target));
        });

        // Le casse: il titolare cambia il nome e le scritte della cassa, il tecnico anche il file e il marchio.
        bo.MapPut("/casse/{id}", async (string id, HttpContext context, SettingsStore settings, AgentRuntime runtime, AdminAccess admin) =>
        {
            if (await Read<CashPointConfig>(context) is not { } changed)
                return Error("Dati non validi.");
            var technician = admin.IsTechnician(context);
            changed.FileCassa = InstallationApi.CleanFile(changed.FileCassa);
            if (technician && InstallationApi.UnsafeCashFile(changed.FileCassa, runtime, settings.Current, id) is { } unsafeFile)
                return Error(unsafeFile);
            return Result(settings.Update(s =>
            {
                if (s.CashPoint(id) is not { } cash)
                    return "La cassa non c'è più.";
                cash.Nome = Clean(changed.Nome);
                var texts = changed.Override?.Scritte;
                cash.Override = texts == null || (Optional(texts.Benvenuto) ?? Optional(texts.Scorrevole) ?? Optional(texts.TitoloConto) ?? Optional(texts.Grazie)) == null
                    ? null
                    : new CashOverride
                    {
                        Scritte = new TextsOverride
                        {
                            Benvenuto = Optional(texts.Benvenuto),
                            Scorrevole = Optional(texts.Scorrevole),
                            TitoloConto = Optional(texts.TitoloConto),
                            Grazie = Optional(texts.Grazie),
                        },
                    };
                if (technician)
                {
                    cash.FileCassa = changed.FileCassa;
                    cash.Marchio = Optional(changed.Marchio) ?? cash.Marchio;
                }
                return null;
            }));
        });

        bo.MapPost("/schermi", async (HttpContext context, DeviceAccess devices, SettingsStore settings, ILogger<DeviceAccess> log) =>
        {
            var body = await Body(context);
            var error = devices.Approve(Text(body, "codice"), Text(body, "nome"), Text(body, "cassa"), Text(body, "stampante"), out var screen);
            if (error != null)
                return Error(error);
            var now = settings.Current;
            log.LogInformation("Schermo {Name} abbinato dal back office: mostra {CashPoint}, stampa su {Printer}",
                screen!.Nome, now.CashPoint(screen.Cassa)?.Nome ?? screen.Cassa, now.Printer(screen.Stampante)?.Nome ?? "nessuna stampante");
            return Ok(new JsonObject { ["id"] = screen.Id });
        });
        bo.MapPut("/schermi/{id}", async (string id, HttpContext context, SettingsStore settings) =>
        {
            if (await Read<ScreenSettings>(context) is not { } changed)
                return Error("Dati non validi.");
            return Result(settings.Update(s =>
            {
                if (s.Screen(id) is not { } screen)
                    return "Lo schermo non c'è più.";
                screen.Nome = Clean(changed.Nome);
                screen.Cassa = Clean(changed.Cassa);
                screen.Stampante = Optional(changed.Stampante);
                var o = changed.Override;
                screen.Override = o == null ? null : new ScreenOverride
                {
                    Disposizione = Optional(o.Disposizione),
                    Animazioni = Optional(o.Animazioni),
                    Attrazione = Optional(o.Attrazione),
                    ColonneConto = o.ColonneConto,
                    ColonnaAllineamento = Optional(o.ColonnaAllineamento),
                };
                if (screen.Override is { IsEmpty: true })
                    screen.Override = null;
                return null;
            }));
        });
        bo.MapDelete("/schermi/{id}", (string id, SettingsStore settings, Statistics stats, ILogger<DeviceAccess> log) =>
        {
            string? name = null;
            var error = settings.Update(s =>
            {
                name = s.Screen(id)?.Nome;
                s.Schermi.RemoveAll(d => d.Id == id);
                return null;
            });
            if (error != null)
                return Error(error);
            stats.Forget(id);
            if (name != null)
                log.LogInformation("Schermo {Name} revocato dal back office", name);
            return Ok();
        });
    }

    /// <summary>"marchio=…" o "kit=1": dove va o da dove si toglie un file.</summary>
    static string Target(HttpContext context) =>
        context.Request.Query["kit"] == "1" ? MediaLibrary.Kit : context.Request.Query["marchio"].ToString();

    static string Parts(BrandPatch patch)
    {
        var parts = new List<string>();
        if (patch.Nome != null || patch.Identita != null || patch.Aspetto != null || patch.Schermo != null || patch.Scritte != null || patch.Vendita != null)
            parts.Add("il locale");
        if (patch.Contenuti?.Vetrine != null || patch.Contenuti?.Regole != null)
            parts.Add("le vetrine");
        if (patch.Contenuti?.Promozioni != null || patch.Contenuti?.Media != null)
            parts.Add("i contenuti");
        return parts.Count == 0 ? "niente" : string.Join(" e ", parts);
    }

    /// <summary>La bozza dentro il marchio, pulita: testi senza spazi in più, liste mai nulle, id validi e senza doppioni.</summary>
    static void Apply(BrandConfig brand, BrandPatch patch)
    {
        if (patch.Nome != null)
            brand.Nome = Clean(patch.Nome);
        if (patch.Identita is { } identity)
        {
            brand.Identita = new BrandIdentity
            {
                NomeLocale = Clean(identity.NomeLocale),
                Sottotitolo = Clean(identity.Sottotitolo),
                Logo = Optional(identity.Logo),
                LogoChiaro = Optional(identity.LogoChiaro),
            };
        }
        if (patch.Aspetto is { } look)
        {
            look.Tema = Clean(look.Tema);
            look.Modo = Clean(look.Modo);
            look.GiornoDalle = Clean(look.GiornoDalle);
            look.GiornoAlle = Clean(look.GiornoAlle);
            look.FontTitoli = Optional(look.FontTitoli);
            look.FontTesto = Optional(look.FontTesto);
            look.Footer = Optional(look.Footer);
            look.Fondo = Clean(look.Fondo);
            look.FondoMedia = Optional(look.FondoMedia);
            if (look.Colori is { } c)
            {
                foreach (var property in typeof(ThemeColorSettings).GetProperties())
                    property.SetValue(c, Clean((string?)property.GetValue(c)));
            }
            brand.Aspetto = look;
        }
        if (patch.Schermo is { } screen)
        {
            screen.Animazioni = Clean(screen.Animazioni);
            screen.Attrazione = Clean(screen.Attrazione);
            screen.ColonnaAllineamento = Clean(screen.ColonnaAllineamento);
            screen.QrPagamento = Clean(screen.QrPagamento);
            brand.Schermo = screen;
        }
        if (patch.Scritte is { } texts)
        {
            texts.Benvenuto = Clean(texts.Benvenuto);
            texts.Scorrevole = Clean(texts.Scorrevole);
            texts.TitoloConto = Clean(texts.TitoloConto);
            texts.Grazie = Clean(texts.Grazie);
            brand.Scritte = texts;
        }
        if (patch.Vendita is { } sale)
        {
            sale.ScontrinoDigitale ??= new DigitalReceiptConfig();
            sale.ScontrinoDigitale.Dicitura = Clean(sale.ScontrinoDigitale.Dicitura);
            sale.ScontrinoDigitale.Blocchi = (sale.ScontrinoDigitale.Blocchi ?? []).Where(b => b != null).ToList();
            brand.Vendita = sale;
        }
        if (patch.Contenuti is not { } content)
            return;
        if (content.Media is { } media)
        {
            foreach (var changed in media.Where(m => m?.Id != null))
            {
                if (brand.Media(changed.Id) is not { } item)
                    continue;
                if (Clean(changed.Nome) is { Length: > 0 } name)
                    item.Nome = name.Length > 80 ? name[..80] : name;
                item.Focus = changed.Focus == null ? null : new FocusPoint { X = changed.Focus.X, Y = changed.Focus.Y };
                item.HaScritte = changed.HaScritte;
            }
        }
        if (content.Promozioni is { } promos)
        {
            promos.RemoveAll(p => p == null);
            foreach (var promo in promos)
            {
                promo.Nome = Clean(promo.Nome);
                promo.Titolo = Clean(promo.Titolo);
                promo.Testo = Clean(promo.Testo);
                promo.QrDidascalia = Clean(promo.QrDidascalia);
                promo.Qr = Optional(promo.Qr);
                promo.Immagine = Optional(promo.Immagine);
                promo.Etichetta = Clean(promo.Etichetta) is { Length: > 0 } label ? label : "nessuna";
                promo.Modello = Clean(promo.Modello).ToUpperInvariant();
            }
            FixIds(promos, "promo", p => p.Id, (p, id) => p.Id = id);
            brand.Contenuti.Promozioni = promos;
        }
        if (content.Regole is { } rules)
        {
            rules.RemoveAll(r => r == null);
            foreach (var rule in rules)
            {
                rule.Nome = Clean(rule.Nome);
                rule.Casse = (rule.Casse ?? []).Where(c => c != null).Distinct().ToList();
                rule.Giorni = (rule.Giorni ?? []).Distinct().Order().ToList();
                rule.Dalle = Optional(rule.Dalle);
                rule.Alle = Optional(rule.Alle);
            }
            FixIds(rules, "regola", r => r.Id, (r, id) => r.Id = id);
            brand.Contenuti.Regole = rules;
        }
        if (content.Vetrine is { } showcases)
        {
            showcases.RemoveAll(v => v == null);
            foreach (var showcase in showcases)
            {
                showcase.Nome = Clean(showcase.Nome);
                showcase.Rotazione = Clean(showcase.Rotazione);
                showcase.Posti = (showcase.Posti ?? []).Where(d => d != null).Distinct().ToList();
                showcase.Elementi = (showcase.Elementi ?? []).Where(e => e is { Id: not null }).ToList();
                showcase.Regola = Optional(showcase.Regola);
            }
            FixIds(showcases, "vetrina", v => v.Id, (v, id) => v.Id = id);
            brand.Contenuti.Vetrine = showcases;
        }
    }

    /// <summary>
    /// Gli elementi nuovi arrivano con l'id già dato dal back office (una vetrina nuova può usare una regola nuova);
    /// senza, o doppio, il prossimo libero.
    /// </summary>
    static void FixIds<T>(List<T> items, string prefix, Func<T, string?> get, Action<T, string> set)
    {
        var seen = new HashSet<string>();
        foreach (var item in items)
        {
            var id = get(item);
            if (string.IsNullOrWhiteSpace(id) || !AgentSettings.IdPattern().IsMatch(id) || !seen.Add(id))
            {
                var fresh = AgentSettings.NextId(prefix, items.Select(get).OfType<string>().Concat(seen));
                set(item, fresh);
                seen.Add(fresh);
            }
        }
    }

    /// <summary>Com'è ogni vetrina del marchio adesso, con le parole del pannello (SPEC §18).</summary>
    internal static JsonObject States(AgentSettings settings, BrandConfig brand, DateTime now)
    {
        var result = new JsonObject();
        foreach (var (id, overview) in Showcases.Overviews(settings, brand, now))
        {
            result[id] = new JsonObject
            {
                ["stato"] = overview.Stato,
                ["dove"] = new JsonArray(overview.Dove.Select(d => (JsonNode)new JsonObject { ["cassa"] = d.Cassa, ["posto"] = d.Posto }).ToArray()),
                ["da"] = new JsonArray(overview.CopertaDa.Select(d => (JsonNode)JsonValue.Create(d)!).ToArray()),
                ["torna"] = overview.Torna?.ToString("yyyy-MM-ddTHH:mm"),
                ["quando"] = overview.Torna is { } back ? Showcases.When(back, now) : null,
                ["nessunaCassa"] = overview.NessunaCassa,
            };
        }
        return result;
    }

    /// <summary>
    /// Le impostazioni per il back office, senza segreti (hash delle password, token degli schermi, password del
    /// certificato). Al titolare senza l'installazione, i file delle casse e il dettaglio delle stampanti.
    /// </summary>
    static JsonObject ForUi(SettingsStore store, bool technician, string? role)
    {
        var settings = store.Current;
        var clock = DateTime.Now;
        var json = new JsonObject
        {
            ["ruolo"] = role,
            ["installazione"] = technician,
            ["demo"] = settings.Installazione.Demo,
            ["ora"] = clock.ToString("yyyy-MM-ddTHH:mm:ss"),
            ["Marchi"] = JsonSerializer.SerializeToNode(settings.Marchi, AgentSettings.Json),
        };
        var cashPoints = JsonSerializer.SerializeToNode(settings.Casse, AgentSettings.Json)!.AsArray();
        var screens = JsonSerializer.SerializeToNode(settings.Schermi, AgentSettings.Json)!.AsArray();
        foreach (var screen in screens.OfType<JsonObject>())
            screen.Remove("Token");
        json["Casse"] = cashPoints;
        json["Schermi"] = screens;
        if (technician)
        {
            json["Stampanti"] = JsonSerializer.SerializeToNode(settings.Stampanti, AgentSettings.Json);
            var install = JsonSerializer.SerializeToNode(settings.Installazione, AgentSettings.Json)!.AsObject();
            install["Accessi"] = new JsonArray(settings.Installazione.Accessi.Select(a => (JsonNode)new JsonObject { ["Ruolo"] = a.Ruolo }).ToArray());
            var https = install["Rete"]!["Https"]!.AsObject();
            https["PasswordCertificatoImpostata"] = settings.Installazione.Rete.Https.PasswordCertificato.Length > 0;
            https.Remove("PasswordCertificato");
            json["Installazione"] = install;
            json["File"] = store.FilePath;
        }
        else
        {
            foreach (var cash in cashPoints.OfType<JsonObject>())
                cash.Remove("FileCassa");
            json["Stampanti"] = new JsonArray(settings.Stampanti.Select(p => (JsonNode)new JsonObject { ["Id"] = p.Id, ["Nome"] = p.Nome }).ToArray());
        }
        json["Stati"] = new JsonObject(settings.Marchi.Select(b => KeyValuePair.Create(b.Id, (JsonNode?)States(settings, b, clock))));
        json["Temi"] = JsonSerializer.SerializeToNode(Palettes.Themes, AgentSettings.Json);
        json["Font"] = new JsonArray(Palettes.Fonts.Keys.Select(f => (JsonNode)f).ToArray());
        json["Limiti"] = new JsonObject
        {
            ["ImmagineMb"] = MediaLibrary.MaxImageBytes / 1024 / 1024,
            ["VideoMb"] = MediaLibrary.MaxVideoBytes / 1024 / 1024,
        };
        return json;
    }
}
