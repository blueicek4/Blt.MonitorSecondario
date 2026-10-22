using Bluetech.Display.Agent.Aspetto;
using Bluetech.Display.Agent.Impostazioni.V2;

namespace Bluetech.Display.Agent.Impostazioni;

/// <summary>
/// Porta un impostazioni.json della versione 2 alla 3 (SPEC §19 del design): l'aspetto diventa il primo marchio, i
/// dispositivi gli schermi, casse, giorni e orari di ogni set una regola col nome del set, i set le vetrine, la password
/// quella del titolare. Quello che non c'era prende i valori di oggi (Vivo, 16 colonne, colonna in basso, carta e QR).
/// Ogni spostamento finisce nelle note, che l'Agent scrive nel log riga per riga.
/// </summary>
public static class MigrazioneV2
{
    public static AgentSettings Migrate(SettingsV2 old, List<string> notes)
    {
        var a = old.Aspetto;
        var settings = new AgentSettings
        {
            Installazione = new InstallationSettings
            {
                Rete = old.Rete,
                BackOfficeDallaRete = old.BackOfficeDallaRete,
            },
            Stampanti = old.Stampanti,
        };
        var install = settings.Installazione;
        if (old.Password != null)
        {
            install.Accessi.Add(new AccessEntry { Ruolo = AccessEntry.Owner, Password = old.Password });
            notes.Add("la password del back office è quella del titolare; quella del tecnico si imposta dall'Installazione");
        }

        var brand = new BrandConfig
        {
            Id = "locale",
            Nome = string.IsNullOrWhiteSpace(a.NomeLocale) ? "Il locale" : a.NomeLocale.Trim(),
        };
        settings.Marchi.Add(brand);
        notes.Add($"l'aspetto è il marchio \"{brand.Nome}\"");

        brand.Identita = new BrandIdentity { NomeLocale = a.NomeLocale, Sottotitolo = a.Sottotitolo, Logo = a.Logo, LogoChiaro = a.LogoChiaro };
        brand.Aspetto = Look(a, notes);
        brand.Schermo = new BrandScreen
        {
            Animazioni = a.Animazioni switch { "piene" => "full", "ridotte" => "eco", _ => "light" },
            QrPagamento = a.QrPagamento == "schermo" ? "fullscreen" : "beside",
            PrezziSoloSulConto = a.PrezziSoloSulConto,
            RigheCompatte = a.RigheCompatte,
        };
        brand.Scritte = new BrandTexts
        {
            Benvenuto = a.Benvenuto,
            Scorrevole = a.Scorrevole,
            TitoloConto = a.TitoloConto,
            Grazie = a.Grazie,
            SecondiGrazie = a.SecondiGrazie,
        };
        var receipt = old.ScontrinoDigitale;
        brand.Vendita = new BrandSale
        {
            ChiusuraDopoSecondi = old.ChiusuraVenditaDopoSecondi,
            ScontrinoDigitale = new DigitalReceiptConfig
            {
                Attivo = receipt.MostraScontrino,
                Secondi = receipt.SecondiAVideo,
                CopiaCarta = receipt.CopiaCartacea,
                Dicitura = receipt.Dicitura,
            },
        };
        notes.Add("scontrino digitale e chiusura del conto sono del marchio");

        // I loghi dei kit sono dell'installazione: i loro file passano lì, e restano anche nel marchio se li usa.
        install.Loghi = new KitLogos
        {
            Bluetech = a.LogoBluetech,
            BluetechChiaro = a.LogoBluetechChiaro,
            Satispay = a.LogoSatispay,
            SatispayChiaro = a.LogoSatispayChiaro,
        };
        var kit = KitLogos.All.Select(l => l.Get(install.Loghi)).OfType<string>().ToHashSet();
        var brandUses = new HashSet<string?> { a.Logo, a.LogoChiaro, a.FondoMedia };
        brandUses.UnionWith(a.Promozioni.Select(p => p.Immagine));
        brandUses.UnionWith(a.Set.SelectMany(s => s.Elementi).Select(e => e.Id));
        foreach (var media in a.Media)
        {
            var item = new MediaItem { Id = media.Id, Nome = media.Nome, Tipo = media.Tipo, Byte = media.Byte, CaricatoIl = media.CaricatoIl };
            if (kit.Contains(media.Id))
            {
                install.Media.Add(item);
                if (!brandUses.Contains(media.Id))
                    continue;
                item = new MediaItem { Id = media.Id, Nome = media.Nome, Tipo = media.Tipo, Byte = media.Byte, CaricatoIl = media.CaricatoIl };
            }
            brand.Contenuti.Media.Add(item);
        }
        if (kit.Count > 0)
            notes.Add("i loghi di Bluetech e di Satispay sono dell'installazione");

        foreach (var promo in a.Promozioni)
        {
            var words = !string.IsNullOrWhiteSpace(promo.Titolo) || !string.IsNullOrWhiteSpace(promo.Testo);
            brand.Contenuti.Promozioni.Add(new PromoConfig
            {
                Id = promo.Id,
                Nome = promo.Nome,
                // Le slide di oggi non hanno un'etichetta sopra il titolo: si aggiunge dal back office.
                Etichetta = "nessuna",
                Titolo = promo.Titolo,
                Testo = promo.Testo,
                Immagine = promo.Immagine,
                // Con il QR il modello D; una foto con il titolo del sistema è muta (C), una foto senza parole parla da sé (B).
                Modello = promo.HasQr ? "D" : promo.Immagine == null ? "A" : words ? "C" : "B",
                Qr = promo.Qr,
                QrDidascalia = promo.QrDidascalia,
                DalGiorno = promo.DalGiorno,
                AlGiorno = promo.AlGiorno,
            });
        }

        Showcases(a, brand, notes);

        foreach (var cash in old.Casse)
            settings.Casse.Add(new CashPointConfig { Id = cash.Id, Nome = cash.Nome, Marchio = brand.Id, FileCassa = cash.FileCassa });
        if (settings.Casse.Count > 0)
            notes.Add($"le casse ({settings.Casse.Count}) mostrano il marchio \"{brand.Nome}\"");

        foreach (var device in old.Dispositivi)
        {
            var layout = device.Disposizione switch
            {
                "larga" => "wide",
                "stretta" => "narrow",
                "verticale" => "portrait",
                "palmare" => "palmare",
                _ => null,
            };
            settings.Schermi.Add(new ScreenSettings
            {
                Id = device.Id,
                Nome = device.Nome,
                Token = device.Token,
                Cassa = device.Cassa,
                Stampante = device.Stampante,
                AutorizzatoIl = device.AutorizzatoIl,
                Override = layout == null ? null : new ScreenOverride { Disposizione = layout },
            });
            if (layout != null)
                notes.Add($"lo schermo {device.Nome} tiene la disposizione {device.Disposizione}, ora un'eccezione dello schermo");
        }
        if (settings.Schermi.Count > 0)
            notes.Add($"i dispositivi ({settings.Schermi.Count}) sono gli schermi, abbinati come prima");
        notes.Add("nuovi valori del marchio: attrazione Vivo, conto su 16 colonne, colonna in basso, scontrino digitale con carta e QR");
        return settings;
    }

