using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EduHelpdesk.Services;

// The certificate the helpdesk serves HTTPS with, for the fingerprint in a phone's pairing code (Pages/Phones). Most
// schools' certificates are self-signed or from their own authority, which Android doesn't trust; the phone trusts this
// one certificate instead, because the pairing code vouches for it. Found the way Kestrel finds it: by subject in a
// certificate store (what the installer writes, installer/Helpers.ps1 WriteAppSettings) or from a file. Null behind a
// proxy that does HTTPS itself, where the helpdesk never sees the certificate - the phone then trusts whatever Android
// trusts.
public static class ServerCertificate
{
    public static string? Fingerprint(IConfiguration configuration, IHostEnvironment environment)
    {
        foreach (var endpoint in configuration.GetSection("Kestrel:Endpoints").GetChildren())
            if (Load(endpoint.GetSection("Certificate")) is { } certificate) return Hash(certificate);
        if (Load(configuration.GetSection("Kestrel:Certificates:Default")) is { } byDefault) return Hash(byDefault);
        // dotnet run: the ASP.NET Core development certificate (dotnet dev-certs https).
        if (environment.IsDevelopment() && DevelopmentCertificate() is { } development) return Hash(development);
        return null;
    }

    // As the phone works it out from the certificate it is shown: SHA-256 of the certificate, lower-case hex.
    public static string Hash(X509Certificate2 certificate) => Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();

    // "ab:cd:…" in fours, for reading aloud or comparing by eye on the Phones page.
    public static string Readable(string fingerprint) =>
        string.Join(' ', Enumerable.Range(0, fingerprint.Length / 4).Select(i => fingerprint.Substring(i * 4, 4).ToUpperInvariant()));

    private static X509Certificate2? Load(IConfigurationSection section)
    {
        try
        {
            if (section["Path"] is { Length: > 0 } path && File.Exists(path))
                return path.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".p12", StringComparison.OrdinalIgnoreCase)
                    ? X509CertificateLoader.LoadPkcs12FromFile(path, section["Password"])
                    : X509CertificateLoader.LoadCertificateFromFile(path);
            if (section["Subject"] is { Length: > 0 } subject)
            {
                var location = Enum.TryParse<StoreLocation>(section["Location"], true, out var parsed) ? parsed : StoreLocation.CurrentUser;
                using var store = new X509Store(section["Store"] is { Length: > 0 } name ? name : "My", location);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                return store.Certificates.Find(X509FindType.FindBySubjectName, subject, validOnly: false)
                    .Where(x => x.HasPrivateKey && x.NotAfter > DateTime.Now)
                    .OrderByDescending(x => x.NotAfter)
                    .FirstOrDefault();
            }
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException) { }
        return null;
    }

    private static X509Certificate2? DevelopmentCertificate()
    {
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            // The extension dotnet dev-certs marks its certificate with.
            return store.Certificates.Where(x => x.Extensions.Any(e => e.Oid?.Value == "1.3.6.1.4.1.311.84.1.1") && x.NotAfter > DateTime.Now)
                .OrderByDescending(x => x.NotAfter).FirstOrDefault();
        }
        catch (CryptographicException) { return null; }
    }
}
