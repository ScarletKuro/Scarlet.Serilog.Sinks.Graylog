using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using Scarlet.Serilog.Sinks.Graylog.Tests.Fakes;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.Helpers
{
    /// <summary>
    /// Loading the client certificate a TLS transport presents.
    /// </summary>
    /// <remarks>
    /// Every failure has to name the file and say what to check: this runs at the first send, long
    /// after configuration, and the exception is all the operator gets.
    /// </remarks>
    public class TlsCertificateLoaderFixture
    {
        [Fact]
        public void ResolveClientCertificate_WithoutAPath_Throws()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => TlsCertificateLoader.ResolveClientCertificate(new TlsOptions()));

            Assert.Contains("client certificate path is required", exception.Message);
        }

        [Fact]
        public void ResolveClientCertificate_WhenTheFileDoesNotExist_Throws()
        {
            string path = Path.Combine(Path.GetTempPath(), $"scarlet-graylog-missing-{Guid.NewGuid():N}.pfx");

            FileNotFoundException exception = Assert.Throws<FileNotFoundException>(
                () => TlsCertificateLoader.ResolveClientCertificate(new TlsOptions { ClientCertificatePath = path }));

            Assert.Equal(path, exception.FileName);
        }

        [Fact]
        public void ResolveClientCertificate_WhenTheFileIsNotACertificate_ThrowsWithTheUnderlyingCause()
        {
            using TestCertificates.PfxFile file = TestCertificates.WriteUnreadableFile();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => TlsCertificateLoader.ResolveClientCertificate(new TlsOptions { ClientCertificatePath = file.Path }));

            Assert.Contains(file.Path, exception.Message);
            Assert.IsAssignableFrom<CryptographicException>(exception.InnerException);
        }

        [Fact]
        public void ResolveClientCertificate_WhenThePasswordIsWrong_ThrowsWithTheUnderlyingCause()
        {
            using TestCertificates.PfxFile file = TestCertificates.WritePfx("correct");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => TlsCertificateLoader.ResolveClientCertificate(new TlsOptions
                {
                    ClientCertificatePath = file.Path,
                    ClientCertificatePassword = "wrong"
                }));

            Assert.Contains("password is correct", exception.Message);
            Assert.IsAssignableFrom<CryptographicException>(exception.InnerException);
        }

        [Fact]
        public void ResolveClientCertificate_WithAPasswordProtectedFile_LoadsTheCertificate()
        {
            using TestCertificates.PfxFile file = TestCertificates.WritePfx("secret");

            (X509Certificate2 certificate, bool owned) = TlsCertificateLoader.ResolveClientCertificate(new TlsOptions
            {
                ClientCertificatePath = file.Path,
                ClientCertificatePassword = "secret"
            });

            using (certificate)
            {
                Assert.Equal(file.Thumbprint, certificate.Thumbprint);
                Assert.Equal(file.Subject, certificate.Subject);
                Assert.True(owned);
            }
        }

        [Fact]
        public void ResolveClientCertificate_WithoutAPassword_LoadsTheCertificate()
        {
            using TestCertificates.PfxFile file = TestCertificates.WritePfx();

            (X509Certificate2 certificate, bool owned) = TlsCertificateLoader.ResolveClientCertificate(new TlsOptions
            {
                ClientCertificatePath = file.Path
            });

            using (certificate)
            {
                Assert.Equal(file.Thumbprint, certificate.Thumbprint);
                Assert.True(owned);
            }
        }

        /// <summary>
        /// A certificate supplied in memory is handed back as-is, and stays the caller's to dispose.
        /// </summary>
        [Fact]
        public void ResolveClientCertificate_WithAnInMemoryCertificate_ReturnsItUnownedWithoutTouchingDisk()
        {
            using X509Certificate2 expected = TestCertificates.CreateSelfSigned();

            (X509Certificate2 certificate, bool owned) = TlsCertificateLoader.ResolveClientCertificate(new TlsOptions
            {
                ClientCertificate = expected
            });

            Assert.Same(expected, certificate);
            Assert.False(owned);
        }

        /// <summary>
        /// An in-memory certificate wins over a path, which is why the two cannot be configured
        /// together - see <c>GraylogSinkOptionsValidator</c>.
        /// </summary>
        [Fact]
        public void ResolveClientCertificate_WithAnInMemoryCertificateAndAMissingPath_DoesNotLookForTheFile()
        {
            using X509Certificate2 expected = TestCertificates.CreateSelfSigned();

            (X509Certificate2 certificate, bool owned) = TlsCertificateLoader.ResolveClientCertificate(new TlsOptions
            {
                ClientCertificate = expected,
                ClientCertificatePath = "certificate-that-does-not-exist.pfx"
            });

            Assert.Same(expected, certificate);
            Assert.False(owned);
        }

        [Fact]
        public void HasClientCertificate_ReportsWhetherEitherRouteIsConfigured()
        {
            using X509Certificate2 certificate = TestCertificates.CreateSelfSigned();

            Assert.False(TlsCertificateLoader.HasClientCertificate(null));
            Assert.False(TlsCertificateLoader.HasClientCertificate(new TlsOptions()));
            Assert.False(TlsCertificateLoader.HasClientCertificate(new TlsOptions { ClientCertificatePath = "   " }));
            Assert.True(TlsCertificateLoader.HasClientCertificate(new TlsOptions { ClientCertificatePath = "client.pfx" }));
            Assert.True(TlsCertificateLoader.HasClientCertificate(new TlsOptions { ClientCertificate = certificate }));
        }
    }
}
