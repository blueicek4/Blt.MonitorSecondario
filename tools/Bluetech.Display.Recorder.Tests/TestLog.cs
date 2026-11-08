using System;
using System.Collections.Generic;
using System.Linq;

namespace Bluetech.Display.Recorder.Tests;

/// <summary>Le righe a video del registratore, per i test.</summary>
sealed class TestLog : IRecorderLog
{
    readonly List<string> _lines = new();

    /// <summary>Chiamato a ogni riga, sul thread che la scrive: serve a far succedere qualcosa in un punto preciso.</summary>
    public Action<string> OnLine { get; set; }

    public void Line(string text, ConsoleColor? color = null)
    {
        lock (_lines)
            _lines.Add(text);
        OnLine?.Invoke(text);
    }

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines)
                return _lines.ToList();
        }
    }

    public bool Contains(string fragment)
    {
        lock (_lines)
            return _lines.Any(l => l.Contains(fragment));
    }
}
