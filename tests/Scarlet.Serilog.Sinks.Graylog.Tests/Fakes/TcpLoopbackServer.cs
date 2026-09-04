using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Fakes
{
    /// <summary>
    /// A plain TCP server on the loopback interface, for exercising the TCP transport without a
    /// Graylog instance. The TLS counterpart is <see cref="TlsLoopbackServer"/>.
    /// </summary>
    /// <remarks>
    /// It binds an ephemeral port and reports it through <see cref="Port"/>, so tests never collide
    /// over a fixed one.
    /// </remarks>
    internal sealed class TcpLoopbackServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly List<IDisposable> _accepted = new List<IDisposable>();

        private TcpLoopbackServer(TcpListener listener, int port)
        {
            _listener = listener;
            Port = port;
        }

        /// <summary>
        /// Binds and starts listening. A server only exists once it is accepting, so there is no
        /// unstarted state for a test to trip over.
        /// </summary>
        /// <param name="address">The address to bind; defaults to the IPv4 loopback.</param>
        /// <param name="port">The port to bind, or 0 to let the operating system pick one.</param>
        /// <remarks>
        /// A factory rather than a constructor because binding can fail - a named
        /// <paramref name="port"/> may already be taken - and a constructor that throws leaves the
        /// listener with nothing to dispose it.
        /// </remarks>
        public static TcpLoopbackServer Start(IPAddress? address = null, int port = 0)
        {
            var listener = new TcpListener(address ?? IPAddress.Loopback, port);
            try
            {
                listener.Start();

                return new TcpLoopbackServer(listener, ((IPEndPoint)listener.LocalEndpoint).Port);
            }
            catch
            {
                listener.Dispose();
                throw;
            }
        }

        public int Port { get; }

        /// <summary>
        /// Binds an ephemeral port and releases it, so a connect to the returned port is refused
        /// rather than left hanging.
        /// </summary>
        /// <remarks>
        /// Picking an arbitrary port number risks hitting something that is actually listening; going
        /// through the operating system's own allocation and giving it straight back does not.
        /// </remarks>
        public static int ReserveClosedPort()
        {
            using TcpLoopbackServer reservation = Start();

            return reservation.Port;
        }

        /// <summary>
        /// Accepts one connection.
        /// </summary>
        /// <remarks>
        /// Start this before the client sends and await it afterwards: the accept has to be in flight
        /// while the connection is being made.
        /// </remarks>
        public async Task<TcpSession> AcceptAsync(CancellationToken cancellationToken)
        {
            TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
            _accepted.Add(client);

            return new TcpSession(client);
        }

        public void Dispose()
        {
            for (int index = _accepted.Count - 1; index >= 0; index--)
            {
                try
                {
                    _accepted[index].Dispose();
                }
                catch (Exception)
                {
                    // Tearing down a connection the test may already have broken on purpose.
                }
            }

            _listener.Stop();
            _listener.Dispose();
        }
    }

    /// <summary>
    /// One accepted plain TCP connection.
    /// </summary>
    internal sealed class TcpSession
    {
        private readonly TcpClient _client;

        public TcpSession(TcpClient client)
        {
            _client = client;
        }

        /// <summary>
        /// Reads exactly <paramref name="count"/> bytes, so a short read fails the test instead of
        /// passing on a truncated frame.
        /// </summary>
        public async Task<byte[]> ReadExactlyAsync(int count, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[count];
            await _client.GetStream().ReadExactlyAsync(buffer, cancellationToken);

            return buffer;
        }
    }
}
