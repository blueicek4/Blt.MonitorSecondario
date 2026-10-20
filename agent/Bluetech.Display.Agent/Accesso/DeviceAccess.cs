using System.Security.Cryptography;
using Bluetech.Display.Agent.Impostazioni;

namespace Bluetech.Display.Agent.Accesso;

/// <summary>
/// I display autorizzati. Un dispositivo nuovo apre /auth e mostra un codice; chi è nel back office lo scrive, sceglie
/// nome, cassa e stampante, e il dispositivo riceve il suo token in un cookie. Nelle impostazioni resta solo l'hash
/// del token: chi legge il file non può fingersi un dispositivo.
/// </summary>
public sealed class DeviceAccess(SettingsStore settings)
{
    public const string Cookie = "bluetech_dispositivo";
    public const string PairingCookie = "bluetech_abbinamento";
    public static readonly TimeSpan PairingLife = TimeSpan.FromMinutes(10);
    const int MaxPending = 50;

    sealed class Pending
    {
        public required string Code;
        public required string SecretHash;
        public required DateTimeOffset Created;
        public required string Remote;
        /// <summary>Il token del dispositivo appena autorizzato, finché il dispositivo non viene a prenderlo.</summary>
        public string? Token;
    }

    readonly object _lock = new();
    readonly List<Pending> _pending = [];

    /// <summary>Il dispositivo di questa richiesta, dal cookie; null se non è autorizzato.</summary>
    public DeviceConfig? Find(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(Cookie, out var token) || token.Length == 0)
            return null;
        var hash = System.Text.Encoding.ASCII.GetBytes(AdminAccess.Sha256(token));
        return settings.Current.Dispositivi.Find(d => d.Token is { Length: 64 } stored
            && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(stored), hash));
    }

    /// <summary>I dispositivi che stanno mostrando un codice: da dove e da quanto (il codice no).</summary>
    public List<(string Remote, TimeSpan Age)> Waiting()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_lock)
            return _pending.Where(p => p.Token == null && now - p.Created <= PairingLife).Select(p => (p.Remote, now - p.Created)).ToList();
    }

    /// <summary>
    /// Il codice da mostrare su /auth. Lo stesso browser ricaricando la pagina rivede lo stesso codice finché non scade.
    /// </summary>
    public (string Code, TimeSpan ExpiresIn) StartPairing(HttpContext context)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_lock)
        {
            _pending.RemoveAll(p => now - p.Created > PairingLife);
            if (Mine(context) is { Token: null } existing)
                return (existing.Code, PairingLife - (now - existing.Created));

            string code;
            do
                code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            while (_pending.Any(p => p.Code == code));
            var secret = AdminAccess.NewToken();
            if (_pending.Count >= MaxPending)
                _pending.RemoveAt(0);
            _pending.Add(new Pending
            {
                Code = code,
                SecretHash = AdminAccess.Sha256(secret),
                Created = now,
                Remote = context.Connection.RemoteIpAddress?.ToString() ?? "?",
            });
            context.Response.Cookies.Append(PairingCookie, secret, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps,
                IsEssential = true,
                Path = "/",
                MaxAge = PairingLife,
            });
            return (code, PairingLife);
        }
    }

    /// <summary>
    /// Il dispositivo chiede com'è andata: "attesa", "autorizzato" (e allora riceve il suo cookie) o "scaduto".
    /// </summary>
    public string Poll(HttpContext context)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_lock)
        {
            if (Mine(context) is not { } pending || now - pending.Created > PairingLife)
                return "scaduto";
            if (pending.Token == null)
                return "attesa";
            _pending.Remove(pending);
            context.Response.Cookies.Append(Cookie, pending.Token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,   // anche aprendo il link da un'altra app
                Secure = context.Request.IsHttps,
                IsEssential = true,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddYears(5),
            });
            context.Response.Cookies.Delete(PairingCookie, new CookieOptions { Path = "/" });
            return "autorizzato";
        }
    }

    /// <summary>
    /// Dal back office: il codice letto sul dispositivo diventa un dispositivo autorizzato. Restituisce l'errore o null.
    /// </summary>
    public string? Approve(string? code, string? name, string? cashPoint, string? printer, out DeviceConfig? device)
    {
        device = null;
        var digits = new string((code ?? "").Where(char.IsAsciiDigit).ToArray());
        var now = DateTimeOffset.UtcNow;
        lock (_lock)
        {
            var pending = _pending.Find(p => p.Code == digits && p.Token == null && now - p.Created <= PairingLife);
            if (pending == null)
                return "Codice non valido o scaduto: controlla quello sul dispositivo (dura 10 minuti).";
            var token = AdminAccess.NewToken();
            DeviceConfig? created = null;
            var error = settings.Update(s =>
            {
                created = new DeviceConfig
                {
                    Id = AgentSettings.NextId("dispositivo", s.Dispositivi.Select(d => d.Id)),
                    Nome = (name ?? "").Trim(),
                    Token = AdminAccess.Sha256(token),
                    Cassa = cashPoint ?? "",
                    Stampante = string.IsNullOrEmpty(printer) ? null : printer,
                    AutorizzatoIl = DateTimeOffset.Now,
                };
                s.Dispositivi.Add(created);
                return null;
            });
            if (error != null)
                return error;
            pending.Token = token;
            device = created;
            return null;
        }
    }

    Pending? Mine(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(PairingCookie, out var secret) || secret.Length == 0)
            return null;
        var hash = AdminAccess.Sha256(secret);
        return _pending.Find(p => p.SecretHash == hash);
    }
}
