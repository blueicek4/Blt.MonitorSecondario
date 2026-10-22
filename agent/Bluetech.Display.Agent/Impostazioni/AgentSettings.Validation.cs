using Bluetech.Display.Agent.Aspetto;

namespace Bluetech.Display.Agent.Impostazioni;

public sealed partial class AgentSettings
{
    /// <summary>Il primo errore, in parole per chi usa il back office; null se va tutto bene.</summary>
    public string? Validate() => Problems().FirstOrDefault();

    /// <summary>
    /// Tutti gli errori. Una modifica dal back office è rifiutata solo se ne aggiunge uno: un errore che c'era già (file
    /// scritto a mano) non deve impedire di cambiare il resto, per esempio la password.
    /// </summary>
    public IEnumerable<string> Problems()
    {
        var install = Installazione;
        if (install.Rete.Porta is < 0 or > 65535)
            yield return "La porta deve essere tra 1 e 65535.";
        if (install.Rete.Https.Porta is < 0 or > 65535)
            yield return "La porta HTTPS deve essere tra 1 e 65535.";
        if (install.Rete.Https.Attivo && install.Rete.Https.Porta != 0 && install.Rete.Https.Porta == install.Rete.Porta)
            yield return "La porta HTTPS deve essere diversa da quella HTTP.";
        if (install.Accessi.Any(a => !AccessEntry.Roles.Contains(a.Ruolo)))
            yield return "Gli accessi sono del titolare o del tecnico.";
        if (install.Accessi.GroupBy(a => a.Ruolo).Any(g => g.Count() > 1))
            yield return "Una password sola per ruolo.";
        foreach (var (name, get, _) in KitLogos.All)
        {
            if (get(install.Loghi) is { } logo && install.Media.Find(m => m.Id == logo) is not { IsVideo: false })
                yield return $"Marchi dei kit: {name} è un'immagine (PNG con lo sfondo trasparente, per esempio).";
        }

        if (Marchi.Count == 0)
            yield return "Serve almeno un marchio.";
        if (Marchi.Count > 10)
            yield return "Al massimo 10 marchi.";
        foreach (var brand in Marchi)
        {
            foreach (var problem in BrandProblems(brand))
                yield return Marchi.Count > 1 ? $"{brand.Nome}: {problem}" : problem;
        }

        if (Casse.Count > 20)
            yield return "Al massimo 20 casse.";
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cash in Casse)
        {
            if (Name(cash.Nome, "La cassa") is { } error)
                yield return error;
            if (Brand(cash.Marchio) == null)
                yield return $"{cash.Nome}: scegli il marchio.";
            var file = cash.FileCassa ?? "";
            if (string.IsNullOrWhiteSpace(file) || !Path.IsPathFullyQualified(file) || Path.GetFileName(file).Length == 0)
                yield return $"{cash.Nome}: il file deve essere un percorso completo, per esempio C:\\MonitorSecondario\\Scontrino.txt.";
            else if (!string.Equals(Path.GetExtension(file), ".txt", StringComparison.OrdinalIgnoreCase))
                yield return $"{cash.Nome}: il file del monitor di Passepartout è un .txt (per esempio Scontrino.txt). L'Agent lo cancella dopo averlo letto: un altro file non va bene.";
            else if (!files.Add(Path.GetFullPath(file)))
                yield return $"{cash.Nome}: il file {file} è già di un'altra cassa. Ogni cassa ha il suo file.";
            if (cash.Override?.Scritte is { } texts)
            {
                foreach (var problem in TextProblems(texts.Benvenuto, texts.Scorrevole, texts.TitoloConto, texts.Grazie))
                    yield return $"{cash.Nome}: {problem}";
            }
        }

