using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Fakes
{
    /// <summary>
    /// A TLS server on the loopback interface, for exercising the TCP transport's TLS path without a
    /// Graylog instance.
    /// </summary>
    /// <remarks>
    /// It presents a self-signed certificate, so a client only completes the handshake if it overrides
    /// <c>TcpTransportClient.ValidateServerCertificate</c> - which is exactly what the tests assert.
    /// </remarks>
    internal sealed class TlsLoopbackServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly X509Certificate2 _certificate;
        private readonly bool _clientCertificateRequired;
        private readonly List<IDisposable> _accepted = new List<IDisposable>();

        private TlsLoopbackServer(TcpListener listener, int port, X509Certificate2 certificate, bool clientCertificateRequired)
        {
            _listener = listener;
            Port = port;
            _certificate = certificate;
            _clientCertificateRequired = clientCertificateRequired;
        }

        /// <summary>
        /// Binds and starts listening. A server only exists once it is accepting, so there is no
        /// unstarted state for a test to trip over.
        /// </summary>
        /// <remarks>
        /// A factory rather than a constructor because binding can fail, and a constructor that
        /// throws leaves the listener with nothing to dispose it.
        /// </remarks>
        public static TlsLoopbackServer Start(X509Certificate2 certificate, bool clientCertificateRequired = false)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();

                return new TlsLoopbackServer(listener, ((IPEndPoint)listener.LocalEndpoint).Port, certificate, clientCertificateRequired);
            }
            catch
            {
                listener.Dispose();
                throw;
            }
        }

        public int Port { get; }

        /// <summary>
        /// Accepts one connection and completes the server side of the handshake.
        /// </summary>
        /// <remarks>
        /// Start this before the client sends, and await it afterwards: both sides of a handshake have
        /// to be in flight at the same time. It faults when the client rejects the certificate, so a
        /// rejection test should await it to observe that failure.
        /// </remarks>
        public async Task<TlsSession> AcceptAsync(CancellationToken cancellationToken)
        {
            TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
            _accepted.Add(client);

            var stream = new SslStream(client.GetStream(), false);
            _accepted.Add(stream);

            // The client presents a self-signed certificate too, so the server has to accept one it
            // cannot chain to a trusted root - the point of the test is that the certificate arrives.
            await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = _certificate,
                ClientCertificateRequired = _clientCertificateRequired,
                EnabledSslProtocols = SslProtocols.None,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            }, cancellationToken);

            return new TlsSession(stream);
        }

        public void Dispose()
        {
            for (int index = _accepted.Count - 1; index >= 0; index--)
            {
                try
                {
                    _accepted[index].Dispose();
                } catch (Exception)
                {
                    // Tearing down a connection the test already broke on purpose.
                }
            }

            _listener.Stop();
            _listener.Dispose();
        }
    }

    /// <summary>
    /// One accepted TLS connection.
    /// </summary>
    internal sealed class TlsSession
    {
        private readonly SslStream _stream;

        public TlsSession(SslStream stream)
        {
            _stream = stream;
        }

        /// <summary>
        /// The certificate the client presented, or <c>null</c> when it presented none.
        /// </summary>
        public X509Certificate? ClientCertificate => _stream.RemoteCertificate;

        public async Task<byte[]> ReadExactlyAsync(int count, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[count];
            await _stream.ReadExactlyAsync(buffer, cancellationToken);

            return buffer;
        }
    }
}
