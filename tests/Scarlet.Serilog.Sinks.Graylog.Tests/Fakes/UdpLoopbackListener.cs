using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Fakes
{
    /// <summary>
    /// A UDP socket bound to an ephemeral loopback port, for exercising the UDP transport without a
    /// Graylog instance. The connection-oriented counterparts are <see cref="TcpLoopbackServer"/> and
    /// <see cref="TlsLoopbackServer"/>.
    /// </summary>
    /// <remarks>
    /// Worth binding even for a test that never reads: a datagram sent to a port with nothing on it
    /// draws an ICMP port-unreachable, which Windows surfaces as a <see cref="SocketException"/> on a
    /// later send through the same socket. A bound listener absorbs the datagram instead.
    /// </remarks>
    internal sealed class UdpLoopbackListener : IDisposable
    {
        private readonly UdpClient _client;

        private UdpLoopbackListener(UdpClient client, int port)
        {
            _client = client;
            Port = port;
        }

        /// <summary>
        /// Binds an ephemeral port. Named for symmetry with the TCP and TLS servers, though for a
        /// connectionless socket binding is all there is to starting.
        /// </summary>
        /// <param name="address">The address to bind; defaults to the IPv4 loopback.</param>
        public static UdpLoopbackListener Start(IPAddress? address = null)
        {
            var client = new UdpClient(new IPEndPoint(address ?? IPAddress.Loopback, 0));
            try
            {
                return new UdpLoopbackListener(client, ((IPEndPoint)client.Client.LocalEndPoint!).Port);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        public int Port { get; }

        /// <summary>
        /// Begins receiving one datagram and returns its payload.
        /// </summary>
        /// <remarks>
        /// Start this before the send and await it afterwards, so the receive is already posted when
        /// the datagram arrives. The token should carry a deadline: a datagram that never turns up
        /// would otherwise hang the test rather than fail it.
        /// </remarks>
        public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
        {
            UdpReceiveResult received = await _client.ReceiveAsync(cancellationToken);

            return received.Buffer;
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