        if (Stampanti.Count > 50)
            yield return "Al massimo 50 stampanti.";
        foreach (var printer in Stampanti)
        {
            if (Name(printer.Nome, "La stampante") is { } error)
                yield return error;
            var address = printer.Indirizzo?.Trim() ?? "";
            if (printer.Collegamento is not ("rete" or "windows" or "prova"))
                yield return $"{printer.Nome}: il collegamento è \"rete\", \"windows\" o \"prova\".";
            else if (printer.Collegamento == "rete" && !NetworkAddress().IsMatch(address))
                yield return $"{printer.Nome}: l'indirizzo della stampante in rete è come 192.168.1.50 o 192.168.1.50:9100.";
            else if (printer.Collegamento == "windows" && address.Length == 0)
                yield return $"{printer.Nome}: manca il nome della stampante in Windows.";
            if (printer.LarghezzaMm is not (58 or 80))
                yield return $"{printer.Nome}: il rotolo è da 58 o da 80 mm.";
            if (printer.Colonne is < 24 or > 64)
                yield return $"{printer.Nome}: le colonne vanno da 24 a 64.";
            if (printer.TabellaCaratteri is not ("1252" or "858" or "ascii"))
                yield return $"{printer.Nome}: la tabella caratteri è 1252, 858 o ascii.";
            if (printer.QrPuntiPerModulo is < 1 or > 10)
                yield return $"{printer.Nome}: i punti per modulo del QR vanno da 1 a 10.";
            if (printer.TimeoutSecondi is < 1 or > 60)
                yield return $"{printer.Nome}: l'attesa della stampante va da 1 a 60 secondi.";
            if ((printer.TestoRitiro?.Length ?? 0) > 80)
                yield return $"{printer.Nome}: il testo per il ritiro è troppo lungo (al massimo 80 caratteri).";
        }

