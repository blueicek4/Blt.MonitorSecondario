using System.Globalization;
using Bluetech.Display.Agent.Impostazioni;

namespace Bluetech.Display.Agent.Aspetto;

/// <summary>
/// ScegliVetrina (config.ts del design, SPEC §18): quale vetrina va in onda in un posto di una cassa, adesso, e com'è
/// ognuna delle altre. Una funzione pura sola, usata dall'Agent per decidere cosa mandare agli schermi e dal back office
/// per spiegare "perché vedo questo adesso?": due copie divergono, sempre. La catena, nell'ordine:
/// 1. le vetrine di quel posto; 2. quelle la cui regola vale adesso per la cassa (giorno, ora, date); 3. quelle con
/// qualcosa da mostrare oggi; 4. la prima dell'elenco va in onda, le altre valide sono coperte.
/// </summary>
public static class Showcases
{
    public const string OnAir = "in-onda";
    public const string Covered = "coperta";
    public const string OffHours = "fuori-orario";
    public const string Empty = "vuota";
    public const string Off = "spenta";

    /// <param name="Stato">Uno dei cinque.</param>
    /// <param name="Da">Coperta: la vetrina che va in onda al suo posto.</param>
    /// <param name="Torna">Fuori orario: quando torna valida, se succede nei prossimi 14 giorni.</param>
    public sealed record State(string Stato, string? Da = null, DateTime? Torna = null);

    /// <summary>Un elemento che si può mostrare: una foto, un video o una promozione, con quanto resta.</summary>
    /// <param name="Seconds">Null per un video: dura quanto dura.</param>
    public sealed record Entry(string Id, string Name, MediaItem? Media, PromoConfig? Promo, int? Seconds)
    {
        public bool HasQr => Promo?.HasQr == true;
    }

    /// <param name="Winner">La vetrina in onda; null = niente in quel posto.</param>
    /// <param name="Items">I suoi elementi validi oggi, nel suo ordine.</param>
    /// <param name="States">Le vetrine di quel posto che valgono per quella cassa, ognuna col suo stato.</param>
    public sealed record Choice(ShowcaseConfig? Winner, List<Entry> Items, Dictionary<string, State> States);

    /// <summary>Una foto con il QR resta almeno tanto: il tempo di tirare fuori il telefono (SPEC §16).</summary>
    public const int MinQrSeconds = 15;

    /// <param name="force">Per l'anteprima del back office: questa vetrina in onda nei suoi posti, accesa o no, a qualunque ora.</param>
    public static Choice Choose(BrandConfig brand, string cashPoint, string place, DateTime now, string? force = null)
    {
        var states = new Dictionary<string, State>();
        ShowcaseConfig? winner = null;
        List<Entry> items = [];
        foreach (var showcase in brand.Contenuti.Vetrine)
        {
            if (!showcase.Posti.Contains(place))
                continue;   // 1
            if (force != null)
            {
                if (showcase.Id == force)
                {
                    winner = showcase;
                    items = Usable(brand, showcase, DateOnly.FromDateTime(now));
                    states[showcase.Id] = new State(OnAir);
                }
                continue;
            }
            var rule = brand.Rule(showcase.Regola);
            if (rule is { Casse.Count: > 0 } && !rule.Casse.Contains(cashPoint))
                continue;   // non è per questa cassa: per lei non esiste
            if (!showcase.Attiva)
            {
                states[showcase.Id] = new State(Off);
                continue;
            }
            if (showcase.Elementi.Count == 0)
            {
                states[showcase.Id] = new State(Empty);
                continue;
            }
            if (ServiceDay(rule, cashPoint, now) is not { } day)
            {
                states[showcase.Id] = new State(OffHours, Torna: NextStart(rule, cashPoint, now));   // 2
                continue;
            }
            var usable = Usable(brand, showcase, day);
            if (usable.Count == 0)
            {
                states[showcase.Id] = new State(Empty);   // 3
                continue;
            }
            if (winner == null)
            {
                winner = showcase;   // 4
                items = usable;
                states[showcase.Id] = new State(OnAir);
            }
            else
            {
                states[showcase.Id] = new State(Covered, Da: winner.Id);
            }
        }
        return new Choice(winner, items, states);
    }

    /// <summary>
    /// Com'è una vetrina adesso, guardando tutte le casse del marchio e tutti i suoi posti: in onda (e dove), coperta (e
    /// da chi), fuori orario (e quando torna), vuota, spenta. È la riga di stato dell'elenco delle vetrine.
    /// </summary>
    public sealed record Overview(string Stato, List<(string Cassa, string Posto)> Dove, List<string> CopertaDa, DateTime? Torna, bool NessunaCassa);