    static BrandLook Look(AppearanceV2 a, List<string> notes)
    {
        var auto = a.Modo == "auto";
        var key = $"{a.Tema}-{(a.Modo == "giorno" ? "giorno" : "notte")}";
        var theme = Palettes.Legacy.TryGetValue(key, out var t) ? t : Palettes.Fallback;
        var look = new BrandLook
        {
            Tema = theme,
            Modo = auto ? "auto" : "fisso",
            GiornoDalle = a.GiornoDalle,
            GiornoAlle = a.GiornoAlle,
            FontTitoli = a.FontTitoli,
            FontTesto = a.FontTesto,
            Raggio = a.Raggio,
            Vetro = auto ? null : a.Vetro,
            // La firma della 2 si chiamava dal fondo su cui stava ("scuro" = sui fondi scuri), quella della 3 dal suo colore.
            Footer = auto ? null : a.Footer switch { "scuro" => "light", "chiaro" => "dark", _ => null },
            Fondo = a.Fondo switch { "tinta" or "sfumato" or "immagine" or "video" => a.Fondo, _ => "mesh" },
            FondoMedia = a.FondoMedia,
        };
        if (!auto && a.Colori is { } c)
        {
            look.Colori = new ThemeColorSettings
            {
                Fondo = c.Fondo,
                Superficie = c.Superficie,
                Testo = c.Testo,
                Tenue = c.Tenue,
                Primario = c.Primario,
                SulPrimario = c.SulPrimario,
                Accento = c.Accento,
                Secondario = c.Secondario,
            };
        }
        var name = Palettes.Get(theme).Nome;
        if (auto && Palettes.Get(theme).Gemello == null)
        {
            look.Modo = "fisso";
            notes.Add($"il tema {a.Tema} con giorno e notte automatici è {name}, che non ha una versione di giorno: resta fisso");
        }
        else if (key == "cocktail-giorno")
            notes.Add($"il tema cocktail di giorno non ha un equivalente chiaro: è {name}, scuro");
        else
            notes.Add($"il tema {a.Tema} ({(auto ? "giorno e notte automatici" : a.Modo)}) è {name}{(auto ? $", con {Palettes.All[Palettes.Get(theme).Gemello!].Nome} di giorno" : "")}");
        return look;
    }

