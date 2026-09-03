using System;
using System.Diagnostics;
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
    /// Sends are serialized, so one instance is safe to share. The target address is resolved on the
    /// first send and re-resolved every <see cref="UdpTransportOptions.DnsRefreshInterval"/>; a host
    /// that is already an IP literal is never resolved at all.
    /// </remarks>
    /// <seealso cref="ITransportClient{T}" />
    public sealed class UdpTransportClient : ITransportClient<byte[]>
    {
        private IPEndPoint? _ipEndPoint;

        private readonly UdpTransportOptions _options;
        private readonly IDnsInfoProvider _dnsInfoProvider;
        private readonly Func<long> _timestamp;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private UdpClient? _client;
        private bool _targetIsLiteral;
        private long _resolvedAt;

        /// <summary>
        /// Initializes a new instance of the <see cref="UdpTransportClient"/> class.
        /// </summary>
        /// <param name="options">The UDP transport options.</param>
        /// <param name="dnsInfoProvider">Resolves <see cref="UdpTransportOptions.Host"/> to an address.</param>
        public UdpTransportClient(UdpTransportOptions options, IDnsInfoProvider dnsInfoProvider)
            : this(options, dnsInfoProvider, Stopwatch.GetTimestamp)
        {
        }

        /// <summary>
        /// Initializes a new instance with an injected clock, so a test can age the resolved address
        /// without waiting. <see cref="Stopwatch.GetTimestamp"/> is the only clock available on every
        /// target framework - TimeProvider and Environment.TickCount64 are not.
        /// </summary>
        internal UdpTransportClient(UdpTransportOptions options, IDnsInfoProvider dnsInfoProvider, Func<long> timestamp)
        {
            _options = options;
            _dnsInfoProvider = dnsInfoProvider;
            _timestamp = timestamp;
        }

        private async Task<bool> EnsureTarget()
        {
            if (_ipEndPoint != null && !IsStale())
            {
                return true;
            }

            string hostNameOrAddress = _options.Host ?? throw new InvalidOperationException("The UDP host value must be set.");

            // An IP literal needs no resolver, and so has nothing to go stale.
            if (IPAddress.TryParse(hostNameOrAddress, out IPAddress? literal))
            {
                SetTarget(literal, isLiteral: true);

                return true;
            }

            IPAddress? ipAddress;
            try
            {
                ipAddress = await _dnsInfoProvider.GetIpAddress(hostNameOrAddress).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                return KeepPreviousTarget(exception.ToString());
            }

            if (ipAddress == null)
            {
                return KeepPreviousTarget("the host did not resolve to a usable address");
            }

            SetTarget(ipAddress, isLiteral: false);

            return true;
        }

        /// <summary>
        /// Reports whether the resolved address has outlived <see cref="UdpTransportOptions.DnsRefreshInterval"/>.
        /// </summary>
        private bool IsStale()
        {
            if (_targetIsLiteral || _options.DnsRefreshInterval is not { } interval)
            {
                return false;
            }

            double elapsedSeconds = (_timestamp() - _resolvedAt) / (double)Stopwatch.Frequency;

            return elapsedSeconds >= interval.TotalSeconds;
        }

        /// <summary>
        /// Handles a resolution that produced nothing. A failed *refresh* must not cost events, so the
        /// last known good address stays in use and is retried on the next interval; only a failure
        /// with no previous address is fatal to the send.
        /// </summary>
        private bool KeepPreviousTarget(string reason)
        {
            if (_ipEndPoint == null)
            {
                SelfLog.WriteLine("IP address could not be resolved: {0}", reason);

                return false;
            }

            SelfLog.WriteLine("Could not refresh the Graylog endpoint, keeping {0}: {1}", _ipEndPoint, reason);
            _resolvedAt = _timestamp();

            return true;
        }

        private void SetTarget(IPAddress address, bool isLiteral)
        {
            // A socket is bound to one address family, so an IPv4 to IPv6 move needs a new client.
            if (_client == null || _client.Client.AddressFamily != address.AddressFamily)
            {
                _client?.Dispose();
                _client = new UdpClient(address.AddressFamily);
            }

            _ipEndPoint = new IPEndPoint(address, _options.Port);
            _targetIsLiteral = isLiteral;
            _resolvedAt = _timestamp();
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
