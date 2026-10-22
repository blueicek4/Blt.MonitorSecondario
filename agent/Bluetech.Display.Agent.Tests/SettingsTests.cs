using System.Text.Json;
using Bluetech.Display.Agent.Accesso;
using Bluetech.Display.Agent.Impostazioni;
using Xunit;

namespace Bluetech.Display.Agent.Tests;

/// <summary>dati\impostazioni.json: primo avvio, modifiche controllate, file rotto o scritto a mano.</summary>
public sealed class SettingsTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), "bt-impostazioni-" + Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(_folder, SettingsStore.FileName);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    static string LocalFile(string folder, string name) => Path.Combine(folder, name, "Scontrino.txt");

    [Fact]
    public void The_first_start_writes_one_cash_point_and_no_password()
    {
        var store = new SettingsStore(_folder);
        Assert.True(File.Exists(FilePath));
        Assert.Empty(store.Current.Installazione.Accessi);
        Assert.Equal(AgentSettings.CurrentVersion, store.Current.Versione);
        Assert.Equal("locale", Assert.Single(store.Current.Marchi).Id);
        var cash = Assert.Single(store.Current.Casse);
        Assert.Equal(@"C:\MonitorSecondario\Scontrino.txt", cash.FileCassa);
        Assert.Equal("locale", cash.Marchio);
        Assert.Contains(store.LoadNotes, n => n.StartsWith("Primo avvio", StringComparison.Ordinal));

        var text = File.ReadAllText(FilePath);
        Assert.Contains("\"Casse\"", text);
        Assert.Contains("\"Cassa 1\"", text);
    }

    [Fact]
    public void A_change_is_checked_saved_and_announced()
    {
        var store = new SettingsStore(_folder);
        Assert.Null(store.Update(s =>
        {
            s.Casse[0].FileCassa = LocalFile(_folder, "cassa1");
            return null;
        }));
        AgentSettings? seen = null;
        store.Changed += (_, after) => seen = after;

        var error = store.Update(s =>
        {
            s.Casse.Add(new CashPointConfig { Id = "cassa2", Nome = "Cassa bar", Marchio = "locale", FileCassa = LocalFile(_folder, "cassa1") });
            return null;
        });
        Assert.Contains("già di un'altra cassa", error);
        Assert.Null(seen);   // rifiutata: niente evento, file com'era
        Assert.Single(new SettingsStore(_folder).Current.Casse);

        Assert.Null(store.Update(s =>
        {
            s.Casse.Add(new CashPointConfig { Id = "cassa2", Nome = "Cassa bar", Marchio = "locale", FileCassa = LocalFile(_folder, "cassa2") });
            return null;
        }));
        Assert.Equal(2, seen!.Casse.Count);
        Assert.Equal(2, new SettingsStore(_folder).Current.Casse.Count);   // salvata
        Assert.True(File.Exists(FilePath + ".bak"));   // la versione prima, per tornare indietro
    }

    [Fact]
    public void Devices_need_an_existing_cash_point_and_printer()
    {
        var store = new SettingsStore(_folder);
        store.Update(s =>
        {
            s.Casse[0].FileCassa = LocalFile(_folder, "cassa1");
            return null;
        });
        var device = new ScreenSettings { Id = "schermo1", Nome = "Palmare", Token = new string('a', 64), Cassa = "cassa9" };
        Assert.Contains("scegli la cassa", store.Update(s =>
        {
            s.Schermi.Add(device);
            return null;
        }));
        device.Cassa = "cassa1";
        device.Stampante = "stampante9";
        Assert.Contains("stampante scelta non c'è", store.Update(s =>
        {
            s.Schermi.Add(device);
            return null;
        }));
        Assert.Contains("indirizzo della stampante", store.Update(s =>
        {
            s.Stampanti.Add(new PrinterConfig { Id = "stampante1", Nome = "Banco", Collegamento = "rete", Indirizzo = "192.168.1.50:porta" });
            return null;
        }));
    }

    [Fact]
    public void An_old_mistake_does_not_block_other_changes()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, $$"""
            {
              "Casse": [ { "Id": "cassa1", "Nome": "Sala", "FileCassa": {{JsonSerializer.Serialize(LocalFile(_folder, "sala"))}} } ],
              "Stampanti": [ { "Id": "stampante1", "Nome": "Banco", "Collegamento": "rete", "Indirizzo": "" } ]
            }
            """);
        var store = new SettingsStore(_folder);
        Assert.Contains(store.LoadNotes, n => n.Contains("Banco: l'indirizzo"));   // detto all'avvio e nel back office
        Assert.Null(store.Update(s =>
        {
            s.Installazione.SetPassword(AccessEntry.Owner, AdminAccess.Hash("la-mia-password"));   // la password si imposta lo stesso
            return null;
        }));
        Assert.Contains("Banco: l'indirizzo", store.Update(s =>
        {
            s.Stampanti.Add(new PrinterConfig { Id = "stampante2", Nome = "Banco", Collegamento = "rete", Indirizzo = "" });
            return null;
        }) ?? "nessun errore");
        Assert.Null(store.Update(s =>
        {
            s.Stampanti[0].Indirizzo = "192.168.1.50";
            return null;
        }));
    }

    [Fact]
    public void A_broken_file_is_set_aside_and_the_last_good_copy_comes_back()
    {
        var store = new SettingsStore(_folder);
        store.Update(s =>
        {
            s.Casse[0].FileCassa = LocalFile(_folder, "cassa1");
            s.Marchi[0].Vendita.ChiusuraDopoSecondi = 75;
            return null;
        });
        store.Update(s =>
        {
            s.Marchi[0].Vendita.ChiusuraDopoSecondi = 90;
            return null;
        });
        File.WriteAllText(FilePath, "{ \"Casse\": [ { \"Nome\": \"rotto\" ");   // come dopo una modifica a mano sbagliata

        var reloaded = new SettingsStore(_folder);
        Assert.Equal(75, reloaded.Current.Marchi[0].Vendita.ChiusuraDopoSecondi);   // la copia di sicurezza: la versione prima dell'ultima
        Assert.Contains(reloaded.LoadNotes, n => n.Contains("non è valido"));
        Assert.Single(Directory.GetFiles(_folder, SettingsStore.FileName + ".errore-*"));
    }

    [Fact]
    public void A_missing_file_comes_back_from_the_backup_not_from_the_defaults()
    {
        var store = new SettingsStore(_folder);
        store.Update(s =>
        {
            s.Casse[0].FileCassa = LocalFile(_folder, "cassa1");
            s.Installazione.SetPassword(AccessEntry.Owner, AdminAccess.Hash("la-mia-password"));
            return null;
        });
        store.Update(s =>
        {
            s.Marchi[0].Vendita.ChiusuraDopoSecondi = 90;
            return null;
        });
        File.Delete(FilePath);   // tolto per errore

        var reloaded = new SettingsStore(_folder);
        Assert.NotNull(reloaded.Current.Installazione.Password(AccessEntry.Owner));   // non le impostazioni iniziali, senza password
        Assert.Contains(reloaded.LoadNotes, n => n.Contains("ripristinato dalla copia"));
        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public void A_file_locked_for_a_moment_is_read_again_not_thrown_away()
    {
        var store = new SettingsStore(_folder);
        store.Update(s =>
        {
            s.Casse[0].FileCassa = LocalFile(_folder, "cassa1");
            s.Marchi[0].Vendita.ChiusuraDopoSecondi = 77;
            return null;
        });
        var locked = new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);   // un antivirus, una copia
        // Un thread suo, non il pool: con gli altri test in parallelo il pool può restare occupato oltre i tentativi.
        var release = new Thread(() =>
        {
            Thread.Sleep(700);
            locked.Dispose();
        });
        release.Start();
        var reloaded = new SettingsStore(_folder);
        release.Join();
        Assert.Equal(77, reloaded.Current.Marchi[0].Vendita.ChiusuraDopoSecondi);
        Assert.Empty(Directory.GetFiles(_folder, SettingsStore.FileName + ".errore-*"));
    }

    [Fact]
    public void A_hand_written_file_is_repaired_where_it_can()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, $$"""
            {
              // scritto a mano, con un commento e una virgola in più
              "casse": [ { "Nome": "Sala", "FileCassa": {{JsonSerializer.Serialize(LocalFile(_folder, "sala"))}} }, ],
              "Dispositivi": [ { "Id": "dispositivo1", "Nome": "Palmare", "Token": "{{new string('b', 64)}}", "Cassa": "manca", "Stampante": "manca" } ]
            }
            """);
        var store = new SettingsStore(_folder);
        var cash = Assert.Single(store.Current.Casse);
        Assert.Equal("cassa1", cash.Id);   // l'id mancante si genera
        var device = Assert.Single(store.Current.Schermi);   // era un file della versione 2: i dispositivi sono gli schermi
        Assert.Equal("cassa1", device.Cassa);
        Assert.Null(device.Stampante);
        Assert.Null(store.Current.Validate());
        Assert.Contains(store.LoadNotes, n => n.Contains("la stampante manca non c'è più"));
    }

    [Fact]
    public void The_password_is_kept_as_a_hash()
    {
        var hash = AdminAccess.Hash("la-mia-password");
        Assert.True(AdminAccess.Verify(hash, "la-mia-password"));
        Assert.False(AdminAccess.Verify(hash, "La-mia-password"));
        Assert.NotEqual(hash.Sale, AdminAccess.Hash("la-mia-password").Sale);   // sale nuovo ogni volta
        Assert.DoesNotContain("la-mia-password", JsonSerializer.Serialize(hash));
        Assert.Equal("La password deve avere almeno 6 caratteri.", AdminAccess.CheckNew("12345"));
    }

    [Fact]
    public void Ids_are_numbered_after_the_highest()
    {
        Assert.Equal("cassa1", AgentSettings.NextId("cassa", []));
        Assert.Equal("cassa4", AgentSettings.NextId("cassa", ["cassa1", "cassa3", "stampante7"]));
    }
}
