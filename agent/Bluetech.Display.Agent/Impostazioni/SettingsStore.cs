using System.Text;
using System.Text.Json;
using Bluetech.Display.Agent.Impostazioni.V2;

namespace Bluetech.Display.Agent.Impostazioni;

/// <summary>
/// Legge e scrive dati\impostazioni.json. Ogni modifica lavora su una copia, la controlla, la salva (prima su un file
/// temporaneo, poi al posto del vecchio, che resta come .bak) e solo allora la rende quella in uso: chi legge
/// <see cref="Current"/> vede sempre impostazioni intere e valide.
/// </summary>
public sealed class SettingsStore
{
    public const string FileName = "impostazioni.json";
    /// <summary>Il file della versione 2 com'era prima della migrazione alla 3.</summary>
    public const string V2BackupName = "impostazioni.v2.bak";
    static readonly UTF8Encoding Utf8 = new(false);

    readonly object _lock = new();
    volatile AgentSettings _current;

    public SettingsStore(string folder)
    {
        Folder = folder;
        FilePath = Path.Combine(folder, FileName);
        Directory.CreateDirectory(folder);
        _current = Load();
    }

    public string Folder { get; }
    public string FilePath { get; }

    /// <summary>Le impostazioni in uso. Non si modificano: si passa da <see cref="Update"/>.</summary>
    public AgentSettings Current => _current;

    /// <summary>Cosa è successo alla lettura (file rotto, riparazioni), da scrivere nel log e mostrare nel back office.</summary>
    public List<string> LoadNotes { get; } = [];

    /// <summary>Le impostazioni sono cambiate: (prima, dopo). Si chiama dentro il lucchetto, una modifica per volta.</summary>
    public event Action<AgentSettings, AgentSettings>? Changed;

    /// <summary>
    /// Cambia le impostazioni. <paramref name="change"/> lavora su una copia e può rifiutare con un messaggio; poi la copia
    /// non deve avere errori nuovi (<see cref="AgentSettings.Problems"/>). Restituisce l'errore, o null se è salvata e in uso.
    /// </summary>
    public string? Update(Func<AgentSettings, string?> change)
    {
        lock (_lock)
        {
            var before = _current;
            var after = before.Clone();
            var error = change(after);
            if (error != null)
                return error;
            // Contati, non solo confrontati: due stampanti con lo stesso nome e lo stesso errore sono due errori.
            var known = before.Problems().GroupBy(p => p).ToDictionary(g => g.Key, g => g.Count());
            foreach (var problem in after.Problems())
            {
                if (known.TryGetValue(problem, out var count) && count > 0)
                    known[problem] = count - 1;
                else
                    return problem;
            }
            try
            {
                Save(after);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"Non riesco a salvare {FilePath}: {ex.Message}";
            }
            _current = after;
            Changed?.Invoke(before, after);
            return null;
        }
    }

