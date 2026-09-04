using Scarlet.Serilog.Sinks.Graylog.Tests.Fakes;
using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests
{
    /// <summary>
    /// Finds the Graylog the integration tests run against and creates the GELF inputs they send to,
    /// once for the whole class rather than per test.
    /// </summary>
    /// <remarks>
    /// A machine without that server is the normal case - the default test run filters these tests out
    /// entirely - so discovery failing is not an error here. <see cref="RequireServer"/> skips instead,
    /// which keeps the tests runnable locally without a checklist: start the compose file and they run,
    /// don't and they report why.
    /// </remarks>
    public sealed class GraylogServerFixture : IAsyncLifetime
    {
        private GraylogServer? _server;
        private string? _unavailable;

        /// <summary>
        /// The thumbprint of the certificate the TLS input presents, for a client to pin to. The
        /// platform does not trust a self-signed certificate, and a client that accepted any
        /// certificate would pass against the wrong server just as happily.
        /// </summary>
        internal string? TlsThumbprint { get; private set; }

        /// <summary>
        /// Generates the certificate the TLS input serves, writes it where the compose file mounts it,
        /// and creates the input that reads it.
        /// </summary>
        /// <remarks>
        /// The paths differ across the bind mount: the test writes to <c>tests/integration/certs</c> on
        /// the host, Graylog reads <c>/etc/graylog/certs</c> inside the container.
        /// </remarks>
        private static async Task<string> EnsureTlsInput(GraylogServer server)
        {
            using X509Certificate2 certificate = TestCertificates.CreateSelfSigned();
            TestCertificates.PemPair pem = TestCertificates.WritePem(certificate, HostCertificateDirectory());

            await server.EnsureInput(
                GraylogServer.TcpInputType,
                12204,
                configuration =>
                {
                    configuration["tls_enable"] = true;
                    configuration["tls_cert_file"] = "/etc/graylog/certs/" + Path.GetFileName(pem.CertificatePath);
                    configuration["tls_key_file"] = "/etc/graylog/certs/" + Path.GetFileName(pem.KeyPath);
                    configuration["tls_key_password"] = string.Empty;
                },
                CancellationToken.None).ConfigureAwait(false);

            return pem.Thumbprint;
        }

        /// <summary>
        /// Finds <c>tests/integration/certs</c> by walking up from the test binary, which is the only
        /// fixed relationship between the two - the working directory is not one.
        /// </summary>
        private static string HostCertificateDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "tests", "integration", "certs");

                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("tests/integration/certs is not above the test binary.");
        }

        public async ValueTask InitializeAsync()
        {
            try
            {
                _server = await GraylogServer.TryDiscover(TimeSpan.FromSeconds(20), CancellationToken.None)
                                             .ConfigureAwait(false);

                if (_server == null)
                {
                    _unavailable = "No Graylog answered. Start tests/integration/docker-compose.yml, or point GRAYLOG_API_URI at one.";

                    return;
                }

                await _server.EnsureInput(GraylogServer.UdpInputType, 12201, CancellationToken.None).ConfigureAwait(false);
                await _server.EnsureInput(GraylogServer.TcpInputType, 12202, CancellationToken.None).ConfigureAwait(false);
                await _server.EnsureInput(GraylogServer.HttpInputType, 12203, CancellationToken.None).ConfigureAwait(false);

                TlsThumbprint = await EnsureTlsInput(_server).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // A server that is up but cannot be set up is worth reporting in full: the message
                // carries Graylog's own answer, which says which input configuration it refused.
                _server?.Dispose();
                _server = null;
                _unavailable = $"Graylog could not be prepared: {exception.Message}";
            }
        }

        /// <summary>
        /// The server, or a skipped test when there is none - unless <c>GRAYLOG_REQUIRED</c> says a
        /// server was supposed to be there, in which case the test fails.
        /// </summary>
        /// <remarks>
        /// CI sets that variable, because a skip there is a false green: the job starts the compose
        /// file and waits on its healthchecks, so nothing answering afterwards means the integration
        /// tests silently tested nothing.
        /// </remarks>
        internal GraylogServer RequireServer()
        {
            if (_server != null)
            {
                return _server;
            }

            string reason = _unavailable ?? "No Graylog is available.";

            if (string.Equals(Environment.GetEnvironmentVariable("GRAYLOG_REQUIRED"), "true", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Fail(reason);
            }

            Assert.Skip(reason);

            return _server!;
        }

        public ValueTask DisposeAsync()
        {
            _server?.Dispose();
            _server = null;

            return default;
        }
    }
}
