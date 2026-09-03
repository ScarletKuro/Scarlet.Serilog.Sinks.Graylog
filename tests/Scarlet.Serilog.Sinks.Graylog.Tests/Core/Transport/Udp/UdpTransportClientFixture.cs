using AutoFixture;
using NSubstitute;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.Transport.Udp
{
    public class UdpTransportClientFixture
    {
        [Fact]
        public async Task Send_WithoutAHostname_ThrowsAClearError()
        {
            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            using var target = new UdpTransportClient(new UdpTransportOptions(), dnsInfoProvider);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => target.Send(new byte[] { 1 }));

            Assert.Equal("The UDP host value must be set.", exception.Message);
            await dnsInfoProvider.DidNotReceive().GetIpAddress(Arg.Any<string>());
        }

        /// <summary>
        /// The test this replaced sent to a port nothing was listening on and asserted nothing, which
        /// a UDP send can never fail. Binding a listener on loopback keeps it hermetic and lets the
        /// datagram itself be asserted.
        /// </summary>
        [Fact]
        public async Task Send_DeliversThePayloadToTheResolvedEndpoint()
        {
            using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            int port = Assert.IsType<IPEndPoint>(listener.Client.LocalEndPoint).Port;

            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress("graylog.example.org").Returns(Task.FromResult<IPAddress?>(IPAddress.Loopback));

            byte[] payload = new Fixture().CreateMany<byte>(128).ToArray();

            using var target = new UdpTransportClient(new UdpTransportOptions
            {
                Host = "graylog.example.org",
                Port = port
            }, dnsInfoProvider);

            // Bounded so a datagram that never arrives fails the test instead of hanging it.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            ValueTask<UdpReceiveResult> receiving = listener.ReceiveAsync(timeout.Token);

            await target.Send(payload);

            UdpReceiveResult received = await receiving;

            Assert.Equal(payload, received.Buffer);
        }

        [Fact]
        public async Task Send_ResolvesTheHostnameOnceAndThenReusesTheEndpoint()
        {
            using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            int port = Assert.IsType<IPEndPoint>(listener.Client.LocalEndPoint).Port;

            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress("graylog.example.org").Returns(Task.FromResult<IPAddress?>(IPAddress.Loopback));

            using var target = new UdpTransportClient(new UdpTransportOptions
            {
                Host = "graylog.example.org",
                Port = port
            }, dnsInfoProvider);

            await target.Send(new byte[] { 1 });
            await target.Send(new byte[] { 2 });

            await dnsInfoProvider.Received(1).GetIpAddress("graylog.example.org");
        }

        [Fact]
        public async Task Send_DeliversThePayloadToAnIpv6Endpoint()
        {
            if (!Socket.OSSupportsIPv6)
            {
                return;
            }

            using var listener = new UdpClient(new IPEndPoint(IPAddress.IPv6Loopback, 0));
            int port = Assert.IsType<IPEndPoint>(listener.Client.LocalEndPoint).Port;
            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress("graylog.example.org").Returns(Task.FromResult<IPAddress?>(IPAddress.IPv6Loopback));
            byte[] payload = new byte[] { 1, 2, 3 };

            using var target = new UdpTransportClient(new UdpTransportOptions
            {
                Host = "graylog.example.org",
                Port = port
            }, dnsInfoProvider);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            ValueTask<UdpReceiveResult> receiving = listener.ReceiveAsync(timeout.Token);

            await target.Send(payload);

            UdpReceiveResult received = await receiving;
            Assert.Equal(payload, received.Buffer);
        }

        /// <summary>
        /// An IP literal is not a name, so it must never reach the resolver.
        /// </summary>
        [Fact]
        public async Task Send_WhenTheHostIsAnIpLiteral_NeverResolves()
        {
            using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            int port = Assert.IsType<IPEndPoint>(listener.Client.LocalEndPoint).Port;

            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();

            using var target = new UdpTransportClient(new UdpTransportOptions
            {
                Host = "127.0.0.1",
                Port = port
            }, dnsInfoProvider);

            await target.Send(new byte[] { 1 });

            await dnsInfoProvider.DidNotReceive().GetIpAddress(Arg.Any<string>());
        }

        /// <summary>
        /// UDP has no connection to fail, so the resolved address used to be kept for the life of the
        /// sink - a host that moved was written to at its old address forever.
        /// </summary>
        [Fact]
        public async Task Send_WhenTheRefreshIntervalElapses_ResolvesTheHostAgain()
        {
            using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            int port = Assert.IsType<IPEndPoint>(listener.Client.LocalEndPoint).Port;

            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress("graylog.example.org").Returns(Task.FromResult<IPAddress?>(IPAddress.Loopback));

            long clock = 0;
            using var target = new UdpTransportClient(new UdpTransportOptions
            {
                Host = "graylog.example.org",
                Port = port,
                DnsRefreshInterval = TimeSpan.FromSeconds(30)
            }, dnsInfoProvider, () => clock);

            await target.Send(new byte[] { 1 });
            await target.Send(new byte[] { 2 });

            await dnsInfoProvider.Received(1).GetIpAddress("graylog.example.org");

            clock += 31 * Stopwatch.Frequency;

            await target.Send(new byte[] { 3 });

            await dnsInfoProvider.Received(2).GetIpAddress("graylog.example.org");
        }

        [Fact]
        public async Task Send_WhenTheRefreshIntervalIsNull_ResolvesOnlyOnce()
        {
            using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            int port = Assert.IsType<IPEndPoint>(listener.Client.LocalEndPoint).Port;

            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress("graylog.example.org").Returns(Task.FromResult<IPAddress?>(IPAddress.Loopback));

            long clock = 0;
            using var target = new UdpTransportClient(new UdpTransportOptions
            {
                Host = "graylog.example.org",
                Port = port,
                DnsRefreshInterval = null
            }, dnsInfoProvider, () => clock);

            await target.Send(new byte[] { 1 });

            clock += 3600 * Stopwatch.Frequency;

            await target.Send(new byte[] { 2 });

            await dnsInfoProvider.Received(1).GetIpAddress("graylog.example.org");
        }

        /// <summary>
        /// A refresh that fails must not cost events - the last address that worked stays in use.
        /// </summary>
        [Fact]
        public async Task Send_WhenARefreshFails_KeepsDeliveringToThePreviousEndpoint()
        {
            using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            int port = Assert.IsType<IPEndPoint>(listener.Client.LocalEndPoint).Port;

            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress("graylog.example.org").Returns(
                _ => Task.FromResult<IPAddress?>(IPAddress.Loopback),
                _ => Task.FromException<IPAddress?>(new SocketException()));

            long clock = 0;
            using var target = new UdpTransportClient(new UdpTransportOptions
            {
                Host = "graylog.example.org",
                Port = port,
                DnsRefreshInterval = TimeSpan.FromSeconds(30)
            }, dnsInfoProvider, () => clock);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            await target.Send(new byte[] { 1 });

            // Drained, so the assertion below cannot pass on the datagram from the first send.
            await listener.ReceiveAsync(timeout.Token);

            clock += 31 * Stopwatch.Frequency;

            ValueTask<UdpReceiveResult> receiving = listener.ReceiveAsync(timeout.Token);

            byte[] payload = { 2, 3 };
            await target.Send(payload);

            UdpReceiveResult received = await receiving;

            Assert.Equal(payload, received.Buffer);
        }

        /// <summary>
        /// A socket is bound to one address family, so a host that moves from IPv4 to IPv6 needs a new
        /// client - sending to an IPv6 endpoint through the old IPv4 socket throws.
        /// </summary>
        [Fact]
        public async Task Send_WhenTheAddressFamilyChanges_RecreatesTheClient()
        {
            if (!Socket.OSSupportsIPv6)
            {
                return;
            }

            using var v4Listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            using var v6Listener = new UdpClient(new IPEndPoint(IPAddress.IPv6Loopback, 0));

            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress("graylog.example.org").Returns(
                _ => Task.FromResult<IPAddress?>(IPAddress.Loopback),
                _ => Task.FromResult<IPAddress?>(IPAddress.IPv6Loopback));

            long clock = 0;
            var options = new UdpTransportOptions
            {
                Host = "graylog.example.org",
                Port = Assert.IsType<IPEndPoint>(v4Listener.Client.LocalEndPoint).Port,
                DnsRefreshInterval = TimeSpan.FromSeconds(30)
            };

            using var target = new UdpTransportClient(options, dnsInfoProvider, () => clock);

            await target.Send(new byte[] { 1 });

            options.Port = Assert.IsType<IPEndPoint>(v6Listener.Client.LocalEndPoint).Port;
            clock += 31 * Stopwatch.Frequency;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            ValueTask<UdpReceiveResult> receiving = v6Listener.ReceiveAsync(timeout.Token);

            byte[] payload = { 4, 5, 6 };
            await target.Send(payload);

            UdpReceiveResult received = await receiving;

            Assert.Equal(payload, received.Buffer);
        }
    }
}
