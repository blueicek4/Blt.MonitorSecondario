using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Bluetech.Display.Agent.Impostazioni;

namespace Bluetech.Display.Agent.Server;

/// <summary>
/// Il certificato della porta HTTPS: un .pfx indicato nelle impostazioni, oppure uno generato dall'Agent. Quello
/// generato è firmato da una piccola autorità dell'Agent (dati\https\autorita.pfx): installata una volta l'autorità
/// sui dispositivi (bluetech-agent-ca.cer, dal back office), il browser si fida anche dei certificati rifatti dopo,
/// per esempio quando cambia l'indirizzo IP del PC.
/// </summary>
public static class HttpsCertificate
{
    public const string AuthorityFile = "bluetech-agent-ca.cer";
    static readonly TimeSpan ServerValidity = TimeSpan.FromDays(730);   // iOS accetta al massimo 825 giorni

    public sealed record Loaded(X509Certificate2 Certificate, string? AuthorityPath);

    /// <summary>Il certificato per Kestrel, o null con il motivo in <paramref name="problem"/>.</summary>
    public static Loaded? Load(HttpsSettings https, string dataFolder, out string? problem)
    {
        problem = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(https.Certificato))
            {
                var path = Path.IsPathFullyQualified(https.Certificato) ? https.Certificato : Path.Combine(AppContext.BaseDirectory, https.Certificato);
                var given = X509CertificateLoader.LoadPkcs12FromFile(path, string.IsNullOrEmpty(https.PasswordCertificato) ? null : https.PasswordCertificato);
                if (!given.HasPrivateKey)
                {
                    problem = $"Il certificato {path} non ha la chiave privata: serve il .pfx completo.";
                    return null;
                }
                return new Loaded(given, null);
            }
            return Generated(Path.Combine(dataFolder, "https"));
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            problem = $"HTTPS non attivo: {ex.Message}";
            return null;
        }
    }

    static Loaded Generated(string folder)
    {
        Directory.CreateDirectory(folder);
        var authorityPfx = Path.Combine(folder, "autorita.pfx");
        X509Certificate2 authority;
        if (File.Exists(authorityPfx))
        {
            authority = X509CertificateLoader.LoadPkcs12FromFile(authorityPfx, null, X509KeyStorageFlags.Exportable);
        }
        else
        {
            authority = CreateAuthority();
            File.WriteAllBytes(authorityPfx, authority.Export(X509ContentType.Pkcs12));
        }
        using (authority)
        {
            var authorityCer = Path.Combine(folder, AuthorityFile);
            File.WriteAllBytes(authorityCer, authority.Export(X509ContentType.Cert));

            var serverPfx = Path.Combine(folder, "server.pfx");
            var (dns, ips) = CurrentNames();
            if (File.Exists(serverPfx))
            {
                var existing = X509CertificateLoader.LoadPkcs12FromFile(serverPfx, null);
                if (existing.NotAfter > DateTime.Now.AddDays(30) && Covers(existing, dns, ips) && SignedBy(existing, authority))
                    return new Loaded(existing, authorityCer);
                existing.Dispose();
            }
            using (var created = CreateServer(authority, dns, ips))
                File.WriteAllBytes(serverPfx, created.Export(X509ContentType.Pkcs12));
            // Riletto dal file: su Windows la chiave di un certificato appena creato resta in memoria e SChannel non la usa.
            return new Loaded(X509CertificateLoader.LoadPkcs12FromFile(serverPfx, null), authorityCer);
        }
    }

    static X509Certificate2 CreateAuthority()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN=Bluetech Display Agent CA ({Environment.MachineName}), O=Bluetech", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(10));
    }

    static X509Certificate2 CreateServer(X509Certificate2 authority, List<string> dns, List<IPAddress> ips)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={Environment.MachineName}, O=Bluetech Display Agent", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));   // server
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(authority, true, false));
        var names = new SubjectAlternativeNameBuilder();
        foreach (var name in dns)
            names.AddDnsName(name);
        foreach (var ip in ips)
            names.AddIpAddress(ip);
        request.CertificateExtensions.Add(names.Build());
        var notAfter = DateTimeOffset.Now + ServerValidity;
        if (notAfter > authority.NotAfter)
            notAfter = authority.NotAfter;
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        using var unsigned = request.Create(authority, DateTimeOffset.Now.AddDays(-1), notAfter, serial);
        return unsigned.CopyWithPrivateKey(key);
    }

    /// <summary>I nomi con cui i dispositivi raggiungono il PC: localhost, il nome del PC e i suoi indirizzi.</summary>
    internal static (List<string> Dns, List<IPAddress> Ips) CurrentNames()
    {
        var dns = new List<string> { "localhost", Environment.MachineName.ToLowerInvariant() };
        var ips = new List<IPAddress> { IPAddress.Loopback, IPAddress.IPv6Loopback };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                foreach (var address in nic.GetIPProperties().UnicastAddresses)
                {
                    var ip = address.Address;
                    if (ip.AddressFamily == AddressFamily.InterNetwork || (ip.AddressFamily == AddressFamily.InterNetworkV6 && !ip.IsIPv6LinkLocal))
                        if (!ips.Contains(ip))
                            ips.Add(ip);
                }
            }
        }
        catch (NetworkInformationException)
        {
        }
        return (dns, ips);
    }

    static bool Covers(X509Certificate2 certificate, List<string> dns, List<IPAddress> ips)
    {
        var san = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
        if (san == null)
            return false;
        var names = san.EnumerateDnsNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var addresses = san.EnumerateIPAddresses().ToHashSet();
        return dns.All(names.Contains) && ips.All(addresses.Contains);
    }

    static bool SignedBy(X509Certificate2 certificate, X509Certificate2 authority)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(authority);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(certificate);
    }
}
