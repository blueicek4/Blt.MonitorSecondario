using System.Text;
using System.Text.Json;

namespace Bluetech.Display.Agent.Impostazioni;

/// <summary>
/// Legge e scrive dati\impostazioni.json. Ogni modifica lavora su una copia, la controlla, la salva (prima su un file
/// temporaneo, poi al posto del vecchio, che resta come .bak) e solo allora la rende quella in uso: chi legge
/// <see cref="Current"/> vede sempre impostazioni intere e valide.
/// </summary>
public sealed class SettingsStore
{
    public const string FileName = "impostazioni.json";
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
        if (!File.Exists(FilePath))
        {
            var defaults = AgentSettings.Defaults();
            Save(defaults);
            LoadNotes.Add($"Primo avvio: creato {FilePath}");
            return defaults;
        }
        if (TryRead(FilePath, out var settings, out var problem))
            return Repaired(settings);

        // File rotto (modificato a mano?): si mette da parte e si riparte dall'ultima copia buona.
        var broken = $"{FilePath}.errore-{DateTime.Now:yyyyMMdd-HHmmss}";
        File.Move(FilePath, broken);
        LoadNotes.Add($"{FileName} non è valido ({problem}): l'ho rinominato in {Path.GetFileName(broken)}");
        if (TryRead(FilePath + ".bak", out var backup, out _))
        {
            LoadNotes.Add("Riparto dalla copia di sicurezza (.bak), cioè dalle impostazioni prima dell'ultima modifica");
            backup = Repaired(backup);
            Save(backup);
            return backup;
        }
        LoadNotes.Add("Nessuna copia di sicurezza buona: riparto dalle impostazioni iniziali");
        var fresh = AgentSettings.Defaults();
        Save(fresh);
        return fresh;
    }

    AgentSettings Repaired(AgentSettings settings)
    {
        var notes = settings.Repair();
        foreach (var note in notes)
            LoadNotes.Add($"{FileName}: {note}");
        if (settings.Validate() is { } invalid)
            LoadNotes.Add($"{FileName}: {invalid} Correggilo dal back office.");
        return settings;
    }

    static bool TryRead(string path, out AgentSettings settings, out string? problem)
    {
        settings = null!;
        problem = null;
        try
        {
            if (!File.Exists(path))
            {
                problem = "il file non c'è";
                return false;
            }
            settings = JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(path, Utf8), AgentSettings.Json)!;
            if (settings == null)
            {
                problem = "il file è vuoto";
                return false;
            }
            return true;
        }
        catch (JsonException ex)
        {
            problem = ex.LineNumber.HasValue ? $"riga {ex.LineNumber + 1}: {ex.Message}" : ex.Message;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = ex.Message;
            return false;
        }
    }

    void Save(AgentSettings settings)
    {
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, AgentSettings.Json), Utf8);
        if (File.Exists(FilePath))
            File.Copy(FilePath, FilePath + ".bak", overwrite: true);
        File.Move(temp, FilePath, overwrite: true);
    }
}
