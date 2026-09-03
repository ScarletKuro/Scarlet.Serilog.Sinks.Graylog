using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Fakes
{
    /// <summary>
    /// Builds the self-signed certificates the TLS tests need, in memory and on disk.
    /// </summary>
    /// <remarks>
    /// Nothing here touches a certificate store, so the tests leave the machine as they found it.
    /// </remarks>
    internal static class TestCertificates
    {
        private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
        private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";
        private const string RoundTripPassword = "scarlet-graylog-tests";

        /// <summary>
        /// Creates a self-signed certificate valid for both server and client authentication on the
        /// loopback interface.
        /// </summary>
        public static X509Certificate2 CreateSelfSigned(string commonName = "localhost")
        {
            using RSA key = RSA.Create(2048);
            var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid(ServerAuthenticationOid), new Oid(ClientAuthenticationOid) }, false));

            var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
            subjectAlternativeNames.AddDnsName("localhost");
            subjectAlternativeNames.AddIpAddress(IPAddress.Loopback);
            subjectAlternativeNames.AddIpAddress(IPAddress.IPv6Loopback);
            request.CertificateExtensions.Add(subjectAlternativeNames.Build());

            using X509Certificate2 generated = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(1));

            // CreateSelfSigned leaves the certificate holding an ephemeral key, which SslStream cannot
            // use for server authentication on Linux - and CI runs on ubuntu. A round trip through
            // PKCS#12 replaces it with one SslStream accepts.
            return LoadPkcs12(generated.Export(X509ContentType.Pfx, RoundTripPassword), RoundTripPassword);
        }

        /// <summary>
        /// Creates a self-signed certificate stripped of its private key, which is useless for client
        /// authentication.
        /// </summary>
        public static X509Certificate2 CreateWithoutPrivateKey(string commonName = "localhost")
        {
            using X509Certificate2 certificate = CreateSelfSigned(commonName);

#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
#else
            return new X509Certificate2(certificate.Export(X509ContentType.Cert));
#endif
        }

        /// <summary>
        /// Writes a new self-signed certificate to a temporary PFX file.
        /// </summary>
        public static PfxFile WritePfx(string? password = null)
        {
            using X509Certificate2 certificate = CreateSelfSigned();

            return WritePfx(certificate, password);
        }

        /// <summary>
        /// Writes <paramref name="certificate"/> to a temporary PFX file.
        /// </summary>
        public static PfxFile WritePfx(X509Certificate2 certificate, string? password)
        {
            string path = NewTemporaryPath();
            File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));

            return new PfxFile(path, password, certificate.Subject, certificate.Thumbprint);
        }

        /// <summary>
        /// Writes a file that is not a certificate at all, for the load-failure path.
        /// </summary>
        public static PfxFile WriteUnreadableFile()
        {
            string path = NewTemporaryPath();
            File.WriteAllBytes(path, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 });

            return new PfxFile(path, password: null, subject: string.Empty, thumbprint: string.Empty);
        }

        public static X509Certificate2 LoadPkcs12(byte[] data, string? password)
        {
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12(data, password, X509KeyStorageFlags.Exportable);
#else
            return new X509Certificate2(data, password, X509KeyStorageFlags.Exportable);
#endif
        }

        private static string NewTemporaryPath()
        {
            return Path.Combine(Path.GetTempPath(), $"scarlet-graylog-{Guid.NewGuid():N}.pfx");
        }

        /// <summary>
        /// A PFX file that deletes itself with the test.
        /// </summary>
        internal sealed class PfxFile : IDisposable
        {
            public PfxFile(string path, string? password, string subject, string thumbprint)
            {
                Path = path;
                Password = password;
                Subject = subject;
                Thumbprint = thumbprint;
            }

            public string Path { get; }

            public string? Password { get; }

            public string Subject { get; }

            public string Thumbprint { get; }

            public void Dispose()
            {
                File.Delete(Path);
            }
        }
    }
}