    AgentSettings Load()
    {
        var read = ReadWithRetries(FilePath, out var settings, out var problem, out var migration);
        if (read == ReadResult.Migrated)
        {
            // Il file vecchio resta accanto, intero: dalla 3 non si torna indietro da sé, ma il tecnico può.
            File.Copy(FilePath, Path.Combine(Folder, V2BackupName), overwrite: true);
            LoadNotes.Add($"{FileName} era della versione 2: portato alla 3, il file di prima è {V2BackupName}");
            AddMigrationNotes(migration);
            settings = Repaired(settings);
            Save(settings, keepBackup: false);
            return settings;
        }
        if (read == ReadResult.Ok)
            return Repaired(settings);

        if (read == ReadResult.Missing)
        {
            // Senza impostazioni.json ma con la copia: il file è stato tolto (a mano, o da un errore): si riparte dalla
            // copia, non dalle impostazioni iniziali, che perderebbero password, casse e dispositivi.
            if (ReadWithRetries(FilePath + ".bak", out var backup, out _, out var backupMigration) is ReadResult.Ok or ReadResult.Migrated)
            {
                LoadNotes.Add($"{FileName} mancava: ripristinato dalla copia di sicurezza (.bak)");
                AddMigrationNotes(backupMigration);
                backup = Repaired(backup);
                Save(backup);
                return backup;
            }
            var defaults = AgentSettings.Defaults();
            Save(defaults);
            LoadNotes.Add($"Primo avvio: creato {FilePath}");
            return defaults;
        }

        if (read == ReadResult.Unreadable)   // bloccato o senza permessi anche dopo i tentativi: meglio non partire che perdere le impostazioni
            throw new IOException($"Non riesco a leggere {FilePath}: {problem}");

        // File rotto (modificato a mano?): se ne tiene una copia e si riparte dall'ultima copia buona.
        var broken = $"{FilePath}.errore-{DateTime.Now:yyyyMMdd-HHmmss}";
        try
        {
            File.Copy(FilePath, broken, overwrite: true);
            LoadNotes.Add($"{FileName} non è valido ({problem}): una copia è in {Path.GetFileName(broken)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoadNotes.Add($"{FileName} non è valido ({problem}) e non riesco a copiarlo: {ex.Message}");
        }
        if (ReadWithRetries(FilePath + ".bak", out var good, out _, out var goodMigration) is ReadResult.Ok or ReadResult.Migrated)
        {
            LoadNotes.Add("Riparto dalla copia di sicurezza (.bak), cioè dalle impostazioni prima dell'ultima modifica");
            AddMigrationNotes(goodMigration);
            good = Repaired(good);
            Save(good, keepBackup: false);
            return good;
        }
        LoadNotes.Add("Nessuna copia di sicurezza buona: riparto dalle impostazioni iniziali");
        var fresh = AgentSettings.Defaults();
        Save(fresh, keepBackup: false);
        return fresh;
    }

    AgentSettings Repaired(AgentSettings settings)
    {
        var notes = settings.Repair();
        foreach (var note in notes)
            LoadNotes.Add($"{FileName}: {note}");
        foreach (var invalid in settings.Problems())
            LoadNotes.Add($"{FileName}: {invalid} Correggilo dal back office.");
        return settings;
    }

    /// <summary>Le note della migrazione dalla 2, se il file letto era della 2.</summary>
    void AddMigrationNotes(List<string>? notes)
    {
        foreach (var note in notes ?? [])
            LoadNotes.Add($"{FileName}, migrazione: {note}");
    }

    enum ReadResult
    {
        Ok,
        /// <summary>Letto, ed era della versione 2 (o della prima): ora è della 3, con le note della migrazione.</summary>
        Migrated,
        Missing,
        Broken,
        Unreadable,
    }

    /// <summary>Un file bloccato per un attimo (antivirus, copia di sicurezza) non è un file rotto: si riprova.</summary>
    static ReadResult ReadWithRetries(string path, out AgentSettings settings, out string? problem, out List<string>? migration)
    {
        for (var attempt = 1; ; attempt++)
        {
            var result = TryRead(path, out settings, out problem, out migration);
            if (result != ReadResult.Unreadable || attempt == 10)
                return result;
            Thread.Sleep(300);
        }
    }

    static ReadResult TryRead(string path, out AgentSettings settings, out string? problem, out List<string>? migration)
    {
        settings = null!;
        problem = null;
        migration = null;
        try
        {
            if (!File.Exists(path))
                return ReadResult.Missing;
            var text = File.ReadAllText(path, Utf8);
            if (VersionOf(text) < AgentSettings.CurrentVersion)
            {
                var old = JsonSerializer.Deserialize<SettingsV2>(text, SettingsV2.Json);
                if (old == null)
                {
                    problem = "il file è vuoto";
                    return ReadResult.Broken;
                }
                migration = old.Repair();
                settings = MigrazioneV2.Migrate(old, migration);
                return ReadResult.Migrated;
            }
            settings = JsonSerializer.Deserialize<AgentSettings>(text, AgentSettings.Json)!;
            if (settings == null)
            {
                problem = "il file è vuoto";
                return ReadResult.Broken;
            }
            return ReadResult.Ok;
        }
        catch (JsonException ex)
        {
            problem = ex.LineNumber.HasValue ? $"riga {ex.LineNumber + 1}: {ex.Message}" : ex.Message;
            return ReadResult.Broken;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = ex.Message;
            return ReadResult.Unreadable;
        }
    }

    /// <summary>La versione scritta nel file; 2 se non c'è (la 2 e la prima non la scrivevano).</summary>
    static int VersionOf(string text)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("non è un oggetto JSON");
        foreach (var property in doc.RootElement.EnumerateObject())
            if (property.Name.Equals("Versione", StringComparison.OrdinalIgnoreCase) && property.Value.TryGetInt32(out var version))
                return version;
        return 2;
    }

    /// <param name="keepBackup">Falso quando il file attuale è rotto: la copia buona resta quella che c'è.</param>
    void Save(AgentSettings settings, bool keepBackup = true)
    {
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, AgentSettings.Json), Utf8);
        if (keepBackup && File.Exists(FilePath))
            File.Copy(FilePath, FilePath + ".bak", overwrite: true);
        File.Move(temp, FilePath, overwrite: true);
    }
}