    public static Dictionary<string, Overview> Overviews(AgentSettings settings, BrandConfig brand, DateTime now)
    {
        var cashPoints = settings.Casse.Where(c => settings.BrandOf(c).Id == brand.Id).ToList();
        var seen = brand.Contenuti.Vetrine.ToDictionary(v => v.Id, _ => new List<(string Cassa, string Posto, State State)>());
        foreach (var cash in cashPoints)
        foreach (var place in ShowcaseConfig.Places)
        foreach (var (id, state) in Choose(brand, cash.Id, place, now).States)
            seen[id].Add((cash.Id, place, state));

        var result = new Dictionary<string, Overview>();
        foreach (var showcase in brand.Contenuti.Vetrine)
        {
            var states = seen[showcase.Id];
            var onAir = states.Where(s => s.State.Stato == OnAir).Select(s => (s.Cassa, s.Posto)).ToList();
            var by = states.Where(s => s.State.Stato == Covered).Select(s => s.State.Da!).Distinct().ToList();
            var back = states.Where(s => s.State.Stato == OffHours && s.State.Torna != null).Select(s => s.State.Torna!.Value).DefaultIfEmpty().Min();
            var stato = !showcase.Attiva ? Off
                : onAir.Count > 0 ? OnAir
                : by.Count > 0 ? Covered
                : states.Any(s => s.State.Stato == OffHours) ? OffHours
                : states.Count > 0 || showcase.Elementi.Count == 0 ? Empty
                : OffHours;   // nessuna cassa del marchio è nella sua regola
            result[showcase.Id] = new Overview(stato, onAir, by, back == default ? null : back, states.Count == 0 && showcase.Attiva && showcase.Elementi.Count > 0);
        }
        return result;
    }

    /// <summary>Gli elementi della vetrina che si possono mostrare oggi: le promozioni fuori dalle loro date non contano.</summary>
    public static List<Entry> Usable(BrandConfig brand, ShowcaseConfig showcase, DateOnly day)
    {
        var items = new List<Entry>();
        foreach (var element in showcase.Elementi)
        {
            var seconds = Math.Clamp(element.Secondi ?? showcase.Secondi, 3, 120);
            if (brand.Media(element.Id) is { Derivato: false } media)
                items.Add(new Entry(media.Id, media.Nome, media, null, media.IsVideo ? null : seconds));
            else if (brand.Promo(element.Id) is { } promo && promo.HasContent && !(promo.DalGiorno > day) && !(promo.AlGiorno < day))
                items.Add(new Entry(promo.Id, promo.Nome, null, promo, promo.HasQr ? Math.Max(seconds, MinQrSeconds) : seconds));
        }
        return items;
    }

    const int MinRandomLength = 60;
    const int MaxLength = 300;

    /// <summary>
    /// La sequenza che lo schermo ripete: come nell'elenco, per nome o a caso (giri mescolati uno dopo l'altro, senza lo
    /// stesso elemento due volte di fila tra un giro e l'altro). Con "un QR ogni N" le promozioni con il QR vanno a parte:
    /// dopo N elementi senza QR, la prossima con il QR, e le due liste fanno giri interi. A caso il seme viene dalla vetrina
    /// e non dall'ora: la stessa vetrina dà sempre la stessa sequenza, e il messaggio non cambia a ogni controllo.
    /// </summary>
    public static List<Entry> Sequence(ShowcaseConfig showcase, List<Entry> items)
    {
        if (items.Count == 0)
            return items;
        if (showcase.Rotazione == "alfabetico")
            items = items.OrderBy(i => i.Name, StringComparer.Create(CultureInfo.GetCultureInfo("it-IT"), CompareOptions.IgnoreCase)).ToList();
        var random = showcase.Rotazione == "casuale" ? new Random(Seed(showcase, items)) : null;
        var after = Math.Clamp(showcase.QrOgni, 0, 20);
        var plain = after == 0 ? items : items.Where(i => !i.HasQr).ToList();
        var qr = after == 0 ? [] : items.Where(i => i.HasQr).ToList();
        if (qr.Count == 0 || plain.Count == 0)
        {
            if (random == null)
                return items;
            var rounds = Math.Clamp((MinRandomLength + items.Count - 1) / items.Count, 1, Math.Max(1, MaxLength / items.Count));
            return Rounds(items, rounds * items.Count, random);
        }
        var blockLength = after + 1;
        var blocks = Lcm(plain.Count / Gcd(plain.Count, after), qr.Count);
        if (random != null && blocks * blockLength < MinRandomLength)
            blocks *= (MinRandomLength + blocks * blockLength - 1) / (blocks * blockLength);
        blocks = Math.Min(blocks, Math.Max(1, MaxLength / blockLength));
        var plainStream = Rounds(plain, (int)blocks * after, random);
        var qrStream = Rounds(qr, (int)blocks, random);
        var result = new List<Entry>((int)blocks * blockLength);
        for (var b = 0; b < blocks; b++)
        {
            result.AddRange(plainStream.GetRange(b * after, after));
            result.Add(qrStream[b]);
        }
        return result;
    }

