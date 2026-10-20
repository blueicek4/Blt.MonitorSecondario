using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bluetech.Display.Agent.Impostazioni;
using Bluetech.Display.Agent.Vendita;

namespace Bluetech.Display.Agent.Server;

/// <summary>
/// I conteggi per il back office, giorno per giorno e cassa per cassa, in dati\statistiche.json (salvato ogni minuto e
/// alla chiusura). Sono quello che ha visto il monitor, non i dati fiscali: quelli restano nel gestionale.
/// </summary>
public sealed class Statistics
{
    public const string FileName = "statistiche.json";
    const int KeepDays = 400;
    static readonly UTF8Encoding Utf8 = new(false);

    public sealed class DayCounters
    {
        /// <summary>Versioni del file lette.</summary>
        public long File { get; set; }
        public long Vendite { get; set; }
        /// <summary>Conti chiusi con la riga PAGATO.</summary>
        public long Pagate { get; set; }
        /// <summary>Centesimi pagati (importo meno resto) dei conti con PAGATO.</summary>
        public long Incasso { get; set; }
        /// <summary>Conti chiusi per inattività, senza PAGATO.</summary>
        public long SenzaPagamento { get; set; }
        public long QrPagamento { get; set; }
        public long ScontriniDigitali { get; set; }
        public long CopieCartacee { get; set; }
        public long CopieNonStampate { get; set; }
    }

    public sealed class DeviceSeen
    {
        public DateTimeOffset UltimoAccesso { get; set; }
        public string Indirizzo { get; set; } = "";
        public long Collegamenti { get; set; }
        public long CopieCartacee { get; set; }
    }

    sealed class Data
    {
        /// <summary>giorno (aaaa-mm-gg) → cassa → contatori.</summary>
        public SortedDictionary<string, Dictionary<string, DayCounters>> Giorni { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, DeviceSeen> Dispositivi { get; set; } = [];
    }

    readonly object _lock = new();
    readonly string _path;
    readonly Data _data;
    bool _dirty;

    public Statistics(string folder)
    {
        _path = Path.Combine(folder, FileName);
        _data = Load(_path, out var problem);
        LoadProblem = problem;
    }

    public string? LoadProblem { get; }

    static Data Load(string path, out string? problem)
    {
        problem = null;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Data>(File.ReadAllText(path, Utf8), AgentSettings.Json) ?? new Data();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            problem = $"{FileName} non leggibile ({ex.Message}): i conteggi ripartono da zero";
            try
            {
                File.Move(path, $"{path}.errore-{DateTime.Now:yyyyMMdd-HHmmss}");
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
            }
        }
        return new Data();
    }

    DayCounters Today(string cashPoint)
    {
        var day = DateTime.Now.ToString("yyyy-MM-dd");
        if (!_data.Giorni.TryGetValue(day, out var cashPoints))
        {
            _data.Giorni[day] = cashPoints = [];
            while (_data.Giorni.Count > KeepDays)
                _data.Giorni.Remove(_data.Giorni.Keys.First());
        }
        if (!cashPoints.TryGetValue(cashPoint, out var counters))
            cashPoints[cashPoint] = counters = new DayCounters();
        _dirty = true;
        return counters;
    }

    public void FileRead(string cashPoint)
    {
        lock (_lock)
            Today(cashPoint).File++;
    }

    /// <summary>Conta quello che il tracker ha mandato ai display.</summary>
    public void Count(string cashPoint, IReadOnlyList<CashIn> messages)
    {
        if (messages.Count == 0)
            return;
        lock (_lock)
        {
            CashIn? previous = null;
            foreach (var message in messages)
            {
                switch (message)
                {
                    case SaleOpen:
                        Today(cashPoint).Vendite++;
                        break;
                    case SaleClose { Paid: { } paid }:
                        var counters = Today(cashPoint);
                        counters.Pagate++;
                        counters.Incasso += paid.Amount - (paid.Change ?? 0);
                        break;
                    case SaleClose when previous is PayClear:
                        break;   // un QR scaduto dopo la chiusura: il conto era già contato
                    case SaleClose:
                        Today(cashPoint).SenzaPagamento++;
                        break;
                    case PayQr:
                        Today(cashPoint).QrPagamento++;
                        break;
                    case ReceiptQr:
                    case ReceiptDigital:
                        Today(cashPoint).ScontriniDigitali++;
                        break;
                }
                previous = message;
            }
        }
    }

    public void PaperCopy(string cashPoint, string? deviceId, bool printed)
    {
        lock (_lock)
        {
            var counters = Today(cashPoint);
            if (printed)
            {
                counters.CopieCartacee++;
                if (deviceId != null && _data.Dispositivi.TryGetValue(deviceId, out var seen))
                    seen.CopieCartacee++;
            }
            else
            {
                counters.CopieNonStampate++;
            }
        }
    }

    /// <summary>Un dispositivo si è fatto vivo (pagina aperta o display collegato).</summary>
    public void Seen(string deviceId, string remote, bool connected)
    {
        lock (_lock)
        {
            if (!_data.Dispositivi.TryGetValue(deviceId, out var seen))
                _data.Dispositivi[deviceId] = seen = new DeviceSeen();
            seen.UltimoAccesso = DateTimeOffset.Now;
            seen.Indirizzo = remote;
            if (connected)
                seen.Collegamenti++;
            _dirty = true;
        }
    }

    public DeviceSeen? LastSeen(string deviceId)
    {
        lock (_lock)
            return _data.Dispositivi.TryGetValue(deviceId, out var seen)
                ? new DeviceSeen { UltimoAccesso = seen.UltimoAccesso, Indirizzo = seen.Indirizzo, Collegamenti = seen.Collegamenti, CopieCartacee = seen.CopieCartacee }
                : null;
    }

    /// <summary>Le casse che hanno dei conteggi, anche tolte: i loro id non si riusano per una cassa nuova.</summary>
    public HashSet<string> CashPointIds()
    {
        lock (_lock)
            return _data.Giorni.Values.SelectMany(d => d.Keys).ToHashSet();
    }

    public void Forget(string deviceId)
    {
        lock (_lock)
            _dirty |= _data.Dispositivi.Remove(deviceId);
    }

    /// <summary>
    /// I giorni del calendario da oggi indietro (1 = oggi, 7 = l'ultima settimana), dal più recente; quelli senza niente
    /// non ci sono.
    /// </summary>
    public JsonArray Report(int days)
    {
        var from = DateTime.Today.AddDays(1 - Math.Clamp(days, 1, KeepDays)).ToString("yyyy-MM-dd");
        lock (_lock)
        {
            var result = new JsonArray();
            foreach (var (day, cashPoints) in _data.Giorni.Reverse().Where(d => string.CompareOrdinal(d.Key, from) >= 0))
            {
                var casse = new JsonObject();
                foreach (var (id, counters) in cashPoints)
                    casse[id] = JsonSerializer.SerializeToNode(counters, AgentSettings.Json);
                result.Add(new JsonObject { ["giorno"] = day, ["casse"] = casse });
            }
            return result;
        }
    }

    public void SaveIfDirty()
    {
        string json;
        lock (_lock)
        {
            if (!_dirty)
                return;
            json = JsonSerializer.Serialize(_data, AgentSettings.Json);
            _dirty = false;
        }
        try
        {
            var temp = _path + ".tmp";
            File.WriteAllText(temp, json, Utf8);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_lock)
                _dirty = true;   // al prossimo giro
        }
    }
}
