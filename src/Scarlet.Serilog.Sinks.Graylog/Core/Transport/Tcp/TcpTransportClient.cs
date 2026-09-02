using Serilog.Debugging;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport.Tcp
{
    public class TcpTransportClient : ITransportClient<byte[]>
    {
        private const int DefaultPort = 12201;

        private Stream? _stream;

        private readonly GraylogSinkOptionsBase _options;
        private readonly IDnsInfoProvider _dnsInfoProvider;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private TcpClient? _client;

        /// <inheritdoc />
        public TcpTransportClient(GraylogSinkOptionsBase options, IDnsInfoProvider dnsInfoProvider)
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

#if !NET
                await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
#else
                await stream.WriteAsync(payload).ConfigureAwait(false);
#endif

                await stream.FlushAsync().ConfigureAwait(false);
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
            if (_client != null && _client.Connected && _stream != null)
            {
                return _stream;
            }

            CloseConnection();
            return await Connect().ConfigureAwait(false);
        }

        private async Task<Stream> Connect()
        {
            string hostNameOrAddress = _options.HostnameOrAddress
                ?? throw new InvalidOperationException("The HostnameOrAddress value must be set.");
            IPAddress? _address = await _dnsInfoProvider.GetIpAddress(hostNameOrAddress).ConfigureAwait(false);
            if (_address == default)
            {
                SelfLog.WriteLine("IP address could not be resolved.");
                throw new InvalidOperationException("The Graylog endpoint could not be resolved.");
            }

            int port = _options.Port.GetValueOrDefault(DefaultPort);
            string? sslHost = _options.UseSsl ? hostNameOrAddress : null;

            var client = new TcpClient(_address.AddressFamily);
            try
            {
                await client.ConnectAsync(_address, port).ConfigureAwait(false);
                Stream stream = client.GetStream();

                if (!string.IsNullOrWhiteSpace(sslHost))
                {
                    var sslStream = new SslStream(stream, false);
                    await sslStream.AuthenticateAsClientAsync(sslHost).ConfigureAwait(false);
                    stream = sslStream;

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
                client.Dispose();
                throw;
            }
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

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                CloseConnection();
            }
        }
    }
}
