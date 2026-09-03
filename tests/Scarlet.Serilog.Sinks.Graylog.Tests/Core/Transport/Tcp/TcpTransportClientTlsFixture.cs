using NSubstitute;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Tcp;
using Scarlet.Serilog.Sinks.Graylog.Tests.Fakes;
using Serilog.Debugging;
using System;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.Transport.Tcp
{
    /// <summary>
    /// The TCP transport's TLS path, against a loopback server presenting a self-signed certificate.
    /// </summary>
    /// <remarks>
    /// A test can only complete a handshake with a certificate the platform does not trust by
    /// overriding <see cref="TcpTransportClient.ValidateServerCertificate"/> - which is the same thing
    /// a consumer does for a Graylog input with a self-signed certificate, so the override point is
    /// under test here as much as the TLS path is.
    /// </remarks>
    [Collection(SelfLogCollection.Name)]
    public class TcpTransportClientTlsFixture
    {
        [Fact]
        public async Task Send_OverTls_DeliversThePayload()
        {
            using X509Certificate2 certificate = TestCertificates.CreateSelfSigned();
            using var server = TlsLoopbackServer.Start(certificate);
            using var target = new TrustingTcpTransportClient(TlsOptionsFor(server.Port), Dns());
            using CancellationTokenSource timeout = Timeout();

            Task<TlsSession> accepting = server.AcceptAsync(timeout.Token);
            byte[] payload = { 1, 2, 3, 0 };

            await target.Send(payload);

            TlsSession session = await accepting;

            Assert.Equal(payload, await session.ReadExactlyAsync(payload.Length, timeout.Token));
            Assert.Equal(1, target.ValidationCalls);
            Assert.Equal(certificate.Thumbprint, target.ValidatedThumbprint);
            Assert.Null(session.ClientCertificate);
        }

        /// <summary>
        /// The certificate the server presented is written to SelfLog, so an operator can tell which
        /// certificate a connection actually accepted.
        /// </summary>
        [Fact]
        public async Task Send_OverTls_ReportsTheRemoteCertificateToSelfLog()
        {
            using X509Certificate2 certificate = TestCertificates.CreateSelfSigned("scarlet-graylog-tls-test");
            using var server = TlsLoopbackServer.Start(certificate);
            using var target = new TrustingTcpTransportClient(TlsOptionsFor(server.Port), Dns());
            using CancellationTokenSource timeout = Timeout();

            int reported = 0;

            // SelfLog is global and other classes run in parallel, so react only to this certificate.
            SelfLog.Enable(message =>
            {
                if (message.Contains("scarlet-graylog-tls-test"))
                {
                    Interlocked.Increment(ref reported);
                }
            });

            try
            {
                Task<TlsSession> accepting = server.AcceptAsync(timeout.Token);

                await target.Send(new byte[] { 1, 0 });
                await accepting;

                Assert.Equal(1, Volatile.Read(ref reported));
            } finally
            {
                SelfLog.Disable();
            }
        }

        /// <summary>
        /// The name to validate defaults to the configured host, and an explicit server name replaces
        /// it - which is what a certificate issued to a name the sink does not connect to needs.
        /// </summary>
        [Fact]
        public async Task Send_OverTlsWithAnExplicitServerName_CompletesTheHandshake()
        {
            using X509Certificate2 certificate = TestCertificates.CreateSelfSigned();
            using var server = TlsLoopbackServer.Start(certificate);
            TcpTransportOptions options = TlsOptionsFor(server.Port);
            options.Tls!.ServerName = "graylog.internal.example.org";

            using var target = new TrustingTcpTransportClient(options, Dns());
            using CancellationTokenSource timeout = Timeout();

            Task<TlsSession> accepting = server.AcceptAsync(timeout.Token);
            byte[] payload = { 9, 0 };

            await target.Send(payload);

            TlsSession session = await accepting;

            Assert.Equal(payload, await session.ReadExactlyAsync(payload.Length, timeout.Token));
        }

        [Fact]
        public async Task Send_OverTlsWithAClientCertificate_PresentsItToTheServer()
        {
            using X509Certificate2 serverCertificate = TestCertificates.CreateSelfSigned();
            using TestCertificates.PfxFile clientCertificate = TestCertificates.WritePfx("secret");
            using var server = TlsLoopbackServer.Start(serverCertificate, clientCertificateRequired: true);

            TcpTransportOptions options = TlsOptionsFor(server.Port);
            options.Tls!.ClientCertificatePath = clientCertificate.Path;
            options.Tls.ClientCertificatePassword = "secret";

            using var target = new TrustingTcpTransportClient(options, Dns());
            using CancellationTokenSource timeout = Timeout();

            Task<TlsSession> accepting = server.AcceptAsync(timeout.Token);

            await target.Send(new byte[] { 1, 0 });

            X509Certificate? presented = (await accepting).ClientCertificate;

            Assert.NotNull(presented);
            Assert.Equal(clientCertificate.Thumbprint, presented!.GetCertHashString());
        }

        [Fact]
        public async Task Send_OverTlsWithAnInMemoryClientCertificate_PresentsItToTheServer()
        {
            using X509Certificate2 serverCertificate = TestCertificates.CreateSelfSigned();
            using X509Certificate2 clientCertificate = TestCertificates.CreateSelfSigned("scarlet-graylog-client");
            using var server = TlsLoopbackServer.Start(serverCertificate, clientCertificateRequired: true);

            TcpTransportOptions options = TlsOptionsFor(server.Port);
            options.Tls!.ClientCertificate = clientCertificate;

            using var target = new TrustingTcpTransportClient(options, Dns());
            using CancellationTokenSource timeout = Timeout();

            Task<TlsSession> accepting = server.AcceptAsync(timeout.Token);

            await target.Send(new byte[] { 1, 0 });

            X509Certificate? presented = (await accepting).ClientCertificate;

            Assert.NotNull(presented);
            Assert.Equal(clientCertificate.GetCertHashString(), presented!.GetCertHashString());
        }

        /// <summary>
        /// A certificate the caller supplied is theirs: it may be shared between sinks and outlive
        /// them, so disposing the client must leave it usable.
        /// </summary>
        [Fact]
        public async Task Dispose_WithAnInMemoryClientCertificate_LeavesTheCallersCertificateUsable()
        {
            using X509Certificate2 serverCertificate = TestCertificates.CreateSelfSigned();
            using X509Certificate2 clientCertificate = TestCertificates.CreateSelfSigned("scarlet-graylog-shared");
            using CancellationTokenSource timeout = Timeout();

            using (var first = TlsLoopbackServer.Start(serverCertificate, clientCertificateRequired: true))
            {
                TcpTransportOptions options = TlsOptionsFor(first.Port);
                options.Tls!.ClientCertificate = clientCertificate;

                using var target = new TrustingTcpTransportClient(options, Dns());
                Task<TlsSession> accepting = first.AcceptAsync(timeout.Token);

                await target.Send(new byte[] { 1, 0 });
                await accepting;

                target.Dispose();
            }

            // The same instance has to still complete a handshake through a second client.
            using var second = TlsLoopbackServer.Start(serverCertificate, clientCertificateRequired: true);
            TcpTransportOptions reused = TlsOptionsFor(second.Port);
            reused.Tls!.ClientCertificate = clientCertificate;

            using var reconnected = new TrustingTcpTransportClient(reused, Dns());
            Task<TlsSession> reaccepting = second.AcceptAsync(timeout.Token);

            await reconnected.Send(new byte[] { 2, 0 });

            X509Certificate? presented = (await reaccepting).ClientCertificate;

            Assert.NotNull(presented);
            Assert.Equal(clientCertificate.GetCertHashString(), presented!.GetCertHashString());
        }

        /// <summary>
        /// An unusable client certificate has to fail loudly at the handshake rather than connect
        /// without one.
        /// </summary>
        [Fact]
        public async Task Send_OverTlsWithAnUnreadableClientCertificate_Throws()
        {
            using X509Certificate2 serverCertificate = TestCertificates.CreateSelfSigned();
            using var server = TlsLoopbackServer.Start(serverCertificate);

            TcpTransportOptions options = TlsOptionsFor(server.Port);
            options.Tls!.ClientCertificatePath = "certificate-that-does-not-exist.pfx";

            using var target = new TrustingTcpTransportClient(options, Dns());

            await Assert.ThrowsAsync<System.IO.FileNotFoundException>(() => target.Send(new byte[] { 1, 0 }));
        }

        /// <summary>
        /// The default validation applies the platform's own policy, so a self-signed certificate is
        /// refused and the half-built connection is torn down instead of left behind.
        /// </summary>
        [Fact]
        public async Task Send_WithDefaultCertificateValidation_RefusesASelfSignedCertificate()
        {
            using X509Certificate2 certificate = TestCertificates.CreateSelfSigned();
            using var server = TlsLoopbackServer.Start(certificate);
            using var target = new TcpTransportClient(TlsOptionsFor(server.Port), Dns());
            using CancellationTokenSource timeout = Timeout();

            Task<TlsSession> accepting = server.AcceptAsync(timeout.Token);

            await Assert.ThrowsAnyAsync<AuthenticationException>(() => target.Send(new byte[] { 1, 0 }));

            // Under TLS 1.3 the server finishes its own handshake before the client's alert reaches
            // it, so whether the server sees the refusal is not something to assert on.
            await Ignoring(accepting);
        }

        private static async Task Ignoring(Task task)
        {
            try
            {
                await task;
            } catch (Exception)
            {
                // The test broke this connection on purpose.
            }
        }

        private static TcpTransportOptions TlsOptionsFor(int port)
        {
            return new TcpTransportOptions
            {
                Host = "graylog.example.org",
                Port = port,
                Tls = new TlsOptions()
            };
        }

        private static IDnsInfoProvider Dns()
        {
            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress("graylog.example.org").Returns(Task.FromResult<IPAddress?>(IPAddress.Loopback));

            return dnsInfoProvider;
        }

        private static CancellationTokenSource Timeout()
        {
            CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            return timeout;
        }

        /// <summary>
        /// A client that accepts the loopback server's self-signed certificate, and records what it
        /// was asked to validate.
        /// </summary>
        /// <remarks>
        /// It records rather than asserts: an exception thrown inside the validation callback surfaces
        /// as a handshake failure, which would hide what actually went wrong.
        /// </remarks>
        private sealed class TrustingTcpTransportClient : TcpTransportClient
        {
            public TrustingTcpTransportClient(TcpTransportOptions options, IDnsInfoProvider dnsInfoProvider)
                : base(options, dnsInfoProvider)
            {
            }

            public int ValidationCalls { get; private set; }

            public string? ValidatedThumbprint { get; private set; }

            protected override bool ValidateServerCertificate(
                object sender,
                X509Certificate? certificate,
                X509Chain? chain,
                SslPolicyErrors sslPolicyErrors)
            {
                ValidationCalls++;
                ValidatedThumbprint = certificate?.GetCertHashString();

                return true;
            }
        }
    }
}
