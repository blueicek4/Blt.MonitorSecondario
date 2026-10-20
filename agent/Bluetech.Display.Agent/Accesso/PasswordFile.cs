using System.Security.Cryptography;
using System.Text;
using Bluetech.Display.Agent.Impostazioni;

namespace Bluetech.Display.Agent.Accesso;

/// <summary>
/// La password del back office da un file, <c>dati\nuova-password.txt</c>: lo scrive l'installazione (la password scelta,
/// o una generata con <c>--genera-password</c>) o il tecnico che deve reimpostarla. All'avvio l'Agent la legge, ne salva
/// solo l'hash e toglie il file, come il file di interscambio della cassa. Per scrivere nella cartella dati serve essere
/// amministratori del PC: dalla rete non si fa, e nessuna password è uguale per tutti i locali.
/// </summary>
public static class PasswordFile
{
    public const string FileName = "nuova-password.txt";

    /// <summary>Senza 0/o e 1/l/i: si legge e si detta al telefono senza equivoci.</summary>
    const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    public sealed record Outcome(string Note, bool Problem);

    /// <summary>
    /// All'avvio: se c'è il file, la password del back office diventa quella. Il file si toglie sempre, anche quando la
    /// password non va bene: una password in chiaro non deve restare sul disco. Null se il file non c'è.
    /// </summary>
    public static Outcome? Apply(string dataFolder, SettingsStore settings)
    {
        var path = Path.Combine(dataFolder, FileName);
        if (!File.Exists(path))
            return null;
        Outcome outcome;
        try
        {
            var password = File.ReadAllText(path, Encoding.UTF8).Split('\n')[0].Trim();
            if (AdminAccess.CheckNew(password) is { } weak)
            {
                outcome = new($"La password in {FileName} non va bene ({weak}): resta quella di prima.", true);
            }
            else
            {
                var error = settings.Update(s =>
                {
                    s.Password = AdminAccess.Hash(password);
                    return null;
                });
                outcome = error == null
                    ? new($"Password del back office impostata con {FileName}: il file è stato tolto.", false)
                    : new($"La password in {FileName} non è stata salvata: {error}", true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new($"Non riesco a leggere {path} ({ex.Message}): la password resta quella di prima.", true);
        }
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            outcome = new($"{outcome.Note} Non riesco però a togliere {path} ({ex.Message}): va tolto a mano, dentro c'è la password in chiaro.", true);
        }
        return outcome;
    }

    /// <summary>Una password casuale e leggibile: quattro gruppi di quattro caratteri, circa 79 bit.</summary>
    public static string Generate()
    {
        var text = new StringBuilder(19);
        for (var i = 0; i < 16; i++)
        {
            if (i > 0 && i % 4 == 0)
                text.Append('-');
            text.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        }
        return text.ToString();
    }

    /// <summary>
    /// <c>--genera-password</c>: una password nuova nel file, che vale dal prossimo avvio dell'Agent. Sull'uscita solo la
    /// password, per l'installazione che la mostra al tecnico; le spiegazioni vanno sugli errori, così si leggono anche a
    /// mano senza disturbare chi legge l'uscita.
    /// </summary>
    public static int GenerateCommand(string dataFolder, TextWriter output, TextWriter messages)
    {
        var password = Generate();
        var path = Path.Combine(dataFolder, FileName);
        try
        {
            Directory.CreateDirectory(dataFolder);
            File.WriteAllText(path, password + Environment.NewLine, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            messages.WriteLine($"Non riesco a scrivere {path}: {ex.Message}. Serve un prompt dei comandi da amministratore.");
            return 1;
        }
        messages.WriteLine("La nuova password del back office (annotala nella scheda del cliente):");
        output.WriteLine(password);
        messages.WriteLine($"Vale dal prossimo avvio dell'Agent: riavvia il servizio {AgentApp.ServiceName}. Fino ad allora è in {path}.");
        return 0;
    }
}
