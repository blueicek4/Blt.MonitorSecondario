using System.Security.Cryptography;
using Bluetech.Display.Agent.Impostazioni;
using Bluetech.Display.Agent.Server;

namespace Bluetech.Display.Agent.Aspetto;

/// <summary>
/// Le foto e i video caricati dal back office (le gallerie, i loghi, lo sfondo, i loghi dei kit), in dati\media. Il nome
/// del file è l'impronta del contenuto: lo stesso file caricato due volte è uno solo, anche in due marchi, e un display lo
/// tiene in cache per sempre perché a un nome corrisponde sempre lo stesso contenuto. Nelle impostazioni ci sono gli
/// elenchi, uno per marchio e uno per i kit, con il nome originale.
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

    /// <summary>Le foto e i video di un marchio, o quelli dei kit (<see cref="Kit"/>): la lista dove finisce un caricamento.</summary>
    public const string Kit = "kit";

    static List<MediaItem>? ListOf(AgentSettings s, string target) =>
        target == Kit ? s.Installazione.Media : s.Brand(target)?.Contenuti.Media;

    /// <summary>Tutti i file caricati, da tutti i marchi e dai kit (lo stesso file può stare in più elenchi).</summary>
    public static IEnumerable<MediaItem> All(AgentSettings s) =>
        s.Installazione.Media.Concat(s.Marchi.SelectMany(b => b.Contenuti.Media));

    /// <summary>
    /// Salva un file caricato nell'elenco <paramref name="target"/> (un marchio o i kit): lo legge una volta sola
    /// (impronta e controlli mentre arriva), lo aggiunge all'elenco e lo restituisce, o restituisce l'errore. Con
    /// <paramref name="posterOf"/> è il primo fotogramma di quel video (sfocato con <paramref name="blurred"/>), fatto dal
    /// back office al caricamento: il display non sfoca mai a runtime.
    /// </summary>
    public async Task<(MediaItem? Item, string? Error)> SaveAsync(Stream body, string? name, string target, CancellationToken cancel,
        string? posterOf = null, bool blurred = false)
    {
        if (ListOf(settings.Current, target) == null)
            return (null, "Il marchio non c'è più.");
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
            if (target == Kit && kind.IsVideo)
                return (null, "I loghi dei kit sono immagini (PNG con lo sfondo trasparente).");
            if (posterOf != null && kind.IsVideo)
                return (null, "Il fotogramma di un video è un'immagine.");

            var id = Convert.ToHexStringLower(sha.GetHashAndReset())[..20] + kind.Extension;
            var known = All(settings.Current).Any(m => m.Id == id) && File.Exists(Path.Combine(Folder, id));
            if (!known && All(settings.Current).DistinctBy(m => m.Id).Sum(m => m.Byte) + total > MaxTotalBytes)
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
                Derivato = posterOf != null,
            };
            var error = settings.Update(s =>
            {
                var list = ListOf(s, target);
                if (list == null)
                    return "Il marchio non c'è più.";
                var existing = list.Find(m => m.Id == id);
                if (existing == null)
                    list.Add(existing = item);
                else if (posterOf == null)
                    existing.Derivato = false;   // caricato di nuovo come foto: ora si vede anche nella galleria
                if (posterOf != null)
                {
                    if (list.Find(m => m.Id == posterOf) is not { IsVideo: true } video)
                        return "Il video non c'è più.";
                    if (blurred)
                        video.PosterSfocato = id;
                    else
                        video.Poster = id;
                }
                return null;
            });
            if (error != null)
                return (null, error);
            log.LogInformation("Caricato {Name} ({Type}, {Bytes} byte) come {Id} in {Target}{Poster}", item.Nome, item.Tipo, item.Byte, id, target,
                posterOf == null ? "" : $", fotogramma {(blurred ? "sfocato " : "")}di {posterOf}");
            return (ListOf(settings.Current, target)?.Find(m => m.Id == id), null);
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
        if (!AgentSettings.MediaIdPattern().IsMatch(id) || !All(settings.Current).Any(m => m.Id == id))
            return null;
        var path = Path.Combine(Folder, id);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Dove è usato un file in un marchio: il logo, lo sfondo, le promozioni, le vetrine, i video di cui è il fotogramma.</summary>
    public static List<string> UsedBy(BrandConfig brand, string id)
    {
        var used = AgentSettings.IdentityImages.Where(l => l.Get(brand) == id).Select(l => l.Name).ToList();
        used.AddRange(brand.Contenuti.Promozioni.Where(p => p.Immagine == id).Select(p => $"la promozione {p.Nome}"));
        used.AddRange(brand.Contenuti.Vetrine.Where(x => x.Elementi.Any(e => e.Id == id)).Select(x => $"la vetrina {x.Nome}"));
        used.AddRange(brand.Contenuti.Media.Where(m => m.Poster == id || m.PosterSfocato == id).Select(m => $"il video {m.Nome}"));
        return used;
    }

    /// <summary>
    /// Toglie un file da un elenco (un marchio o i kit), con i fotogrammi del video se nessuno li usa; se è ancora usato,
    /// dice dove. Il file sul disco se ne va quando non lo ha più nessun elenco.
    /// </summary>
    public string? Delete(string id, string target)
    {
        var gone = new List<string>();
        var error = settings.Update(s =>
        {
            var list = ListOf(s, target);
            if (list?.Find(m => m.Id == id) is not { } item)
                return "Il file non c'è.";
            if (target == Kit)
            {
                var used = KitLogos.All.Where(l => l.Get(s.Installazione.Loghi) == id).Select(l => l.Name).ToList();
                if (used.Count > 0)
                    return $"Il file serve ancora: è {string.Join(", ", used)}. Toglilo da lì e poi eliminalo.";
                list.Remove(item);
                gone.Add(id);
                return null;
            }
            var brand = s.Brand(target)!;
            var users = UsedBy(brand, id).Where(u => !u.StartsWith("il video ", StringComparison.Ordinal) || item.Derivato).ToList();
            if (users.Count > 0)
                return $"Il file serve ancora: lo usa {string.Join(", ", users)}. Toglilo da lì e poi eliminalo.";
            list.Remove(item);
            gone.Add(id);
            foreach (var derived in new[] { item.Poster, item.PosterSfocato }.OfType<string>())
            {
                if (list.Find(m => m.Id == derived) is { Derivato: true } frame && UsedBy(brand, derived).Count == 0)
                {
                    list.Remove(frame);
                    gone.Add(derived);
                }
            }
            return null;
        });
        if (error != null)
            return error;
        var still = All(settings.Current).Select(m => m.Id).ToHashSet();
        foreach (var file in gone.Where(g => !still.Contains(g)))
            TryDelete(Path.Combine(Folder, file));
        return null;
    }

    /// <summary>
    /// All'avvio: dagli elenchi i file che sul disco non ci sono più (tolti a mano), dal disco quelli che nessun elenco ha
    /// (un caricamento interrotto, un file eliminato mentre era aperto). Restituisce cosa ha fatto, per il log.
    /// </summary>
    public List<string> Tidy()
    {
        var notes = new List<string>();
        if (!Directory.Exists(Folder))
            return notes;
        var missing = All(settings.Current).Where(m => !File.Exists(Path.Combine(Folder, m.Id))).Select(m => m.Id).ToHashSet();
        if (missing.Count > 0)
        {
            var error = settings.Update(s =>
            {
                foreach (var item in All(s).Where(m => missing.Contains(m.Id)).DistinctBy(m => m.Id))
                    notes.Add($"{item.Nome}: il file non c'è più in {Folder}");
                s.Installazione.Media.RemoveAll(m => missing.Contains(m.Id));
                foreach (var brand in s.Marchi)
                {
                    brand.Contenuti.Media.RemoveAll(m => missing.Contains(m.Id));
                    foreach (var promo in brand.Contenuti.Promozioni.Where(p => p.Immagine != null && missing.Contains(p.Immagine)).ToList())
                    {
                        promo.Immagine = null;
                        if (promo.Modello is "B" or "C")
                            promo.Modello = PromoConfig.Suggested(promo.HasQr, null);
                        if (promo.HasContent)
                            continue;
                        brand.Contenuti.Promozioni.Remove(promo);
                        foreach (var showcase in brand.Contenuti.Vetrine)
                            showcase.Elementi.RemoveAll(e => e.Id == promo.Id);
                        notes.Add($"la promozione {promo.Nome} è tolta: senza il suo file non resta niente da mostrare");
                    }
                }
                notes.AddRange(s.Repair());   // loghi, sfondo, fotogrammi, vetrine: quello che puntava ai file spariti
                return null;
            });
            if (error != null)
                notes.Add($"Non riesco a togliere i file mancanti dagli elenchi: {error}");
        }
        var known = All(settings.Current).Select(m => m.Id).ToHashSet();
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
