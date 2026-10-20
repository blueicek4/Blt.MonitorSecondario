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
    readonly Dictionary<string, (int Failures, DateTimeOffset LockedUntil)> _failures = [];

    public bool PasswordSet => settings.Current.Password != null;

    public const string OnlyFromThisPc = "Il back office si apre dal PC dove gira l'Agent (anche in desktop remoto). " +
        "Per aprirlo anche dagli altri PC e dai telefoni: Opzioni, \"Back office anche dalla rete\", da quel PC.";

    /// <summary>Da qui si può entrare? Dal PC dell'Agent sempre; dalla rete solo con l'opzione accesa.</summary>
    public bool Reachable(HttpContext context) => IsLocal(context) || settings.Current.BackOfficeDallaRete;

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

    /// <summary>
    /// Il nome del cookie: su HTTPS con "_s" in fondo. Un cookie Secure non viaggia su HTTP e il browser non lo lascia
    /// sostituire da uno non Secure con lo stesso nome: con due nomi, chi passa da HTTPS a HTTP (o il contrario) non resta
    /// chiuso fuori.
    /// </summary>
    internal static string CookieName(string name, HttpContext context) => context.Request.IsHttps ? name + "_s" : name;

    /// <summary>I valori del cookie, prima quello HTTPS e poi l'altro (su HTTPS arrivano tutti e due).</summary>
    internal static IEnumerable<string> CookieValues(HttpContext context, string name)
    {
        if (context.Request.Cookies.TryGetValue(name + "_s", out var secure) && secure.Length > 0)
            yield return secure;
        if (context.Request.Cookies.TryGetValue(name, out var plain) && plain.Length > 0)
            yield return plain;
    }

    public bool IsAuthenticated(HttpContext context)
    {
        if (!PasswordSet || !Reachable(context))   // una sessione aperta dalla rete finisce quando l'opzione si spegne
            return false;
        var now = DateTimeOffset.UtcNow;
        foreach (var token in CookieValues(context, Cookie))
        {
            var key = Sha256(token);
            if (!_sessions.TryGetValue(key, out var lastUse))
                continue;
            if (now - lastUse > SessionIdle)
            {
                _sessions.TryRemove(key, out _);
                continue;
            }
            _sessions[key] = now;
            return true;
        }
        return false;
    }

    /// <summary>Controlla la password e apre la sessione. Restituisce l'errore o null.</summary>
    public string? Login(HttpContext context, string? password)
    {
        var who = context.Connection.RemoteIpAddress?.ToString() ?? "?";
        var now = DateTimeOffset.UtcNow;
        if (!Reachable(context))
            return OnlyFromThisPc;
        if (settings.Current.Password is not { } stored)
            return "La password non è ancora impostata.";
        // Tentativi in parallelo dallo stesso indirizzo: ognuno prende il suo posto nel conto prima di controllare la
        // password, così nessuno passa oltre il quinto.
        lock (_failures)
        {
            _failures.TryGetValue(who, out var record);
            if (record.LockedUntil > now)
                return "Troppi tentativi sbagliati: riprova tra un minuto.";
            var failures = record.Failures + 1;
            _failures[who] = failures >= FailuresBeforeLock ? (0, now + LockFor) : (failures, default);
        }
        if (string.IsNullOrEmpty(password) || !Verify(stored, password))
            return "Password sbagliata.";
        lock (_failures)
            _failures.Remove(who);
        StartSession(context);
        return null;
    }

    public void StartSession(HttpContext context)
    {
        var token = NewToken();
        _sessions[Sha256(token)] = DateTimeOffset.UtcNow;
        context.Response.Cookies.Append(CookieName(Cookie, context), token, new CookieOptions
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
        foreach (var token in CookieValues(context, Cookie))
            _sessions.TryRemove(Sha256(token), out _);
        context.Response.Cookies.Delete(CookieName(Cookie, context), new CookieOptions { Path = "/" });
    }

    /// <summary>Dopo il cambio della password restano aperte solo le sessioni nuove.</summary>
    public void EndAllSessions() => _sessions.Clear();

    internal static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static string Sha256(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
