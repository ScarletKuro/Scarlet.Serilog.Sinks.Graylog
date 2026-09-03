using Serilog.Debugging;
using System;
using System.IO;
using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport.Tcp
{
    /// <summary>
    /// Owns the connection to a Graylog TCP input and writes GELF frames to it.
    /// </summary>
    /// <remarks>
    /// The connection is established on the first send and reused afterwards; a failed write
    /// closes it so the next send reconnects. Sends are serialized, so one instance is safe to
    /// share - and has to be, because the frames share a single stream.
    /// </remarks>
    /// <seealso cref="ITransportClient{T}" />
    public class TcpTransportClient : ITransportClient<byte[]>
    {
        private Stream? _stream;

        private readonly TcpTransportOptions _options;
        private readonly IDnsInfoProvider _dnsInfoProvider;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private TcpClient? _client;
        private X509Certificate2? _clientCertificate;

        /// <summary>
        /// Initializes a new instance of the <see cref="TcpTransportClient"/> class.
        /// </summary>
        /// <param name="options">The TCP transport options.</param>
        /// <param name="dnsInfoProvider">Resolves <see cref="TcpTransportOptions.Host"/> to an address.</param>
        public TcpTransportClient(TcpTransportOptions options, IDnsInfoProvider dnsInfoProvider)
        {
            _options = options;
            _dnsInfoProvider = dnsInfoProvider;

        }


        /// <inheritdoc />
        public async Task Send(byte[] payload)
        {
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Stream stream = await EnsureConnection().ConfigureAwait(false);

                await WriteWithTimeout(stream, payload).ConfigureAwait(false);
            }
            catch
            {
                // A failed write leaves TcpClient.Connected unreliable; force a clean reconnect next time.
                CloseConnection();
                throw;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task<Stream> EnsureConnection()
        {
            if (_client is { Connected: true } && _stream != null)
            {
                return _stream;
            }

            CloseConnection();
            return await Connect().ConfigureAwait(false);
        }

        private async Task<Stream> Connect()
        {
            string hostNameOrAddress = _options.Host ?? throw new InvalidOperationException("The TCP host value must be set.");
            // An IP literal needs no resolver; only a name is looked up, and it is looked up
            // again on every reconnect, so a host that moves is picked up there.
            IPAddress? address = IPAddress.TryParse(hostNameOrAddress, out IPAddress? literal)
                ? literal
                : await _dnsInfoProvider.GetIpAddress(hostNameOrAddress).ConfigureAwait(false);
            if (address == null)
            {
                SelfLog.WriteLine("IP address could not be resolved.");
                throw new InvalidOperationException("The Graylog endpoint could not be resolved.");
            }

            int port = _options.Port;
            string? sslHost = _options.Tls == null ? null : _options.Tls.ServerName ?? hostNameOrAddress;

            var client = new TcpClient(address.AddressFamily);
            Stream? stream = null;
            try
            {
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, _options.EnableKeepAlive);
                await ConnectWithTimeout(client, address, port).ConfigureAwait(false);
                stream = client.GetStream();

                if (!string.IsNullOrWhiteSpace(sslHost))
                {
                    var sslStream = new SslStream(stream, false);

                    // Adopted before the handshake, so a failure below disposes it too.
                    stream = sslStream;

                    var certificates = new X509CertificateCollection();
                    if (!string.IsNullOrWhiteSpace(_options.Tls?.ClientCertificatePath))
                    {
                        // Loaded once and reused for the life of the client. Connect runs again on
                        // every reconnect, and loading here pulled the PFX off disk each time and took
                        // an unmanaged key handle that nothing released.
                        _clientCertificate ??= TlsCertificateLoader.LoadClientCertificate(_options.Tls!);
                        certificates.Add(_clientCertificate);
                    }

                    await sslStream.AuthenticateAsClientAsync(sslHost, certificates, SslProtocols.None, true).ConfigureAwait(false);

                    if (sslStream.RemoteCertificate != null)
                    {
                        SelfLog.WriteLine("Remote cert was issued to {0} and is valid from {1} until {2}.",
                            sslStream.RemoteCertificate.Subject,
                            sslStream.RemoteCertificate.GetEffectiveDateString(),
                            sslStream.RemoteCertificate.GetExpirationDateString());
                    } else
                    {
                        SelfLog.WriteLine("Remote certificate is null.");
                    }
                }

                _client = client;
                _stream = stream;
                return stream;
            }
            catch
            {
                stream?.Dispose();
                client.Dispose();
                throw;
            }
        }

        private async Task ConnectWithTimeout(TcpClient client, IPAddress address, int port)
        {
            Task connect = client.ConnectAsync(address, port);
            if (!_options.ConnectTimeout.HasValue)
            {
                await connect.ConfigureAwait(false);
                return;
            }

            if (await Task.WhenAny(connect, Task.Delay(_options.ConnectTimeout.Value)).ConfigureAwait(false) != connect)
            {
                throw new TimeoutException("The TCP connection timed out.");
            }

            await connect.ConfigureAwait(false);
        }

        private async Task WriteWithTimeout(Stream stream, byte[] payload)
        {
#if !NET
            Task write = stream.WriteAsync(payload, 0, payload.Length);
#else
            Task write = stream.WriteAsync(payload).AsTask();
#endif
            await AwaitWithTimeout(write, _options.WriteTimeout, "write").ConfigureAwait(false);
            await AwaitWithTimeout(stream.FlushAsync(), _options.WriteTimeout, "flush").ConfigureAwait(false);
        }

        private static async Task AwaitWithTimeout(Task operation, TimeSpan? timeout, string operationName)
        {
            if (!timeout.HasValue)
            {
                await operation.ConfigureAwait(false);
                return;
            }

            if (await Task.WhenAny(operation, Task.Delay(timeout.Value)).ConfigureAwait(false) != operation)
                throw new TimeoutException($"The TCP {operationName} timed out.");

            await operation.ConfigureAwait(false);
        }

        private void CloseConnection()
        {
            _stream?.Dispose();
            _stream = null;
            _client?.Dispose();
            _client = null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the resources used by this client.
        /// </summary>
        /// <param name="disposing"><c>true</c> when called from <see cref="Dispose()"/>; the stream and socket are closed with it.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                CloseConnection();
                _clientCertificate?.Dispose();
                _clientCertificate = null;
            }
        }
    }
}
