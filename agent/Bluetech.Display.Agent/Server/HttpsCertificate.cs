using System.Formats.Asn1;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Bluetech.Display.Agent.Impostazioni;

namespace Bluetech.Display.Agent.Server;

/// <summary>
/// Il certificato della porta HTTPS: un .pfx indicato nelle impostazioni, oppure uno generato dall'Agent. Quello
/// generato è firmato da una piccola autorità dell'Agent (dati\https\autorita.pfx): installata una volta l'autorità
/// sui dispositivi (bluetech-agent-ca.cer, dal back office), il browser si fida anche dei certificati rifatti dopo,
/// per esempio quando cambia l'indirizzo IP del PC. L'autorità ha i vincoli di nome: vale solo per le reti private e
/// per i nomi di questo PC, così chi ne copiasse la chiave non potrebbe garantire nessun sito vero.
/// </summary>
public static class HttpsCertificate
{
    public const string AuthorityFile = "bluetech-agent-ca.cer";
    static readonly TimeSpan ServerValidity = TimeSpan.FromDays(730);   // iOS accetta al massimo 825 giorni

    public sealed record Loaded(X509Certificate2 Certificate, string? AuthorityPath);

    /// <summary>Le reti dove l'autorità vale: private, loopback e link-local, IPv4 e IPv6.</summary>
    static readonly (IPAddress Network, int Prefix)[] PrivateNetworks =
    [
        (IPAddress.Parse("10.0.0.0"), 8),
        (IPAddress.Parse("172.16.0.0"), 12),
        (IPAddress.Parse("192.168.0.0"), 16),
        (IPAddress.Parse("127.0.0.0"), 8),
        (IPAddress.Parse("169.254.0.0"), 16),
        (IPAddress.IPv6Loopback, 128),
        (IPAddress.Parse("fc00::"), 7),
        (IPAddress.Parse("fe80::"), 10),
    ];

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
        var permittedFile = Path.Combine(folder, "autorita-nomi.json");
        var (dns, ips) = CurrentNames();
        X509Certificate2 authority;
        List<string>? permitted = null;   // null: un'autorità senza vincoli, di una versione precedente
        if (File.Exists(authorityPfx))
        {
            authority = X509CertificateLoader.LoadPkcs12FromFile(authorityPfx, null, X509KeyStorageFlags.Exportable);
            if (File.Exists(permittedFile))
                permitted = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(permittedFile));
        }
        else
        {
            permitted = [.. dns, "local"];
            authority = CreateAuthority(permitted);
            File.WriteAllBytes(authorityPfx, authority.Export(X509ContentType.Pkcs12));
            File.WriteAllText(permittedFile, JsonSerializer.Serialize(permitted));
        }
        // Nel certificato del server solo i nomi che l'autorità copre (il PC rinominato dopo resta raggiungibile per IP).
        if (permitted != null)
            dns = dns.Where(name => permitted.Exists(p => name == p || name.EndsWith("." + p, StringComparison.Ordinal))).ToList();
        using (authority)
        {
            var authorityCer = Path.Combine(folder, AuthorityFile);
            File.WriteAllBytes(authorityCer, authority.Export(X509ContentType.Cert));

            var serverPfx = Path.Combine(folder, "server.pfx");
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

    static X509Certificate2 CreateAuthority(List<string> dnsNames)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN=Bluetech Display Agent CA ({Environment.MachineName}), O=Bluetech", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(NameConstraints(dnsNames));
        return request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(10));
    }

    /// <summary>RFC 5280 §4.2.1.10: l'autorità vale solo per questi nomi (e i loro sottonomi) e per le reti private.</summary>
    internal static X509Extension NameConstraints(IEnumerable<string> dnsNames)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))   // permittedSubtrees
        {
            foreach (var name in dnsNames.Distinct())
            {
                using (writer.PushSequence())
                    writer.WriteCharacterString(UniversalTagNumber.IA5String, name, new Asn1Tag(TagClass.ContextSpecific, 2));
            }
            foreach (var (network, prefix) in PrivateNetworks)
            {
                var address = network.GetAddressBytes();
                var mask = new byte[address.Length];
                for (var bit = 0; bit < prefix; bit++)
                    mask[bit / 8] |= (byte)(0x80 >> (bit % 8));
                using (writer.PushSequence())
                    writer.WriteOctetString([.. address, .. mask], new Asn1Tag(TagClass.ContextSpecific, 7));
            }
        }
        return new X509Extension("2.5.29.30", writer.Encode(), critical: true);
    }

    static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        var bytes = ip.GetAddressBytes();
        foreach (var (network, prefix) in PrivateNetworks)
        {
            var net = network.GetAddressBytes();
            if (net.Length != bytes.Length)
                continue;
            var inside = true;
            for (var bit = 0; bit < prefix && inside; bit++)
            {
                var mask = (byte)(0x80 >> (bit % 8));
                inside = (bytes[bit / 8] & mask) == (net[bit / 8] & mask);
            }
            if (inside)
                return true;
        }
        return false;
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
    /// <summary>
    /// I nomi con cui i dispositivi raggiungono il PC: localhost, il nome completo del PC (quello di Windows è troncato a 15
    /// caratteri) anche con .local e con il dominio, e gli indirizzi delle reti private.
    /// </summary>
    internal static (List<string> Dns, List<IPAddress> Ips) CurrentNames()
    {
        var dns = new List<string> { "localhost" };
        void Add(string? name)
        {
            name = name?.Trim().TrimEnd('.').ToLowerInvariant();
            if (!string.IsNullOrEmpty(name) && !dns.Contains(name) && Uri.CheckHostName(name) == UriHostNameType.Dns)
                dns.Add(name);
        }
        string host = Environment.MachineName;
        try
        {
            host = Dns.GetHostName();
        }
        catch (SocketException)
        {
        }
        Add(host);
        Add(Environment.MachineName);
        Add(host + ".local");
        try
        {
            var domain = IPGlobalProperties.GetIPGlobalProperties().DomainName;
            if (!string.IsNullOrWhiteSpace(domain))
                Add(host + "." + domain);
        }
        catch (NetworkInformationException)
        {
        }
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
                    if ((ip.AddressFamily == AddressFamily.InterNetwork || (ip.AddressFamily == AddressFamily.InterNetworkV6 && !ip.IsIPv6LinkLocal))
                        && IsPrivate(ip) && !ips.Contains(ip))
                        ips.Add(ip);   // un indirizzo pubblico non va: l'autorità non lo copre
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
