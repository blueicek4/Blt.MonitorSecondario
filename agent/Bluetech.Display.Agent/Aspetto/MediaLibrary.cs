using System.Security.Cryptography;
using Bluetech.Display.Agent.Impostazioni;
using Bluetech.Display.Agent.Server;

namespace Bluetech.Display.Agent.Aspetto;

/// <summary>
/// Le foto e i video caricati dal back office (le gallerie, il logo, lo sfondo), in dati\media. Il nome del file è
/// l'impronta del contenuto: lo stesso file caricato due volte è uno solo, e un display lo tiene in cache per sempre
/// perché a un nome corrisponde sempre lo stesso contenuto. Nelle impostazioni c'è l'elenco, con il nome originale.
/// </summary>
public sealed class MediaLibrary(SettingsStore settings, AgentRuntime runtime, ILogger<MediaLibrary> log)
{
    public const long MaxImageBytes = 15L * 1024 * 1024;
    public const long MaxVideoBytes = 200L * 1024 * 1024;
    public const long MaxTotalBytes = 3L * 1024 * 1024 * 1024;
    const int HeadBytes = 16;

    public string Folder => Path.Combine(runtime.DataFolder, "media");

    public sealed record Kind(string Type, string Extension)
    {
        public bool IsVideo => Type.StartsWith("video/", StringComparison.Ordinal);
    }

