using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Bluetech.Display.Recorder;

/// <summary>Una foto dello schermo: il JPEG da salvare e un'impronta piccola per capire se è cambiato qualcosa.</summary>
public sealed class ScreenShot
{
    public byte[] Jpeg;
    /// <summary>Luminosità media di una griglia di celle; la finestra del registratore è esclusa (vale 0).</summary>
    public byte[] Fingerprint;
    public int Width;
    public int Height;
}

public interface IScreenCamera
{
    /// <summary>Tutti gli schermi in una foto, o null se lo schermo non si può leggere (sessione bloccata, desktop remoto chiuso).</summary>
    ScreenShot Take();
}

/// <summary>
/// Le foto dello schermo della cassa durante un flusso: ogni secondo, solo se qualcosa è cambiato, e a ogni versione
/// del file. Servono a vedere cosa mostra Passepartout (la finestra della cassa automatica, del POS, di Satispay)
/// accanto a quello che scrive per il monitor. Le foto finiscono in "foto\" con una riga nell'indice.
/// </summary>
public sealed class ScreenRecorder : IDisposable
{
    public const int IntervalMilliseconds = 1000;
    public const int MaxPhotos = 2000;
    public const long MaxBytes = 800L * 1024 * 1024;
    /// <summary>Differenza di luminosità media di una cella oltre la quale lo schermo è cambiato (cifre che cambiano incluse).</summary>
    public const int ChangeThreshold = 6;
    const int FailuresBeforeGivingUp = 3;

    readonly IScreenCamera _camera;
    readonly int _interval;
    readonly RecordingSession _session;
    readonly IRecorderLog _log;
    readonly object _sync = new();
    Timer _timer;
    bool _on;
    bool _stopped;
    string _disabledReason;
    byte[] _lastFingerprint;
    string _lastFile;
    int _failures;
    int _count;
    long _bytes;

    /// <param name="intervalMilliseconds">Ogni quanto guardare lo schermo; Timeout.Infinite = solo a comando (test).</param>
    public ScreenRecorder(IScreenCamera camera, RecordingSession session, IRecorderLog log, int intervalMilliseconds = IntervalMilliseconds)
    {
        _camera = camera;
        _interval = intervalMilliseconds;
        _session = session;
        _log = log;
        if (camera == null)
            _disabledReason = "le foto dello schermo ci sono solo su Windows";
    }

    public bool On { get { lock (_sync) return _on; } }
    public int Count { get { lock (_sync) return _count; } }
    public long Bytes { get { lock (_sync) return _bytes; } }
    /// <summary>Perché le foto non si fanno più (limite raggiunto, schermo non leggibile), o null.</summary>
    public string DisabledReason { get { lock (_sync) return _disabledReason; } }

    /// <summary>Accende o spegne la foto ogni secondo. Restituisce false se le foto non si possono fare.</summary>
    public bool SetOn(bool on)
    {
        lock (_sync)
        {
            if (_stopped || (on && _disabledReason != null))
                return false;
            if (_on == on)
                return true;
            _on = on;
            if (on)
            {
                _lastFingerprint = null;   // la prima foto di un flusso si salva sempre
                if (_interval != Timeout.Infinite)
                {
                    _timer ??= new Timer(_ => Capture("ogni secondo", onlyIfChanged: true), null, Timeout.Infinite, Timeout.Infinite);
                    _timer.Change(0, _interval);
                }
            }
            else
            {
                _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
        _session.WriteEvent(_session.NewEvent("foto-schermo").With("accese", on));
        return true;
    }

    /// <summary>Una foto adesso (una versione nuova del file): se lo schermo non è cambiato, l'indice punta all'ultima.</summary>
    public void Shoot(string reason)
    {
        if (On)
            ThreadPool.QueueUserWorkItem(_ => Capture(reason, onlyIfChanged: false));
    }

    internal void Capture(string reason, bool onlyIfChanged)
    {
        lock (_sync)
        {
            if (_stopped || !_on || _disabledReason != null)
                return;

            ScreenShot shot;
            try
            {
                shot = _camera.Take();
            }
            catch (Exception)
            {
                shot = null;
            }
            if (shot == null)
            {
                if (++_failures >= FailuresBeforeGivingUp)
                    Disable("lo schermo non si legge (sessione bloccata o desktop remoto chiuso)");
                return;
            }
            _failures = 0;

            var changed = _lastFingerprint == null || Differs(_lastFingerprint, shot.Fingerprint);
            if (!changed)
            {
                if (!onlyIfChanged && _lastFile != null)
                    _session.WriteEvent(_session.NewEvent("foto").With("file", _lastFile).With("motivo", reason).With("schermoUguale", true));
                return;
            }

            var folder = Path.Combine(_session.SessionFolder, "foto");
            var name = $"{_count + 1:0000}_{DateTime.Now.ToString("HH-mm-ss.fff", CultureInfo.InvariantCulture)}.jpg";
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, name), shot.Jpeg);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Disable("non riesco a salvare le foto: " + ex.Message);
                return;
            }
            _count++;
            _bytes += shot.Jpeg.Length;
            _lastFingerprint = shot.Fingerprint;
            _lastFile = @"foto\" + name;
            _session.WriteEvent(_session.NewEvent("foto")
                .With("file", _lastFile).With("motivo", reason).With("byte", shot.Jpeg.Length)
                .With("larghezza", shot.Width).With("altezza", shot.Height));

            if (_count >= MaxPhotos || _bytes >= MaxBytes)
                Disable($"raggiunto il limite di {MaxPhotos} foto o {MaxBytes / 1024 / 1024} MB");
        }
    }

    void Disable(string reason)
    {
        _disabledReason = reason;
        _on = false;
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _log.Line("       Foto dello schermo spente: " + reason + ".", ConsoleColor.DarkYellow);
        _session.WriteEvent(_session.NewEvent("foto-schermo").With("accese", false).With("motivo", reason));
    }

    /// <summary>Basta una cella che cambia: un importo che passa da 10,00 a 20,00 occupa poco schermo.</summary>
    public static bool Differs(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
            return true;
        for (var i = 0; i < a.Length; i++)
        {
            if (Math.Abs(a[i] - b[i]) >= ChangeThreshold)
                return true;
        }
        return false;
    }

    public void Stop()
    {
        Timer timer;
        lock (_sync)
        {
            _stopped = true;
            _on = false;
            timer = _timer;
            _timer = null;
        }
        if (timer != null)
        {
            using var done = new ManualResetEvent(false);
            if (timer.Dispose(done))
                done.WaitOne(TimeSpan.FromSeconds(3));   // aspetta una foto che si sta salvando
        }
    }

    public void Dispose() => Stop();
}