        if (Schermi.Count > 200)
            yield return "Al massimo 200 schermi.";
        foreach (var screen in Schermi)
        {
            if (Name(screen.Nome, "Lo schermo") is { } error)
                yield return error;
            if (CashPoint(screen.Cassa) == null)
                yield return $"{screen.Nome}: scegli la cassa da mostrare.";
            if (screen.Stampante != null && Printer(screen.Stampante) == null)
                yield return $"{screen.Nome}: la stampante scelta non c'è.";
            if (screen.Override is { } o)
            {
                if (o.Disposizione != null && !ScreenOverride.Layouts.Contains(o.Disposizione))
                    yield return $"{screen.Nome}: la disposizione è automatica, larga, stretta, verticale o palmare.";
                if (o.Animazioni != null && !BrandScreen.MotionLevels.Contains(o.Animazioni))
                    yield return $"{screen.Nome}: le animazioni sono piena, leggera o eco.";
                if (o.Attrazione != null && !BrandScreen.AttractionLevels.Contains(o.Attrazione))
                    yield return $"{screen.Nome}: l'attrazione è sobrio, vivo, spettacolo o troppo.";
                if (o.ColonneConto is < 9 or > 17)
                    yield return $"{screen.Nome}: la larghezza del conto va da 9 a 17 colonne su 24.";
                if (o.ColonnaAllineamento != null && !BrandScreen.Alignments.Contains(o.ColonnaAllineamento))
                    yield return $"{screen.Nome}: la colonna accanto al conto sta in alto, al centro o in basso.";
            }
        }
    }

    /// <summary>Gli errori di un marchio, senza il nome davanti (lo aggiunge chi chiama quando i marchi sono più di uno).</summary>
    public IEnumerable<string> BrandProblems(BrandConfig brand)
    {
        if (Name(brand.Nome, "Il marchio") is { } error)
            yield return error;
        var id = brand.Identita;
        if ((id.NomeLocale?.Length ?? 0) > 40)
            yield return "Il locale: il nome è troppo lungo (al massimo 40 caratteri).";
        if ((id.Sottotitolo?.Length ?? 0) > 60)
            yield return "Il locale: il sottotitolo è troppo lungo (al massimo 60 caratteri).";
        foreach (var (name, get, _) in IdentityImages.Take(2))
        {
            if (get(brand) is { } logo && brand.Media(logo) is not { IsVideo: false })
                yield return $"Il locale: {name} è un'immagine (PNG con lo sfondo trasparente, per esempio).";
        }

        var look = brand.Aspetto;
        if (!Palettes.IsTheme(look.Tema))
            yield return $"Il locale: il tema \"{look.Tema}\" non c'è. Si sceglie tra {string.Join(", ", Palettes.Themes.Select(t => t.Nome))}.";
        if (!BrandLook.Modes.Contains(look.Modo))
            yield return "Il locale: giorno e notte è \"fisso\" o \"auto\".";
        if (look.Modo == "auto" && !(ParseTime(look.GiornoDalle) is { } dayStart && ParseTime(look.GiornoAlle) is { } dayEnd && dayStart < dayEnd))
            yield return "Il locale: il giorno va dalle 07:00 alle 19:00, per esempio (la prima ora prima della seconda).";
        if (LocaleColors(brand) is { } colors && Palettes.IsTheme(look.Tema))
        {
            var named = new (string Name, string Value)[]
            {
                ("il fondo", colors.Bg), ("il testo", colors.Text), ("il testo tenue", colors.Muted), ("il primario", colors.Primary),
                ("il testo sopra il primario", colors.OnPrimary), ("l'accento", colors.Accent), ("il secondario", colors.Secondary),
            };
            var wrong = named.Where(n => ThemeColors.ParseHex(n.Value) == null).Select(n => n.Name).ToList();
            if (ThemeColors.ParseColor(colors.Surface) == null)
                wrong.Add("la card");
            if (wrong.Count > 0)
            {
                yield return $"Il locale: {string.Join(", ", wrong)} non è un colore valido (come #1F6FEB).";
            }
            else
            {
                var basePalette = Palettes.Get(look.Tema);
                var failing = ThemeColors.Pairs(ThemeColors.WithLocaleColors(basePalette, colors)).Where(p => !p.Ok).ToList();
                if (failing.Count > 0)
                    yield return ContrastProblem(basePalette, colors, failing);
            }
        }
        if (look.FontTitoli != null && !Palettes.Fonts.ContainsKey(look.FontTitoli))
            yield return $"Il locale: il font dei titoli \"{look.FontTitoli}\" non c'è. Si sceglie tra {string.Join(", ", Palettes.Fonts.Keys)}.";
        if (look.FontTesto != null && !Palettes.Fonts.ContainsKey(look.FontTesto))
            yield return $"Il locale: il font del testo \"{look.FontTesto}\" non c'è. Si sceglie tra {string.Join(", ", Palettes.Fonts.Keys)}.";
        if (look.Raggio is < 0 or > 32)
            yield return "Il locale: gli angoli arrotondati vanno da 0 a 32 px.";
        if (look.Vetro is < 0 or > 100)
            yield return "Il locale: il vetro va da 0 a 100.";
        if (look.Footer != null && !BrandLook.Footers.Contains(look.Footer))
            yield return "Il locale: la firma Bluetech in basso è chiara (\"light\") o scura (\"dark\").";
        if (!BrandLook.Backgrounds.Contains(look.Fondo))
            yield return "Il locale: lo sfondo è luci, tinta, sfumato, immagine o video.";
        else if (look.Fondo is "immagine" or "video" && !(brand.Media(look.FondoMedia) is { } background && background.IsVideo == (look.Fondo == "video")))
            yield return look.Fondo == "video" ? "Il locale: per lo sfondo video scegli un video." : "Il locale: per lo sfondo a immagine scegli un'immagine.";

        var screen = brand.Schermo;
        if (!BrandScreen.MotionLevels.Contains(screen.Animazioni))
            yield return "Il locale: le animazioni sono piena, leggera o eco.";
        if (!BrandScreen.AttractionLevels.Contains(screen.Attrazione))
            yield return "Il locale: quanto il monitor si fa notare è sobrio, vivo, spettacolo o troppo.";
        if (screen.ColonneConto is < 9 or > 17)
            yield return "Il locale: la larghezza del conto va da 9 a 17 colonne su 24.";
        if (screen.ColonneContoPagamento is < 8 or > 14)
            yield return "Il locale: con il QR accanto, il conto va da 8 a 14 colonne su 24.";
        if (!BrandScreen.Alignments.Contains(screen.ColonnaAllineamento))
            yield return "Il locale: la colonna accanto al conto sta in alto, al centro o in basso.";
        if (!BrandScreen.QrLayouts.Contains(screen.QrPagamento))
            yield return "Il locale: il QR del pagamento sta accanto al conto o a tutto schermo.";

        var texts = brand.Scritte;
        foreach (var problem in TextProblems(texts.Benvenuto, texts.Scorrevole, texts.TitoloConto, texts.Grazie))
            yield return $"Il locale: {problem}";
        if (texts.SecondiGrazie is < 10 or > 120)
            yield return "Il locale: con un QR il Grazie resta tra 10 e 120 secondi.";

        var sale = brand.Vendita;
        if (sale.ChiusuraDopoSecondi is < 0 or > 3600)
            yield return "Il locale: la chiusura del conto è tra 0 (mai) e 3600 secondi.";
        var receipt = sale.ScontrinoDigitale;
        if (receipt.Secondi is < 5 or > 600)
            yield return "Scontrino digitale: resta a video tra 5 e 600 secondi.";
        if ((receipt.Dicitura?.Length ?? 0) > 200)
            yield return "Scontrino digitale: la dicitura della copia è troppo lunga (al massimo 200 caratteri).";
        if (ReceiptProblem(receipt.Blocchi) is { } layout)
            yield return $"Scontrino digitale: {layout}";

        foreach (var problem in ContentProblems(brand))
            yield return problem;
    }

    /// <summary>Le regole della griglia a sei posti (SPEC §2.7). Null se va.</summary>
    public static string? ReceiptProblem(List<ReceiptBlock> blocks)
    {
        if (blocks.Any(b => !ReceiptBlock.Types.Contains(b.Tipo) || !ReceiptBlock.Rows.Contains(b.Riga) || b.Colonna is < 1 or > 3))
            return "ogni blocco sta in una colonna da 1 a 3, in alto, in basso o a tutta altezza.";
        var on = blocks.Where(b => b.Attivo).ToList();
        if (on.Count == 0)
            return "accendi almeno un blocco: la carta, il QR o la reclame.";
        if (on.Any(b => b.Tipo == "carta" && b.Riga != "piena"))
            return "la carta occupa sempre tutta l'altezza della sua colonna.";
        foreach (var column in on.GroupBy(b => b.Colonna))
        {
            var rows = column.Select(b => b.Riga).ToList();
            if (rows.Count > 2 || (rows.Count == 2 && !(rows.Contains("alto") && rows.Contains("basso"))))
                return $"nella colonna {column.Key} due blocchi stanno solo uno in alto e uno in basso.";
        }
        return null;
    }

    static IEnumerable<string> TextProblems(string? welcome, string? ticker, string? receiptTitle, string? thanks)
    {
        foreach (var (text, label, max) in new[]
                 {
                     (welcome, "il benvenuto", 80), (ticker, "il testo scorrevole", 500), (receiptTitle, "il titolo del conto", 30), (thanks, "il saluto del Grazie", 60),
                 })
        {
            if ((text?.Length ?? 0) > max)
                yield return $"{label} è troppo lungo (al massimo {max} caratteri).";
        }
    }

    IEnumerable<string> ContentProblems(BrandConfig brand)
    {
        var c = brand.Contenuti;
        if (c.Media.Count > 500)
            yield return "Al massimo 500 foto e video.";
        var ids = new HashSet<string>();
        foreach (var item in c.Media)
        {
            if (!MediaIdPattern().IsMatch(item.Id ?? "") || !ids.Add(item.Id!))
                yield return $"Il file {item.Nome} ha un nome interno non valido ({item.Id}).";
            if (item.Focus is { } f && (f.X is < 0 or > 1 || f.Y is < 0 or > 1))
                yield return $"Il file {item.Nome}: il punto di messa a fuoco sta dentro la foto.";
        }

        if (c.Promozioni.Count > 200)
            yield return "Al massimo 200 promozioni.";
        foreach (var promo in c.Promozioni)
        {
            if (Name(promo.Nome, "La promozione") is { } error)
                yield return error;
            var what = $"La promozione {promo.Nome}";
            var image = brand.Media(promo.Immagine);
            if (promo.Immagine != null && image == null)
                yield return $"{what}: l'immagine non c'è più.";
            else if (image is { IsVideo: true })
                yield return $"{what}: ha un'immagine, non un video (il video si mette direttamente nella vetrina).";
            if (!promo.HasContent)
                yield return $"{what}: scrivi un titolo o un testo, o scegli un'immagine o un QR.";
            if (promo.HasQr && !(Uri.TryCreate(promo.Qr!.Trim(), UriKind.Absolute, out var url) && url.Scheme is "https" or "http"))
                yield return $"{what}: l'indirizzo del QR è come https://… (registrazione, recensioni, menu digitale).";
            if ((promo.Qr?.Length ?? 0) > 400)
                yield return $"{what}: l'indirizzo del QR è troppo lungo (al massimo 400 caratteri).";
            if ((promo.Titolo?.Length ?? 0) > 60 || (promo.Testo?.Length ?? 0) > 200 || (promo.QrDidascalia?.Length ?? 0) > 120)
                yield return $"{what}: il titolo arriva a 60 caratteri, il testo a 200, la scritta del QR a 120.";
            if (!PromoConfig.Models.Contains(promo.Modello))
                yield return $"{what}: il modello è A, B, C o D.";
            else if (promo.Modello == "D" && !promo.HasQr)
                yield return $"{what}: il modello D è quello con il QR: scrivi l'indirizzo o scegli un altro modello.";
            else if (promo.Modello is "B" or "C" && image == null)
                yield return $"{what}: i modelli B e C sono con la foto: sceglila, o passa al modello A.";
            if (!PromoConfig.Labels.Contains(promo.Etichetta))
                yield return $"{what}: l'etichetta si sceglie tra Promozione, Novità, Questa settimana, Il consiglio della casa, Evento, Diventa cliente o nessuna.";
            if (promo.DalGiorno > promo.AlGiorno)
                yield return $"{what}: l'ultimo giorno viene prima del primo.";
        }

        if (c.Regole.Count > 50)
            yield return "Al massimo 50 regole.";
        foreach (var rule in c.Regole)
        {
            if (Name(rule.Nome, "La regola") is { } error)
                yield return error;
            var what = $"La regola {rule.Nome}";
            foreach (var cash in rule.Casse.Where(id => CashPoint(id) == null))
                yield return $"{what}: la cassa {cash} non c'è.";
            if (rule.Giorni.Any(d => d is < 1 or > 7))
                yield return $"{what}: i giorni vanno da 1 (lunedì) a 7 (domenica).";
            var from = ParseTime(rule.Dalle);
            var to = ParseTime(rule.Alle);
            if ((rule.Dalle != null && from == null) || (rule.Alle != null && to == null) || (from == null) != (to == null))
                yield return $"{what}: gli orari sono come 18:00 e 20:00, tutti e due o nessuno.";
            else if (from != null && from == to)
                yield return $"{what}: l'ora di fine è uguale a quella di inizio.";
            if (rule.DalGiorno > rule.AlGiorno)
                yield return $"{what}: l'ultimo giorno viene prima del primo.";
        }

        if (c.Vetrine.Count > 30)
            yield return "Al massimo 30 vetrine.";
        foreach (var showcase in c.Vetrine)
        {
            if (Name(showcase.Nome, "La vetrina") is { } error)
                yield return error;
            var what = $"La vetrina {showcase.Nome}";
            if (showcase.Posti.Count == 0 || showcase.Posti.Any(d => !ShowcaseConfig.Places.Contains(d)))
                yield return $"{what}: scegli dove si vede: nell'attesa, accanto al conto o nel Grazie.";
            if (!ShowcaseConfig.Rotations.Contains(showcase.Rotazione))
                yield return $"{what}: l'ordine è elenco, casuale o alfabetico.";
            if (showcase.QrOgni is < 0 or > 20)
                yield return $"{what}: una promozione con il QR dopo 1-20 elementi (0 = come le altre).";
            if (showcase.Secondi is < 3 or > 120 || showcase.Elementi.Any(e => e.Secondi is < 3 or > 120))
                yield return $"{what}: una foto o una promozione resta tra 3 e 120 secondi.";
            if (showcase.Elementi.Count > 100)
                yield return $"{what}: al massimo 100 elementi.";
            foreach (var item in showcase.Elementi.Where(e => brand.Media(e.Id) == null && brand.Promo(e.Id) == null))
                yield return $"{what}: un elemento non c'è più ({item.Id}).";
            if (showcase.Regola != null && brand.Rule(showcase.Regola) == null)
                yield return $"{what}: la regola scelta non c'è più.";
        }
    }

    /// <summary>I colori del locale, se ci sono e se valgono (con giorno e notte automatici valgono quelli del tema).</summary>
    public static LocaleColors? LocaleColors(BrandConfig brand) =>
        brand.Aspetto.Colori is { } c && brand.Aspetto.Modo != "auto"
            ? new LocaleColors(c.Fondo, c.Superficie, c.Testo, c.Tenue, c.Primario, c.SulPrimario, c.Accento, c.Secondario)
            : null;

    static string ContrastProblem(Palette basePalette, LocaleColors colors, List<ThemeColors.Pair> failing)
    {
        var what = string.Join(", ", failing.Select(p => $"{p.Label} {ThemeColors.Ratio(p.Ratio ?? 0)}"));
        var text = $"Il locale: colori poco leggibili, {what} (serve almeno 4,5:1).";
        if (ThemeColors.Suggest(basePalette, colors) is not { } fixedColors)
            return text + " Scegli colori più distanti tra loro.";
        var changes = new List<string>();
        if (fixedColors.Text != colors.Text)
            changes.Add($"testo {fixedColors.Text}");
        if (fixedColors.OnPrimary != colors.OnPrimary)
            changes.Add($"testo sopra il primario {fixedColors.OnPrimary}");
        return text + $" La correzione più vicina: {string.Join(", ", changes)}.";
    }

    static string? Name(string? name, string what) =>
        string.IsNullOrWhiteSpace(name) ? $"{what} ha bisogno di un nome."
        : name.Length > 40 ? $"{what} \"{name[..20]}…\": il nome è troppo lungo (al massimo 40 caratteri)."
        : null;
}
