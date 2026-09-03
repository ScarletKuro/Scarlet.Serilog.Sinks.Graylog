using NSubstitute;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Tcp;
using Scarlet.Serilog.Sinks.Graylog.Tests.Fakes;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.Transport.Tcp
{
    public class TcpTransportClientFixture
    {
        private const string Host = "graylog.example.org";

        [Fact]
        public async Task Send_AfterAConnectionFailure_ReconnectsWithANewClient()
        {
            int port = TcpLoopbackServer.ReserveClosedPort();

            using var target = new TcpTransportClient(OptionsFor(port), Dns());

            await Assert.ThrowsAnyAsync<SocketException>(() => target.Send(new byte[] { 1 }));

            using var server = TcpLoopbackServer.Start(port: port);
            using CancellationTokenSource timeout = Timeout();
            Task<TcpSession> accepting = server.AcceptAsync(timeout.Token);

            byte[] payload = { 2, 0 };
            await target.Send(payload);

            TcpSession session = await accepting;

            Assert.Equal(payload, await session.ReadExactlyAsync(payload.Length, timeout.Token));
        }

        [Fact]
        public async Task Send_WhenEndpointCannotBeResolved_ThrowsAClearError()
        {
            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress(Host).Returns(Task.FromResult<IPAddress?>(null));

            using var target = new TcpTransportClient(new TcpTransportOptions { Host = Host }, dnsInfoProvider);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => target.Send(new byte[] { 1 }));

            Assert.Equal("The Graylog endpoint could not be resolved.", exception.Message);
        }

        [Fact]
        public async Task Send_DeliversThePayloadToTheResolvedEndpoint()
        {
            using var server = TcpLoopbackServer.Start();
            IDnsInfoProvider dnsInfoProvider = Dns();
            using var target = new TcpTransportClient(OptionsFor(server.Port), dnsInfoProvider);
            using CancellationTokenSource timeout = Timeout();

            Task<TcpSession> accepting = server.AcceptAsync(timeout.Token);
            byte[] payload = { 1, 2, 3, 0 };

            await target.Send(payload);

            TcpSession session = await accepting;

            Assert.Equal(payload, await session.ReadExactlyAsync(payload.Length, timeout.Token));
            await dnsInfoProvider.Received(1).GetIpAddress(Host);
        }

        [Fact]
        public async Task Send_ConcurrentCallsKeepNullTerminatedFramesIntact()
        {
            const int messageCount = 16;
            using var server = TcpLoopbackServer.Start();
            using var target = new TcpTransportClient(OptionsFor(server.Port), Dns());
            using CancellationTokenSource timeout = Timeout();

            Task<TcpSession> accepting = server.AcceptAsync(timeout.Token);

            await Task.WhenAll(Enumerable.Range(1, messageCount)
                .Select(value => target.Send(new byte[] { (byte)value, 0 })));

            TcpSession session = await accepting;
            byte[] received = await session.ReadExactlyAsync(messageCount * 2, timeout.Token);

            Assert.All(Enumerable.Range(0, messageCount), index => Assert.Equal(0, received[(index * 2) + 1]));
            Assert.Equal(Enumerable.Range(1, messageCount).Select(value => (byte)value),
                received.Where((_, index) => index % 2 == 0).OrderBy(value => value));
        }

        /// <summary>
        /// Both timeouts are optional, and clearing them has to leave the send waiting on the socket
        /// rather than on a timer.
        /// </summary>
        [Fact]
        public async Task Send_WithoutTimeouts_DeliversThePayload()
        {
            using var server = TcpLoopbackServer.Start();
            TcpTransportOptions options = OptionsFor(server.Port);
            options.ConnectTimeout = null;
            options.WriteTimeout = null;

            using var target = new TcpTransportClient(options, Dns());
            using CancellationTokenSource timeout = Timeout();

            Task<TcpSession> accepting = server.AcceptAsync(timeout.Token);
            byte[] payload = { 1, 2, 3, 0 };

            await target.Send(payload);

            TcpSession session = await accepting;

            Assert.Equal(payload, await session.ReadExactlyAsync(payload.Length, timeout.Token));
        }

        /// <summary>
        /// A Graylog host that accepts the connection but never answers must not hold a send forever.
        /// </summary>
        /// <remarks>
        /// 192.0.2.1 is RFC 5737 TEST-NET-1, which is not routed anywhere, so the SYN goes unanswered
        /// and the connect is still outstanding when the timeout elapses.
        /// </remarks>
        [Fact]
        public async Task Send_WhenTheConnectionTimesOut_Throws()
        {
            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            using var target = new TcpTransportClient(new TcpTransportOptions
            {
                Host = "192.0.2.1",
                Port = 12201,
                ConnectTimeout = TimeSpan.FromMilliseconds(1)
            }, dnsInfoProvider);

            TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() => target.Send(new byte[] { 1, 0 }));

            Assert.Equal("The TCP connection timed out.", exception.Message);
            await dnsInfoProvider.DidNotReceive().GetIpAddress(Arg.Any<string>());
        }

        [Fact]
        public async Task Send_DeliversThePayloadToAnIpv6Endpoint()
        {
            if (!Socket.OSSupportsIPv6)
            {
                return;
            }

            using var server = TcpLoopbackServer.Start(IPAddress.IPv6Loopback);
            using var target = new TcpTransportClient(OptionsFor(server.Port), Dns(IPAddress.IPv6Loopback));
            using CancellationTokenSource timeout = Timeout();

            Task<TcpSession> accepting = server.AcceptAsync(timeout.Token);
            byte[] payload = { 1, 2, 3, 0 };

            await target.Send(payload);

            TcpSession session = await accepting;

            Assert.Equal(payload, await session.ReadExactlyAsync(payload.Length, timeout.Token));
        }

        private static TcpTransportOptions OptionsFor(int port)
        {
            return new TcpTransportOptions
            {
                Host = Host,
                Port = port
            };
        }

        private static IDnsInfoProvider Dns(IPAddress? address = null)
        {
            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress(Host).Returns(Task.FromResult<IPAddress?>(address ?? IPAddress.Loopback));

            return dnsInfoProvider;
        }

        private static CancellationTokenSource Timeout()
        {
            CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            return timeout;
        }
    }
}
