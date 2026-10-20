using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Bluetech.Display.Agent.Impostazioni;

namespace Bluetech.Display.Agent.Accesso;

/// <summary>
/// Il back office: una password (come hash in impostazioni.json) e le sessioni aperte, in memoria. Al riavvio
/// dell'Agent si rientra con la password. Pochi tentativi sbagliati di fila fermano quell'indirizzo per un minuto.
/// </summary>
public sealed class AdminAccess(SettingsStore settings)
{
    public const string Cookie = "bluetech_bo";
    const int Iterations = 100_000;
    static readonly TimeSpan SessionIdle = TimeSpan.FromHours(12);
    const int FailuresBeforeLock = 5;
    static readonly TimeSpan LockFor = TimeSpan.FromMinutes(1);

    readonly ConcurrentDictionary<string, DateTimeOffset> _sessions = new();
    readonly ConcurrentDictionary<string, (int Failures, DateTimeOffset LockedUntil)> _failures = new();

    public bool PasswordSet => settings.Current.Password != null;

    public static PasswordHash Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return new PasswordHash
        {
            Iterazioni = Iterations,
            Sale = Convert.ToBase64String(salt),
            Hash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32)),
        };
    }

    public static bool Verify(PasswordHash stored, string password)
    {
        try
        {
            var expected = Convert.FromBase64String(stored.Hash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(stored.Sale), Math.Max(1, stored.Iterazioni), HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Una password accettabile: almeno 6 caratteri. Restituisce l'errore o null.</summary>
    public static string? CheckNew(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < 6 ? "La password deve avere almeno 6 caratteri."
        : password.Length > 200 ? "La password è troppo lunga."
        : null;

    /// <summary>La prima password si imposta solo dal PC dell'Agent: dalla rete nessuno può prendersi il back office.</summary>
    public static bool IsLocal(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        return remote == null || IPAddress.IsLoopback(remote) || (remote.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(remote.MapToIPv4()));
    }

    public bool IsAuthenticated(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(Cookie, out var token) || token.Length == 0)
            return false;
        var key = Sha256(token);
        if (!_sessions.TryGetValue(key, out var lastUse))
            return false;
        var now = DateTimeOffset.UtcNow;
        if (now - lastUse > SessionIdle || !PasswordSet)
        {
            _sessions.TryRemove(key, out _);
            return false;
        }
        _sessions[key] = now;
        return true;
    }

    /// <summary>Controlla la password e apre la sessione. Restituisce l'errore o null.</summary>
    public string? Login(HttpContext context, string? password)
    {
        var who = context.Connection.RemoteIpAddress?.ToString() ?? "?";
        var now = DateTimeOffset.UtcNow;
        if (_failures.TryGetValue(who, out var record) && record.LockedUntil > now)
            return "Troppi tentativi sbagliati: riprova tra un minuto.";
        if (settings.Current.Password is not { } stored)
            return "La password non è ancora impostata.";
        if (string.IsNullOrEmpty(password) || !Verify(stored, password))
        {
            var failures = record.Failures + 1;
            _failures[who] = failures >= FailuresBeforeLock ? (0, now + LockFor) : (failures, default);
            return "Password sbagliata.";
        }
        _failures.TryRemove(who, out _);
        StartSession(context);
        return null;
    }

    public void StartSession(HttpContext context)
    {
        var token = NewToken();
        _sessions[Sha256(token)] = DateTimeOffset.UtcNow;
        context.Response.Cookies.Append(Cookie, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            IsEssential = true,
            Path = "/",
        });
    }

    public void EndSession(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(Cookie, out var token))
            _sessions.TryRemove(Sha256(token), out _);
        context.Response.Cookies.Delete(Cookie, new CookieOptions { Path = "/" });
    }

    /// <summary>Dopo il cambio della password restano aperte solo le sessioni nuove.</summary>
    public void EndAllSessions() => _sessions.Clear();

    internal static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static string Sha256(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
