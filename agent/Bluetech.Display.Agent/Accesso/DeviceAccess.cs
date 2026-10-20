using System.Security.Cryptography;
using System.Text;
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

    /// <summary>Chrome, Edge e WebView2 non tengono un cookie oltre 400 giorni: si rinnova a ogni visita.</summary>
    static readonly TimeSpan CookieLife = TimeSpan.FromDays(400);

    /// <summary>Codici in attesa al massimo, in tutto e per indirizzo: chi chiede codici a raffica scalza solo i suoi.</summary>
    const int MaxPending = 50;
    const int MaxPendingPerRemote = 3;

    sealed class Pending
    {
        public required string Code;
        public required string SecretHash;
        public required DateTimeOffset Created;
        public required string Remote;
        /// <summary>Il token del dispositivo appena autorizzato, finché il dispositivo non viene a prenderlo.</summary>
        public string? Token;
        public DateTimeOffset ApprovedAt;

        public bool Expired(DateTimeOffset now) => Token == null ? now - Created > PairingLife : now - ApprovedAt > PairingLife;
    }

    readonly object _lock = new();
    readonly List<Pending> _pending = [];

    /// <summary>
    /// Il dispositivo di questa richiesta, dal cookie; null se non è autorizzato. Su HTTPS il cookie ha un altro nome
    /// (vedi <see cref="AdminAccess.CookieName"/>): vale quello dei due che è ancora autorizzato.
    /// </summary>
    public DeviceConfig? Find(HttpContext context)
    {
        foreach (var token in AdminAccess.CookieValues(context, Cookie))
        {
            var hash = Encoding.ASCII.GetBytes(AdminAccess.Sha256(token));
            var device = settings.Current.Dispositivi.Find(d => d.Token is { Length: 64 } stored
                && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(stored), hash));
            if (device != null)
                return device;
        }
        return null;
    }

    /// <summary>Rinnova la scadenza del cookie di un dispositivo autorizzato (pagina aperta, controllo periodico).</summary>
    public void Refresh(HttpContext context)
    {
        foreach (var token in AdminAccess.CookieValues(context, Cookie))
        {
            var hash = AdminAccess.Sha256(token);
            if (settings.Current.Dispositivi.Exists(d => d.Token == hash))
            {
                SetDeviceCookie(context, token);
                return;
            }
        }
    }

    static void SetDeviceCookie(HttpContext context, string token) =>
        context.Response.Cookies.Append(AdminAccess.CookieName(Cookie, context), token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,   // anche aprendo il link da un'altra app
            Secure = context.Request.IsHttps,
            IsEssential = true,
            Path = "/",
            Expires = DateTimeOffset.UtcNow + CookieLife,
        });

    /// <summary>I dispositivi che stanno mostrando un codice: da dove e da quanto (il codice no).</summary>
    public List<(string Remote, TimeSpan Age)> Waiting()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_lock)
            return _pending.Where(p => p.Token == null && !p.Expired(now)).Select(p => (p.Remote, now - p.Created)).ToList();
    }

    /// <summary>
    /// Il codice da mostrare su /auth. Lo stesso browser ricaricando la pagina rivede lo stesso codice finché non scade.
    /// </summary>
    public (string Code, TimeSpan ExpiresIn) StartPairing(HttpContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var remote = context.Connection.RemoteIpAddress?.ToString() ?? "?";
        lock (_lock)
        {
            _pending.RemoveAll(p => p.Expired(now));
            if (Mine(context) is { Token: null } existing)
                return (existing.Code, PairingLife - (now - existing.Created));

            // Si scalzano solo codici non ancora autorizzati: un'autorizzazione già data non si perde.
            var waiting = _pending.Where(p => p.Token == null).ToList();
            if (waiting.Count(p => p.Remote == remote) >= MaxPendingPerRemote)
                _pending.Remove(waiting.First(p => p.Remote == remote));
            else if (waiting.Count >= MaxPending)
                _pending.Remove(waiting[0]);

            string code;
            do
                code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            while (_pending.Any(p => p.Code == code));
            var secret = AdminAccess.NewToken();
            _pending.Add(new Pending
            {
                Code = code,
                SecretHash = AdminAccess.Sha256(secret),
                Created = now,
                Remote = remote,
            });
            context.Response.Cookies.Append(AdminAccess.CookieName(PairingCookie, context), secret, new CookieOptions
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
            if (Mine(context) is not { } pending || pending.Expired(now))
                return "scaduto";
            if (pending.Token == null)
                return "attesa";
            _pending.Remove(pending);
            SetDeviceCookie(context, pending.Token);
            context.Response.Cookies.Delete(AdminAccess.CookieName(PairingCookie, context), new CookieOptions { Path = "/" });
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
            var pending = _pending.Find(p => p.Code == digits && p.Token == null && !p.Expired(now));
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
            pending.ApprovedAt = now;
            device = created;
            return null;
        }
    }

    Pending? Mine(HttpContext context)
    {
        foreach (var secret in AdminAccess.CookieValues(context, PairingCookie))
        {
            var hash = AdminAccess.Sha256(secret);
            if (_pending.Find(p => p.SecretHash == hash) is { } pending)
                return pending;
        }
        return null;
    }
}
