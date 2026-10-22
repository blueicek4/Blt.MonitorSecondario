using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bluetech.Display.Agent.Cassa;
using Bluetech.Display.Agent.Impostazioni;
using Bluetech.Display.Agent.Stampa;
using Bluetech.Display.Agent.Vendita;
using Microsoft.AspNetCore.Hosting.Server;
using static Bluetech.Display.Agent.Server.BackOfficeApi;

namespace Bluetech.Display.Agent.Server;

/// <summary>
/// La sezione Installazione del back office, solo per il tecnico (SPEC §20): rete e HTTPS, accesso dalla rete, modalità
/// demo, i loghi dei kit, le casse con il file del gestionale, le stampanti, i marchi. Al titolare queste API rispondono
/// 403: la sezione per lui non esiste.
/// </summary>
public static class InstallationApi
{
    static readonly Lazy<string> TestQr = new(() =>
    {
        using var stream = typeof(InstallationApi).Assembly.GetManifestResourceStream("qr-prova.txt")!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return ReceiptParser.Parse(memory.ToArray()).QrDataUri!;
    });

    sealed class InstallationUpdate
    {
        public NetworkSettings? Rete { get; set; }
        /// <summary>Null = com'era.</summary>
        public bool? BackOfficeDallaRete { get; set; }
        public bool? Demo { get; set; }
    }

