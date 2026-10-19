using System;
using System.Collections.Generic;

namespace Bluetech.Display.Recorder;

public enum DiffOp
{
    Same,
    Added,
    Removed,
}

public sealed class DiffLine
{
    public DiffLine(DiffOp op, string text, int index)
    {
        Op = op;
        Text = text;
        Index = index;
    }

    public DiffOp Op { get; }
    public string Text { get; }
    /// <summary>Posizione nella nuova versione (Same/Added) o nella precedente (Removed).</summary>
    public int Index { get; }
}

/// <summary>
/// Confronto tra il conto in memoria e quello appena riscritto da Passepartout (che rimanda sempre il conto intero).
/// Sottosequenza comune più lunga, percorsa dall'inizio: a parità, le righe nuove risultano in fondo,
/// così "un altro cappuccino" si anima come ultima riga e non al posto di un cappuccino già presente.
/// </summary>
public static class ReceiptDiff
{
    const long MaxCells = 4_000_000;

    public static List<DiffLine> Compute(IReadOnlyList<string> previous, IReadOnlyList<string> current, Func<string, string> key)
    {
        var result = new List<DiffLine>();
        int n = previous.Count, m = current.Count;
        var a = new string[n];
        var b = new string[m];
        for (var i = 0; i < n; i++) a[i] = key(previous[i]);
        for (var j = 0; j < m; j++) b[j] = key(current[j]);

        if ((long)(n + 1) * (m + 1) > MaxCells)
        {
            // Conti enormi: niente confronto riga per riga, si ridisegna tutto.
            for (var i = 0; i < n; i++) result.Add(new DiffLine(DiffOp.Removed, previous[i], i));
            for (var j = 0; j < m; j++) result.Add(new DiffLine(DiffOp.Added, current[j], j));
            return result;
        }

        // lcs[i, j] = lunghezza della sottosequenza comune più lunga tra a[i..] e b[j..]
        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y])
            {
                result.Add(new DiffLine(DiffOp.Same, current[y], y));
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                result.Add(new DiffLine(DiffOp.Removed, previous[x], x));
                x++;
            }
            else
            {
                result.Add(new DiffLine(DiffOp.Added, current[y], y));
                y++;
            }
        }
        for (; x < n; x++) result.Add(new DiffLine(DiffOp.Removed, previous[x], x));
        for (; y < m; y++) result.Add(new DiffLine(DiffOp.Added, current[y], y));
        return result;
    }
}
