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
    /// Sends GELF datagrams to a Graylog UDP input.
    /// </summary>
    /// <remarks>
    /// The endpoint is resolved once, on the first send, and the socket is reused afterwards. Sends
    /// are serialized, so one instance is safe to share.
    /// </remarks>
    /// <seealso cref="ITransportClient{T}" />
    public sealed class UdpTransportClient : ITransportClient<byte[]>
    {
        private IPEndPoint? _ipEndPoint;

        private readonly UdpTransportOptions _options;
        private readonly IDnsInfoProvider _dnsInfoProvider;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private UdpClient? _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="UdpTransportClient"/> class.
        /// </summary>
        /// <param name="options">The UDP transport options.</param>
        /// <param name="dnsInfoProvider">Resolves <see cref="UdpTransportOptions.Host"/> to an address.</param>
        public UdpTransportClient(UdpTransportOptions options, IDnsInfoProvider dnsInfoProvider)
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

            string hostNameOrAddress = _options.Host ?? throw new InvalidOperationException("The UDP host value must be set.");
            var ipAddress = await _dnsInfoProvider.GetIpAddress(hostNameOrAddress).ConfigureAwait(false);
            if (ipAddress == null)
            {
                SelfLog.WriteLine("IP address could not be resolved.");
                return false;
            }

            _ipEndPoint = new IPEndPoint(ipAddress, _options.Port);
            _client = new UdpClient(ipAddress.AddressFamily);
            return true;
        }

        /// <summary>
        /// Sends the specified payload.
        /// </summary>
        /// <param name="payload">The payload.</param>
        /// <exception cref="InvalidOperationException">No host is configured, or it could not be resolved.</exception>
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

        /// <inheritdoc />
        public void Dispose() => _client?.Dispose();
    }
}