    public static void Map(RouteGroupBuilder tech)
    {
        tech.MapPut("/installazione", async (HttpContext context, SettingsStore settings, IServer server, ILogger<SettingsStore> log) =>
        {
            if (await Read<InstallationUpdate>(context) is not { } update)
                return Error("Dati non validi.");
            var install = settings.Current.Installazione;
            var network = update.Rete;
            var certificate = "";
            if (network != null)
            {
                network.Https ??= new HttpsSettings();
                if (network.Porta is < 1 or > 65535 || network.Https.Porta is < 1 or > 65535)
                    return Error("Le porte vanno da 1 a 65535.");
                certificate = Clean(network.Https.Certificato);
                if (network.Https.Attivo && certificate.Length > 0
                    && !File.Exists(Path.IsPathFullyQualified(certificate) ? certificate : Path.Combine(AppContext.BaseDirectory, certificate)))
                    return Error($"Il certificato {certificate} non c'è.");
                // Una porta occupata da un altro programma terrebbe l'Agent fermo al prossimo avvio: meglio dirlo adesso.
                var bound = BoundPorts(server);
                foreach (var (port, name) in new[] { (network.Porta, "HTTP"), (network.Https.Attivo ? network.Https.Porta : 0, "HTTPS") })
                {
                    if (port != 0 && !bound.Contains(port) && !AgentApp.PortAvailable(port, network.ApertoInRete))
                        return Error($"La porta {name} {port} è già usata da un altro programma su questo PC.");
                }
            }
            var fromNetwork = update.BackOfficeDallaRete ?? install.BackOfficeDallaRete;
            var opened = fromNetwork != install.BackOfficeDallaRete;
            var demo = update.Demo ?? install.Demo;
            var error = settings.Update(s =>
            {
                s.Installazione.BackOfficeDallaRete = fromNetwork;
                s.Installazione.Demo = demo;
                if (network != null)
                {
                    var password = network.Https.PasswordCertificato ?? s.Installazione.Rete.Https.PasswordCertificato;   // null = resta quella di prima
                    s.Installazione.Rete = network;
                    s.Installazione.Rete.Https.Certificato = certificate;
                    s.Installazione.Rete.Https.PasswordCertificato = password;
                }
                return null;
            });
            if (error != null)
                return Error(error);
            log.LogInformation("Installazione cambiata dal back office");
            if (demo != install.Demo)
                log.LogWarning("Modalità demo {State} da {Remote}", demo ? "accesa: QR finto dello scontrino digitale dopo ogni conto pagato" : "spenta", AgentApp.Remote(context));
            if (opened && fromNetwork)
                log.LogWarning("Back office aperto anche alla rete da {Remote}", AgentApp.Remote(context));
            else if (opened)
                log.LogWarning("Back office di nuovo solo dal PC dell'Agent ({Remote})", AgentApp.Remote(context));
            return Ok();
        });

        tech.MapPut("/installazione/loghi", async (HttpContext context, SettingsStore settings) =>
        {
            if (await Read<KitLogos>(context) is not { } logos)
                return Error("Dati non validi.");
            return Result(settings.Update(s =>
            {
                s.Installazione.Loghi = new KitLogos
                {
                    Bluetech = Optional(logos.Bluetech),
                    BluetechChiaro = Optional(logos.BluetechChiaro),
                    Satispay = Optional(logos.Satispay),
                    SatispayChiaro = Optional(logos.SatispayChiaro),
                };
                return null;
            }));
        });

        tech.MapPost("/casse", async (HttpContext context, SettingsStore settings, Statistics stats, AgentRuntime runtime) =>
        {
            if (await Read<CashPointConfig>(context) is not { } cash)
                return Error("Dati non validi.");
            cash.Nome = Clean(cash.Nome);
            cash.FileCassa = CleanFile(cash.FileCassa);
            if (UnsafeCashFile(cash.FileCassa, runtime, settings.Current, null) is { } unsafeFile)
                return Error(unsafeFile);
            var used = stats.CashPointIds();
            var error = settings.Update(s =>
            {
                cash.Id = AgentSettings.NextId("cassa", s.Casse.Select(c => c.Id).Concat(used));   // un id nuovo non eredita i conteggi di una cassa tolta
                cash.Marchio = s.Brand(cash.Marchio)?.Id ?? s.Marchi[0].Id;
                cash.Override = null;
                s.Casse.Add(cash);
                return null;
            });
            return error == null ? Ok(new JsonObject { ["id"] = cash.Id }) : Error(error);   // il motore scrive nel log la cassa nuova
        });
        tech.MapDelete("/casse/{id}", (string id, SettingsStore settings) => Result(settings.Update(s =>
        {
            if (s.CashPoint(id) is not { } cash)
                return null;
            var users = s.Schermi.Where(d => d.Cassa == id).Select(d => d.Nome).ToList();
            if (users.Count > 0)
                return $"{cash.Nome} è mostrata da {string.Join(", ", users)}: sposta prima gli schermi su un'altra cassa.";
            if (s.Casse.Count == 1)
                return "Serve almeno una cassa.";
            s.Casse.Remove(cash);
            foreach (var brand in s.Marchi)
            {
                foreach (var rule in brand.Contenuti.Regole.Where(r => r.Casse.Remove(id) && r.Casse.Count == 0))
                {
                    // Era solo per questa cassa: le sue vetrine non devono passare a tutte.
                    foreach (var showcase in brand.Contenuti.Vetrine.Where(v => v.Regola == rule.Id))
                        showcase.Attiva = false;
                }
            }
            return null;
        })));

        tech.MapPost("/stampanti", async (HttpContext context, SettingsStore settings) =>
        {
            if (await Read<PrinterConfig>(context) is not { } printer)
                return Error("Dati non validi.");
            var error = settings.Update(s =>
            {
                printer.Id = AgentSettings.NextId("stampante", s.Stampanti.Select(p => p.Id));
                Tidy(printer);
                s.Stampanti.Add(printer);
                return null;
            });
            return error == null ? Ok(new JsonObject { ["id"] = printer.Id }) : Error(error);
        });
        tech.MapPut("/stampanti/{id}", async (string id, HttpContext context, SettingsStore settings) =>
        {
            if (await Read<PrinterConfig>(context) is not { } changed)
                return Error("Dati non validi.");
            return Result(settings.Update(s =>
            {
                var index = s.Stampanti.FindIndex(p => p.Id == id);
                if (index < 0)
                    return "La stampante non c'è più.";
                changed.Id = id;
                Tidy(changed);
                s.Stampanti[index] = changed;
                return null;
            }));
        });
        tech.MapDelete("/stampanti/{id}", (string id, SettingsStore settings) => Result(settings.Update(s =>
        {
            if (s.Printer(id) is not { } printer)
                return null;
            var users = s.Schermi.Where(d => d.Stampante == id).Select(d => d.Nome).ToList();
            if (users.Count > 0)
                return $"{printer.Nome} è usata da {string.Join(", ", users)}: togli prima la stampante dagli schermi.";
            s.Stampanti.Remove(printer);
            return null;
        })));
        tech.MapPost("/stampanti/{id}/prova", async (string id, SettingsStore settings, AgentRuntime runtime, ILogger<AgentEngine> log) =>
        {
            var now = settings.Current;
            if (now.Printer(id) is not { } config)
                return Error("La stampante non c'è più.", 404);
            var printOptions = config.ToOptions(now.Marchi[0].Vendita.ScontrinoDigitale.Dicitura);
            if (CopyPrinters.Create(printOptions, runtime.LogFolder) is not { } printer)
                return Error("Manca l'indirizzo della stampante.");
            var blocks = PaperCopy.Compose(TestReceipt(config), printOptions);
            var text = PaperCopy.ToText(blocks, printOptions.Colonne);
            var watch = Stopwatch.StartNew();
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(printOptions.TimeoutSecondi, 1, 60)));
                await printer.PrintAsync(EscPos.Encode(blocks, printOptions), text, "PROVA", timeout.Token);
            }
            catch (Exception ex)
            {
                var problem = ex is OperationCanceledException ? "la stampante non risponde" : ex.Message;
                log.LogWarning("Stampa di prova su {Printer} non riuscita: {Problem}", printer.Description, problem);
                return Error($"Non stampata: {problem}.");
            }
            log.LogInformation("Stampa di prova inviata a {Printer} in {Ms} ms", printer.Description, watch.ElapsedMilliseconds);
            return Ok(new JsonObject { ["ms"] = watch.ElapsedMilliseconds, ["testo"] = text, ["stampante"] = printer.Description });
        });

        // Un marchio nuovo parte dai valori di sempre, con il nome del locale già scritto: il titolare lo veste da Il locale.
        tech.MapPost("/marchi", async (HttpContext context, SettingsStore settings, ILogger<SettingsStore> log) =>
        {
            var body = await Body(context);
            var name = Clean(Text(body, "nome"));
            BrandConfig? brand = null;
            var error = settings.Update(s =>
            {
                var from = s.Brand(Text(body, "copiaDa"));
                brand = from == null
                    ? new BrandConfig()
                    : JsonSerializer.Deserialize<BrandConfig>(JsonSerializer.Serialize(from, AgentSettings.Json), AgentSettings.Json)!;
                brand.Id = AgentSettings.NextId("marchio", s.Marchi.Select(m => m.Id));
                brand.Nome = name;
                if (from == null)
                    brand.Identita.NomeLocale = name;
                s.Marchi.Add(brand);
                return null;
            });
            if (error != null)
                return Error(error);
            log.LogInformation("Marchio {Name} aggiunto dal back office", name);
            return Ok(new JsonObject { ["id"] = brand!.Id });
        });
        tech.MapDelete("/marchi/{id}", (string id, SettingsStore settings) => Result(settings.Update(s =>
        {
            if (s.Brand(id) is not { } brand)
                return null;
            if (s.Marchi.Count == 1)
                return "Serve almeno un marchio.";
            var users = s.Casse.Where(c => c.Marchio == id).Select(c => c.Nome).ToList();
            if (users.Count > 0)
                return $"{brand.Nome} è il marchio di {string.Join(", ", users)}: dai prima un altro marchio a queste casse.";
            s.Marchi.Remove(brand);
            return null;
        })));
    }

    internal static string CleanFile(string? file) => (file ?? "").Trim().Trim('"');

    /// <summary>
    /// L'Agent cancella il file della cassa dopo averlo letto: non deve poter essere un suo file (impostazioni, log,
    /// display) né un file che c'è già e non sembra quello del monitor (un percorso sbagliato lo cancellerebbe).
    /// </summary>
    internal static string? UnsafeCashFile(string file, AgentRuntime runtime, AgentSettings current, string? cashId)
    {
        if (string.IsNullOrWhiteSpace(file) || !Path.IsPathFullyQualified(file))
            return null;   // lo dice la validazione
        var full = Path.GetFullPath(file);
        if (current.CashPoint(cashId)?.FileCassa is { } unchanged && string.Equals(Path.GetFullPath(unchanged), full, StringComparison.OrdinalIgnoreCase))
            return null;   // il file di prima: è già sorvegliato
        foreach (var folder in new[] { runtime.DataFolder, runtime.LogFolder, runtime.DisplayFolder })
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return $"Il file della cassa non può stare nelle cartelle dell'Agent ({root.TrimEnd(Path.DirectorySeparatorChar)}): l'Agent lo cancella dopo averlo letto.";
        }
        try
        {
            if (!File.Exists(full))
                return null;
            var length = new FileInfo(full).Length;
            if (length > CashFileSource.MaxFileBytes
                || (length > 0 && ReceiptParser.Parse(File.ReadAllBytes(full)).Kind == CashFileKind.Message))
                return $"C'è già un file {full} che non sembra quello del monitor di Passepartout: l'Agent lo cancellerebbe. Controlla il percorso.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Non riesco a leggere {full}: {ex.Message}";
        }
        return null;
    }

    static void Tidy(PrinterConfig printer)
    {
        printer.Nome = Clean(printer.Nome);
        printer.Collegamento = Clean(printer.Collegamento).ToLowerInvariant();
        printer.Indirizzo = Clean(printer.Indirizzo);
        printer.TabellaCaratteri = Clean(printer.TabellaCaratteri).ToLowerInvariant();
        printer.TestoRitiro = Clean(printer.TestoRitiro);
    }

    /// <summary>Uno scontrino finto con accenti, euro, sconto, resto e un QR di prova (porta a example.com).</summary>
    static DigitalReceipt TestReceipt(PrinterConfig printer) => new()
    {
        Id = "PROVA",
        Png = TestQr.Value,
        Caption = "Stampa di prova",
        Bill = new CashFile
        {
            Kind = CashFileKind.Receipt,
            Hash = "prova",
            IsBill = true,
            Header = ["STAMPA DI PROVA", printer.Nome, "Bluetech Display Agent"],
            Entries =
            [
                new ItemEntry("Caffè", 2, 240, 120),
                new AdjustmentEntry("SCONTO", -40, true),
                new ItemEntry("Accenti: àèéìòù €", 1, 0, null),
            ],
            Total = 200,
            Payment = new Payment("CONTANTI", 500, 300),
        },
        IssuedAt = DateTimeOffset.Now,
        PaperCopy = true,
    };
}
