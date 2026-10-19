using System;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace Bluetech.Display.Player;

/// <summary>
/// Codice di accesso alla configurazione: salvato come hash PBKDF2-SHA256 con sale casuale, mai in chiaro.
/// Protegge dalle modifiche accidentali o non autorizzate dall'interfaccia; chi ha accesso ai file di Windows
/// può comunque cancellare la configurazione e ripartire dalla prima installazione.
/// </summary>
public static class PinProtector
{
    public const int MinLength = 4;
    public const int MaxLength = 64;

    const int Iterations = 100_000;
    const int SaltSize = 16;
    const int HashSize = 32;
    const int MaxFailures = 5;
    static readonly TimeSpan LockoutTime = TimeSpan.FromSeconds(60);

    static int _failures;
    static DateTime _lockedUntil = DateTime.MinValue;

    public static void SetPin(PlayerSettings settings, string pin)
    {
        var salt = new byte[SaltSize];
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(salt);

        settings.PinSalt = Convert.ToBase64String(salt);
        settings.PinHash = Convert.ToBase64String(Derive(pin, salt, Iterations));
        settings.PinIterations = Iterations;
    }

    public static bool IsValidNewPin(string pin) =>
        pin != null && pin.Length >= MinLength && pin.Length <= MaxLength;

    public static TimeSpan LockoutRemaining =>
        _lockedUntil > DateTime.UtcNow ? _lockedUntil - DateTime.UtcNow : TimeSpan.Zero;

    /// <summary>Verifica il codice applicando il blocco temporaneo dopo troppi tentativi errati.</summary>
    public static bool Verify(PlayerSettings settings, string pin)
    {
        if (LockoutRemaining > TimeSpan.Zero || string.IsNullOrEmpty(pin))
            return false;

        bool ok;
        try
        {
            var salt = Convert.FromBase64String(settings.PinSalt);
            var expected = Convert.FromBase64String(settings.PinHash);
            ok = FixedTimeEquals(Derive(pin, salt, Math.Max(settings.PinIterations, 1)), expected);
        }
        catch (FormatException)
        {
            ok = false;
        }

        if (ok)
        {
            _failures = 0;
            return true;
        }

        if (++_failures >= MaxFailures)
        {
            _failures = 0;
            _lockedUntil = DateTime.UtcNow + LockoutTime;
            Log.Info("Troppi codici errati: configurazione bloccata per " + LockoutTime.TotalSeconds + " s");
        }
        return false;
    }

    /// <summary>Chiede il codice all'operatore. True se corretto.</summary>
    public static bool Prompt(string reason, PlayerSettings settings)
    {
        using var form = new PinForm(reason, settings);
        return form.ShowDialog() == DialogResult.OK;
    }

    static byte[] Derive(string pin, byte[] salt, int iterations)
    {
        using var kdf = new Rfc2898DeriveBytes(pin, salt, iterations, HashAlgorithmName.SHA256);
        return kdf.GetBytes(HashSize);
    }

    static bool FixedTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
            return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
