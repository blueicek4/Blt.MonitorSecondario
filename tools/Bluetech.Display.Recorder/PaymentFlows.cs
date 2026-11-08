using System;
using System.Collections.Generic;

namespace Bluetech.Display.Recorder;

/// <summary>
/// I flussi di pagamento da registrare uno alla volta, con i passi da fare in cassa. Ogni versione del file registrata
/// durante un flusso porta il suo numero nell'indice, e ogni passo finisce nell'indice con l'ora: la registrazione si
/// legge senza dover ricordare cosa si stava facendo.
/// </summary>
public sealed class PaymentFlow
{
    public PaymentFlow(int number, string name, string why, bool glory, params string[] steps)
    {
        Number = number;
        Name = name;
        Why = why;
        Glory = glory;
        Steps = steps;
    }

    public int Number { get; }
    public string Name { get; }
    /// <summary>Cosa vogliamo scoprire con questo flusso, in una riga.</summary>
    public string Why { get; }
    /// <summary>Il flusso della cassa automatica: è quello in cui serve di più la cattura di rete.</summary>
    public bool Glory { get; }
    public IReadOnlyList<string> Steps { get; }

    public static readonly IReadOnlyList<PaymentFlow> All = new[]
    {
        new PaymentFlow(1, "Pagamento normale in contanti", "il conto, il PAGATO e il resto senza dispositivi", false,
            "Batti 3 articoli, uno dei quali due volte.",
            "Se lo usate, fai il conto (preconto).",
            "Incassa in contanti dando il resto (es. 50,00 su 23,50).",
            "Aspetta 10 secondi senza toccare niente."),
        new PaymentFlow(2, "Pagamento con transazione POS", "cosa arriva al monitor mentre il POS lavora", false,
            "Batti 2 articoli.",
            "Paga con carta sul POS collegato e aspetta l'esito.",
            "Se puoi, un secondo conto con la transazione rifiutata o annullata sul POS.",
            "Aspetta 10 secondi senza toccare niente."),
        new PaymentFlow(3, "Documento con attivazione Glory", "cosa mostra la cassa durante il pagamento con la cassa automatica", true,
            "Batti 2 articoli con un totale non tondo (es. 23,50).",
            "Avvia il pagamento con la cassa automatica.",
            "Inserisci il contante un pezzo alla volta: prima meno del totale, poi il resto, così che dia il resto.",
            "Ritira il resto e aspetta che la cassa chiuda il documento.",
            "Un secondo conto: avvia il pagamento con la cassa automatica e annullalo a metà inserimento.",
            "Aspetta 10 secondi senza toccare niente."),
        new PaymentFlow(4, "Documento con Satispay", "il QR a video, il pagamento riuscito e quello che non va a buon fine", false,
            "Batti 2 articoli.",
            "Scegli Satispay: deve comparire il QR.",
            "Paga dall'app Satispay e aspetta la conferma.",
            "Un secondo conto con Satispay: annullalo o lascia scadere il QR.",
            "Aspetta 10 secondi senza toccare niente."),
        new PaymentFlow(5, "Documento con scontrino digitale", "il QR dello scontrino digitale dopo il pagamento", false,
            "Batti 2 articoli.",
            "Paga (contanti o carta) con lo scontrino digitale attivo.",
            "Aspetta il QR dello scontrino digitale e lascialo a video.",
            "Aspetta 30 secondi senza toccare niente."),
        new PaymentFlow(6, "Annullo scontrino", "il documento di annullo e cosa resta a video", false,
            "Batti 2 articoli e chiudi lo scontrino con un pagamento.",
            "Fai l'annullo dello scontrino appena emesso (documento di annullo).",
            "Aspetta 10 secondi senza toccare niente."),
    };

    public static PaymentFlow Find(int number) => number >= 1 && number <= All.Count ? All[number - 1] : null;
}

/// <summary>
/// Dove si è nel percorso: il flusso in corso, il passo mostrato, quelli già fatti. Solo stato e testi: le righe
/// a video e nell'indice le scrive chi lo usa.
/// </summary>
public sealed class FlowGuide
{
    readonly HashSet<int> _done = new();
    readonly HashSet<int> _interrupted = new();

    public PaymentFlow Current { get; private set; }
    /// <summary>Indice del passo mostrato (0 = il primo); uguale a Steps.Count quando i passi sono finiti.</summary>
    public int Step { get; private set; }
    public IReadOnlyCollection<int> Done => _done;

    public bool StepsFinished => Current != null && Step >= Current.Steps.Count;

    /// <summary>Comincia un flusso. Se ce n'era un altro in corso lo restituisce come interrotto.</summary>
    public PaymentFlow Begin(PaymentFlow flow)
    {
        var previous = Current;
        if (previous != null)
            _interrupted.Add(previous.Number);
        Current = flow ?? throw new ArgumentNullException(nameof(flow));
        Step = 0;
        return previous;
    }

    /// <summary>Il passo fatto: si passa al successivo. Restituisce false se i passi erano già finiti.</summary>
    public bool Advance()
    {
        if (Current == null || StepsFinished)
            return false;
        Step++;
        return true;
    }

    /// <summary>Chiude il flusso in corso; completo se tutti i passi sono stati segnati.</summary>
    public PaymentFlow End(out bool complete)
    {
        var flow = Current;
        complete = flow != null && StepsFinished;
        if (flow != null)
        {
            if (complete)
            {
                _done.Add(flow.Number);
                _interrupted.Remove(flow.Number);
            }
            else
            {
                _interrupted.Add(flow.Number);
            }
        }
        Current = null;
        Step = 0;
        return flow;
    }

    /// <summary>"1 ✓ · 2 ✓ · 3 (interrotto) · 4 · 5 · 6": a che punto è il giro.</summary>
    public string Progress()
    {
        var parts = new List<string>();
        foreach (var flow in PaymentFlow.All)
        {
            var mark = _done.Contains(flow.Number) ? " ✓"
                : Current?.Number == flow.Number ? " (in corso)"
                : _interrupted.Contains(flow.Number) ? " (interrotto)"
                : "";
            parts.Add(flow.Number + mark);
        }
        return string.Join(" · ", parts);
    }
}
