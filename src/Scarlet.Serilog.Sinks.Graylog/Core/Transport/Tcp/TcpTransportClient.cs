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
    /// <seealso cref="ITransportClient" />
    public class TcpTransportClient : ITransportClient
    {
        private Stream? _stream;

        private readonly TcpTransportOptions _options;
        private readonly IDnsInfoProvider _dnsInfoProvider;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private TcpClient? _client;
        private X509Certificate2? _clientCertificate;
        private bool _ownsClientCertificate;

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
        public async Task Send(ReadOnlyMemory<byte> payload)
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
                    var sslStream = new SslStream(stream, false, ValidateServerCertificate);

                    // Adopted before the handshake, so a failure below disposes it too.
                    stream = sslStream;

                    var certificates = new X509CertificateCollection();
                    if (TlsCertificateLoader.HasClientCertificate(_options.Tls))
                    {
                        // Resolved once and reused for the life of the client. Connect runs again on
                        // every reconnect, and loading here pulled the PFX off disk each time and took
                        // an unmanaged key handle that nothing released.
                        if (_clientCertificate == null)
                        {
                            (_clientCertificate, _ownsClientCertificate) = TlsCertificateLoader.ResolveClientCertificate(_options.Tls!);
                        }

                        certificates.Add(_clientCertificate);
                    }

                    await sslStream.AuthenticateAsClientAsync(sslHost, certificates, SslProtocols.None, true).ConfigureAwait(false);

                    if (sslStream.RemoteCertificate != null)
                    {
                        SelfLog.WriteLine("Remote cert was issued to {0} and is valid from {1} until {2}.",
                            sslStream.RemoteCertificate.Subject,
                            sslStream.RemoteCertificate.GetEffectiveDateString(),
                            sslStream.RemoteCertificate.GetExpirationDateString());
                    }
                    else
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

        /// <summary>
        /// Validates the certificate the Graylog server presents during the TLS handshake.
        /// </summary>
        /// <param name="sender">The <see cref="SslStream"/> performing the handshake.</param>
        /// <param name="certificate">The certificate the server presented, if any.</param>
        /// <param name="chain">The chain built for <paramref name="certificate"/>, if any.</param>
        /// <param name="sslPolicyErrors">The errors the platform found while validating.</param>
        /// <returns><c>true</c> to continue the handshake; <c>false</c> to fail it.</returns>
        /// <remarks>
        /// The default applies the platform's own policy - the certificate is accepted only when it
        /// validated without error. Override this to accept a certificate the platform does not trust,
        /// which is what a Graylog input with a self-signed certificate needs.
        /// </remarks>
        protected virtual bool ValidateServerCertificate(
            object sender,
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            return sslPolicyErrors == SslPolicyErrors.None;
        }

        private async Task ConnectWithTimeout(TcpClient client, IPAddress address, int port)
        {
            if (_options.ConnectTimeout is not { } timeout)
            {
                await client.ConnectAsync(address, port).ConfigureAwait(false);
                return;
            }

#if NET
            // Cancellation actually aborts the connect here, so the socket is not left dialling in the
            // background after the caller has given up on it.
            using var timeoutSource = new CancellationTokenSource(timeout);

            try
            {
                await client.ConnectAsync(address, port, timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
            {
                throw new TimeoutException("The TCP connection timed out.");
            }
#else
            await AwaitWithTimeout(client.ConnectAsync(address, port), timeout, "connection").ConfigureAwait(false);
#endif
        }

        private async Task WriteWithTimeout(Stream stream, ReadOnlyMemory<byte> payload)
        {
#if !NET
            // No span-based stream API on this target, so the frame goes out as the array underneath
            // it rather than being copied into another one.
            ArraySegment<byte> segment = ByteBufferWriter.AsArraySegment(payload);
#endif
            if (_options.WriteTimeout is not { } timeout)
            {
#if NET
                await stream.WriteAsync(payload).ConfigureAwait(false);
#else
                await stream.WriteAsync(segment.Array!, segment.Offset, segment.Count).ConfigureAwait(false);
#endif
                await stream.FlushAsync().ConfigureAwait(false);

                return;
            }

#if NET
            // One source for both halves: the timeout covers getting the frame out, not each call
            // separately, and cancelling it tears the write down rather than leaving it running against
            // a stream the caller is about to close.
            using var timeoutSource = new CancellationTokenSource(timeout);

            try
            {
                await stream.WriteAsync(payload, timeoutSource.Token).ConfigureAwait(false);
                await stream.FlushAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
            {
                throw new TimeoutException("The TCP write timed out.");
            }
#else
            await AwaitWithTimeout(
                stream.WriteAsync(segment.Array!, segment.Offset, segment.Count),
                timeout,
                "write").ConfigureAwait(false);
            await AwaitWithTimeout(stream.FlushAsync(), timeout, "flush").ConfigureAwait(false);
#endif
        }

#if !NET
        /// <summary>
        /// Awaits <paramref name="operation"/>, giving up after <paramref name="timeout"/>.
        /// </summary>
        /// <remarks>
        /// The fallback for frameworks whose stream and socket APIs take no <see cref="CancellationToken"/>,
        /// where nothing can stop the operation itself - only stop waiting on it. Two details matter.
        /// The delay is cancelled once the race is decided, so a short timeout on a busy sink does not
        /// leave a timer per send queued until it fires; and an abandoned operation is observed, so the
        /// exception it fails with later cannot resurface as an unobserved task exception - fatal to the
        /// process on a .NET Framework application configured with
        /// <c>&lt;ThrowUnobservedTaskExceptions enabled="true"/&gt;</c>.
        /// </remarks>
        private static async Task AwaitWithTimeout(
            Task operation,
            TimeSpan timeout,
            string operationName)
        {
            using var timeoutSource = new CancellationTokenSource();

            Task delay = Task.Delay(timeout, timeoutSource.Token);

            if (await Task.WhenAny(operation, delay).ConfigureAwait(false) != operation)
            {
                Observe(operation);
                throw new TimeoutException($"The TCP {operationName} timed out.");
            }

            timeoutSource.Cancel();

            await operation.ConfigureAwait(false);
        }

        /// <summary>
        /// Swallows the eventual outcome of an operation nobody is waiting on any more.
        /// </summary>
        private static void Observe(Task operation)
        {
            operation.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

#endif

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
        /// <remarks>
        /// A client certificate supplied through <see cref="TlsOptions.ClientCertificate"/> belongs to
        /// the caller and is left alone; only one loaded from
        /// <see cref="TlsOptions.ClientCertificatePath"/> is disposed here.
        /// </remarks>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                CloseConnection();

                if (_ownsClientCertificate)
                {
                    _clientCertificate?.Dispose();
                }

                _clientCertificate = null;
                _ownsClientCertificate = false;
            }
        }
    }
}