    /// <summary>Tanti elementi quanti ne servono, un giro dopo l'altro; a caso ogni giro è mescolato.</summary>
    static List<Entry> Rounds(List<Entry> items, int count, Random? random)
    {
        var result = new List<Entry>(count);
        while (result.Count < count)
        {
            var round = items.ToArray();
            if (random != null)
            {
                random.Shuffle(round);
                if (round.Length > 1 && result.Count > 0 && round[0].Id == result[^1].Id)
                {
                    var other = 1 + random.Next(round.Length - 1);
                    (round[0], round[other]) = (round[other], round[0]);
                }
            }
            result.AddRange(round.Take(count - result.Count));
        }
        return result;
    }

    static int Seed(ShowcaseConfig showcase, List<Entry> items)
    {
        // FNV-1a: string.GetHashCode cambia a ogni avvio, il seme deve restare lo stesso.
        var hash = 2166136261u;
        foreach (var c in showcase.Id + "|" + string.Join(",", items.Select(i => i.Id)))
            hash = unchecked((hash ^ c) * 16777619u);
        return (int)(hash & 0x7FFFFFFF);
    }

    static long Gcd(long a, long b)
    {
        while (b != 0)
            (a, b) = (b, a % b);
        return a;
    }

    static long Lcm(long a, long b) => a / Gcd(a, b) * b;

    /// <summary>
    /// Il giorno a cui appartiene adesso la regola, se vale adesso per questa cassa; null se no. Senza regola: sempre,
    /// oggi. Una fascia a cavallo della mezzanotte (22:00-02:00) appartiene al giorno in cui comincia: il venerdì sera
    /// dura fino alle 2 di sabato, e una promozione valida fino a venerdì c'è ancora all'una.
    /// </summary>
    public static DateOnly? ServiceDay(RuleConfig? rule, string cashPoint, DateTime now)
    {
        var day = DateOnly.FromDateTime(now);
        if (rule == null)
            return day;
        if (rule.Casse.Count > 0 && !rule.Casse.Contains(cashPoint))
            return null;
        var time = TimeOnly.FromDateTime(now);
        if (AgentSettings.ParseTime(rule.Dalle) is { } from && AgentSettings.ParseTime(rule.Alle) is { } to)
        {
            if (from < to)
            {
                if (time < from || time >= to)
                    return null;
            }
            else if (time < to)
            {
                day = day.AddDays(-1);
            }
            else if (time < from)
            {
                return null;
            }
        }
        if (rule.DalGiorno is { } first && day < first)
            return null;
        if (rule.AlGiorno is { } last && day > last)
            return null;
        return rule.Giorni.Count == 0 || rule.Giorni.Contains(Weekday(day)) ? day : null;
    }

    /// <summary>1 = lunedì … 7 = domenica.</summary>
    public static int Weekday(DateOnly day) => day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)day.DayOfWeek;

    /// <summary>Quando la regola torna a valere per la cassa, nei prossimi 14 giorni (dal suo primo giorno, se è futuro); null se no.</summary>
    public static DateTime? NextStart(RuleConfig? rule, string cashPoint, DateTime now)
    {
        if (rule == null || (rule.Casse.Count > 0 && !rule.Casse.Contains(cashPoint)))
            return null;
        var start = DateOnly.FromDateTime(now);
        if (rule.DalGiorno is { } first && first > start)
            start = first;
        var at = AgentSettings.ParseTime(rule.Dalle) is { } from && AgentSettings.ParseTime(rule.Alle) != null ? from : TimeOnly.MinValue;
        for (var i = 0; i <= 14; i++)
        {
            var candidate = start.AddDays(i).ToDateTime(at);
            if (candidate > now && ServiceDay(rule, cashPoint, candidate) != null)
                return candidate;
        }
        return null;
    }

    /// <summary>"venerdì alle 18:00", "domani alle 18:00", "oggi alle 18:00", "il 12/11 alle 18:00".</summary>
    public static string When(DateTime at, DateTime now)
    {
        var italian = CultureInfo.GetCultureInfo("it-IT");
        var days = (DateOnly.FromDateTime(at).DayNumber - DateOnly.FromDateTime(now).DayNumber);
        var day = days switch
        {
            0 => "oggi",
            1 => "domani",
            < 7 => italian.DateTimeFormat.GetDayName(at.DayOfWeek),
            _ => "il " + at.ToString("d/M", italian),
        };
        return at.TimeOfDay == TimeSpan.Zero ? day : $"{day} alle {at:HH:mm}";
    }
}