    /// <summary>Il tipo dai primi byte, non dal nome: PNG, JPEG, GIF, WebP, MP4, WebM. Null per tutto il resto.</summary>
    public static Kind? Detect(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return new("image/png", ".png");
        if (head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
            return new("image/jpeg", ".jpg");
        if (head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8))
            return new("image/gif", ".gif");
        if (head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
            return new("image/webp", ".webp");
        if (head.StartsWith((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]))
            return new("video/webm", ".webm");
        // MP4: la scatola "ftyp" in testa. "qt  " è un .mov di QuickTime, che i browser non riproducono tutti.
        if (head.Length >= 12 && head[4..8].SequenceEqual("ftyp"u8) && !head[8..12].SequenceEqual("qt  "u8))
            return new("video/mp4", ".mp4");
        return null;
    }

    /// <summary>
    /// Salva un file caricato: lo legge una volta sola (impronta e controlli mentre arriva), lo aggiunge all'elenco delle
    /// impostazioni e lo restituisce, o restituisce l'errore.
    /// </summary>
    public async Task<(MediaItem? Item, string? Error)> SaveAsync(Stream body, string? name, CancellationToken cancel)
    {
        Directory.CreateDirectory(Folder);
        var temp = Path.Combine(Folder, $".caricamento-{Guid.NewGuid():N}.tmp");
        try
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var head = new byte[HeadBytes];
            var headLength = 0;
            long total = 0;
            Kind? kind = null;
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await body.ReadAsync(buffer, cancel)) > 0)
                {
                    if (headLength < HeadBytes)
                    {
                        var take = Math.Min(read, HeadBytes - headLength);
                        Array.Copy(buffer, 0, head, headLength, take);
                        headLength += take;
                    }
                    if (kind == null && headLength == HeadBytes && (kind = Detect(head)) == null)
                        return (null, NotMedia);
                    total += read;
                    if (total > (kind?.IsVideo == true ? MaxVideoBytes : MaxImageBytes))
                        return (null, kind?.IsVideo == true
                            ? $"Il video è troppo grande: al massimo {MaxVideoBytes / 1024 / 1024} MB (per 30 secondi in alta definizione ne bastano 20-40)."
                            : $"L'immagine è troppo grande: al massimo {MaxImageBytes / 1024 / 1024} MB (per uno schermo 1920×1080 ne basta uno).");
                    sha.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancel);
                }
            }
            if (headLength == 0)
                return (null, "Il file è vuoto.");
            kind ??= Detect(head.AsSpan(0, headLength));
            if (kind == null)
                return (null, NotMedia);

            var id = Convert.ToHexStringLower(sha.GetHashAndReset())[..20] + kind.Extension;
            if (settings.Current.Media(id) is { } existing && File.Exists(Path.Combine(Folder, id)))
                return (existing, null);   // lo stesso file, già caricato
            if (settings.Current.Aspetto.Media.Sum(m => m.Byte) + total > MaxTotalBytes)
                return (null, $"Immagini e video insieme arrivano al massimo a {MaxTotalBytes / 1024 / 1024 / 1024} GB: togli quelli che non usi più.");
            var path = Path.Combine(Folder, id);
            try
            {
                if (!File.Exists(path))
                    File.Move(temp, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Lo stesso file caricato due volte nello stesso momento: è già al suo posto.
            }
            var item = new MediaItem
            {
                Id = id,
                Nome = CleanName(name, kind),
                Tipo = kind.Type,
                Byte = total,
                CaricatoIl = DateTimeOffset.Now,
            };
            var error = settings.Update(s =>
            {
                if (s.Media(id) == null)
                    s.Aspetto.Media.Add(item);
                return null;
            });
            if (error != null)
                return (null, error);
            log.LogInformation("Caricato {Name} ({Type}, {Bytes} byte) come {Id}", item.Nome, item.Tipo, item.Byte, id);
            return (settings.Current.Media(id), null);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    const string NotMedia = "Questo file non è un'immagine (PNG, JPEG, WebP, GIF) né un video (MP4, WebM). Un logo in SVG va esportato in PNG.";

    static string CleanName(string? name, Kind kind)
    {
        var clean = new string(Path.GetFileName(name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0)
            clean = (kind.IsVideo ? "video" : "immagine") + kind.Extension;
        return clean.Length > 80 ? clean[..80] : clean;
    }

    /// <summary>Il percorso del file, solo se è uno di quelli caricati.</summary>
    public string? PathOf(string id)
    {
        if (!AgentSettings.MediaIdPattern().IsMatch(id) || settings.Current.Media(id) == null)
            return null;
        var path = Path.Combine(Folder, id);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Dove è usato un file: i loghi, lo sfondo, le promozioni, i set.</summary>
    public static List<string> UsedBy(AgentSettings s, string id)
    {
        var used = AppearanceSettings.Logos.Where(l => l.Get(s.Aspetto) == id).Select(l => l.Name).ToList();
        if (s.Aspetto.FondoMedia == id)
            used.Add("lo sfondo");
        used.AddRange(s.Aspetto.Promozioni.Where(p => p.Immagine == id).Select(p => $"la promozione {p.Nome}"));
        used.AddRange(s.Aspetto.Set.Where(x => x.Elementi.Any(e => e.Id == id)).Select(x => $"il set {x.Nome}"));
        return used;
    }

    /// <summary>Toglie un file che non serve più; se è ancora usato, dice dove.</summary>
    public string? Delete(string id)
    {
        var error = settings.Update(s =>
        {
            var used = UsedBy(s, id);
            if (used.Count > 0)
                return $"Il file serve ancora: lo usa {string.Join(", ", used)}. Toglilo da lì e poi eliminalo.";
            return s.Aspetto.Media.RemoveAll(m => m.Id == id) == 0 ? "Il file non c'è." : null;
        });
        if (error != null)
            return error;
        TryDelete(Path.Combine(Folder, id));
        return null;
    }

    /// <summary>
    /// All'avvio: dall'elenco i file che sul disco non ci sono più (tolti a mano), dal disco quelli che l'elenco non ha
    /// (un caricamento interrotto, un file eliminato mentre era aperto). Restituisce cosa ha fatto, per il log.
    /// </summary>
    public List<string> Tidy()
    {
        var notes = new List<string>();
        if (!Directory.Exists(Folder))
            return notes;
        var missing = settings.Current.Aspetto.Media.Where(m => !File.Exists(Path.Combine(Folder, m.Id))).Select(m => m.Id).ToHashSet();
        if (missing.Count > 0)
        {
            var error = settings.Update(s =>
            {
                foreach (var id in missing)
                {
                    var item = s.Media(id);
                    notes.Add($"{item?.Nome ?? id}: il file non c'è più in {Folder}");
                    foreach (var logo in AppearanceSettings.Logos.Where(l => l.Get(s.Aspetto) == id))
                        logo.Set(s.Aspetto, null);
                    if (s.Aspetto.FondoMedia == id)
                    {
                        s.Aspetto.FondoMedia = null;
                        s.Aspetto.Fondo = "luci";
                    }
                    foreach (var set in s.Aspetto.Set)
                        set.Elementi.RemoveAll(e => e.Id == id);
                    foreach (var promo in s.Aspetto.Promozioni.Where(p => p.Immagine == id).ToList())
                    {
                        promo.Immagine = null;
                        if (promo.HasContent)
                            continue;
                        s.Aspetto.Promozioni.Remove(promo);
                        foreach (var set in s.Aspetto.Set)
                            set.Elementi.RemoveAll(e => e.Id == promo.Id);
                        notes.Add($"la promozione {promo.Nome} è tolta: senza il suo file non resta niente da mostrare");
                    }
                    s.Aspetto.Media.RemoveAll(m => m.Id == id);
                }
                return null;
            });
            if (error != null)
                notes.Add($"Non riesco a togliere i file mancanti dall'elenco: {error}");
        }
        var known = settings.Current.Aspetto.Media.Select(m => m.Id).ToHashSet();
        foreach (var file in Directory.EnumerateFiles(Folder))
        {
            var name = Path.GetFileName(file);
            if ((AgentSettings.MediaIdPattern().IsMatch(name) && !known.Contains(name)) || name.StartsWith(".caricamento-", StringComparison.Ordinal))
                TryDelete(file);
        }
        return notes;
    }

    static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
