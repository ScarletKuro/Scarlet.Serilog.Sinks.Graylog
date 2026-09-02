using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Debugging;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp
{
    /// <summary>
    /// Udp transport client
    /// </summary>
    /// <seealso cref="byte" />
    public sealed class UdpTransportClient : ITransportClient<byte[]>
    {
        private IPEndPoint? _ipEndPoint;

        private readonly GraylogSinkOptionsBase _options;
        private readonly IDnsInfoProvider _dnsInfoProvider;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private UdpClient? _client;

        public UdpTransportClient(GraylogSinkOptionsBase options, IDnsInfoProvider dnsInfoProvider)
        {
            _options = options;
            _dnsInfoProvider = dnsInfoProvider;

        }

        private async Task<bool> EnsureTarget()
        {
            if (_ipEndPoint != null)
            {
                return true;
            }

            string hostNameOrAddress = _options.HostnameOrAddress
                ?? throw new InvalidOperationException("The HostnameOrAddress value must be set.");
            var ipAddress = await _dnsInfoProvider.GetIpAddress(hostNameOrAddress).ConfigureAwait(false);
            if (ipAddress == default)
            {
                SelfLog.WriteLine("IP address could not be resolved.");
                return false;
            }

            _ipEndPoint = new IPEndPoint(ipAddress, _options.Port.GetValueOrDefault());
            _client = new UdpClient(ipAddress.AddressFamily);
            return true;
        }

        /// <summary>
        /// Sends the specified payload.
        /// </summary>
        /// <param name="payload">The payload.</param>
        public async Task Send(byte[] payload)
        {
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!await EnsureTarget().ConfigureAwait(false))
                {
                    throw new InvalidOperationException("The Graylog endpoint could not be resolved.");
                }

                UdpClient client = _client ?? throw new InvalidOperationException("The UDP client could not be initialized.");
                IPEndPoint endpoint = _ipEndPoint ?? throw new InvalidOperationException("The Graylog endpoint could not be initialized.");
                await client.SendAsync(payload, payload.Length, endpoint).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Dispose() => _client?.Dispose();
    }
}
