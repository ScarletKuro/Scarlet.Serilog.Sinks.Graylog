using Serilog.Debugging;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport.Tcp
{
    public class TcpTransportClient : ITransportClient<byte[]>
    {
        private const int DefaultPort = 12201;

        private Stream? _stream;

        private readonly GraylogSinkOptionsBase _options;
        private readonly IDnsInfoProvider _dnsInfoProvider;
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
            Stream stream = await EnsureConnection().ConfigureAwait(false)
                ?? throw new InvalidOperationException("The Graylog endpoint could not be resolved.");

#if !NET
            await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
#else
            await stream.WriteAsync(payload).ConfigureAwait(false);
#endif

            await stream.FlushAsync().ConfigureAwait(false);
        }

        private async Task<Stream?> EnsureConnection()
        {
            if (_client == null || !_client.Connected)
            {
                await Connect().ConfigureAwait(false);
            }

            return _stream;
        }

        private async Task Connect()
        {
            string hostNameOrAddress = _options.HostnameOrAddress
                ?? throw new InvalidOperationException("The HostnameOrAddress value must be set.");
            IPAddress? _address = await _dnsInfoProvider.GetIpAddress(hostNameOrAddress).ConfigureAwait(false);
            if (_address == default)
            {
                SelfLog.WriteLine("IP address could not be resolved.");
                return;
            }

            int port = _options.Port.GetValueOrDefault(DefaultPort);
            string? sslHost = _options.UseSsl ? hostNameOrAddress : null;

            _client ??= new TcpClient(_address.AddressFamily);
            await _client.ConnectAsync(_address, port).ConfigureAwait(false);

            _stream = _client.GetStream();

            if (!string.IsNullOrWhiteSpace(sslHost))
            {
                var _sslStream = new SslStream(_stream, false);

                await _sslStream.AuthenticateAsClientAsync(sslHost).ConfigureAwait(false);

                if (_sslStream.RemoteCertificate != null)
                {
                    SelfLog.WriteLine("Remote cert was issued to {0} and is valid from {1} until {2}.",
                        _sslStream.RemoteCertificate.Subject,
                        _sslStream.RemoteCertificate.GetEffectiveDateString(),
                        _sslStream.RemoteCertificate.GetExpirationDateString());

                    _stream = _sslStream;
                } else
                {
                    SelfLog.WriteLine("Remote certificate is null.");
                }
            }
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
                _stream?.Dispose();
                _client?.Dispose();
            }
        }
    }
}