    /// <summary>
    /// Ogni set è una vetrina, nello stesso ordine. Casse, giorni, orari e date di un set diventano una regola col nome
    /// del set; due regole uguali sono una, col nome di entrambe ("Aperitivo · Serata jazz").
    /// </summary>
    static void Showcases(AppearanceV2 a, BrandConfig brand, List<string> notes)
    {
        var rules = brand.Contenuti.Regole;
        foreach (var set in a.Set)
        {
            string? ruleId = null;
            var hasRule = set.Casse.Count > 0 || set.Giorni.Count > 0 || set.Dalle != null || set.Alle != null || set.DalGiorno != null || set.AlGiorno != null;
            if (hasRule)
            {
                var casse = set.Casse.Distinct().Order(StringComparer.Ordinal).ToList();
                var giorni = set.Giorni.Distinct().Order().ToList();
                var same = rules.Find(r => r.Casse.SequenceEqual(casse) && r.Giorni.SequenceEqual(giorni)
                    && r.Dalle == set.Dalle && r.Alle == set.Alle && r.DalGiorno == set.DalGiorno && r.AlGiorno == set.AlGiorno);
                if (same != null)
                {
                    notes.Add($"la regola di {set.Nome} è uguale a quella di {same.Nome}: sono una, \"{same.Nome} · {set.Nome}\"");
                    same.Nome = $"{same.Nome} · {set.Nome}";
                    ruleId = same.Id;
                }
                else
                {
                    var rule = new RuleConfig
                    {
                        Id = AgentSettings.NextId("regola", rules.Select(r => r.Id)),
                        Nome = set.Nome,
                        Casse = casse,
                        Giorni = giorni,
                        Dalle = set.Dalle,
                        Alle = set.Alle,
                        DalGiorno = set.DalGiorno,
                        AlGiorno = set.AlGiorno,
                    };
                    rules.Add(rule);
                    ruleId = rule.Id;
                    notes.Add($"casse, giorni e orari del set {set.Nome} sono la regola \"{rule.Nome}\"");
                }
            }
            brand.Contenuti.Vetrine.Add(new ShowcaseConfig
            {
                Id = set.Id,
                Nome = set.Nome,
                Attiva = set.Attivo,
                Posti = [.. set.Dove],
                Regola = ruleId,
                Rotazione = set.Ordine,
                QrOgni = set.QrDopo,
                Secondi = set.Secondi,
                Elementi = set.Elementi.Select(e => new ShowcaseItemConfig { Id = e.Id, Secondi = e.Secondi }).ToList(),
            });
            notes.Add($"il set {set.Nome} è una vetrina{(ruleId == null ? ", sempre in onda" : "")}");
        }
    }
}
