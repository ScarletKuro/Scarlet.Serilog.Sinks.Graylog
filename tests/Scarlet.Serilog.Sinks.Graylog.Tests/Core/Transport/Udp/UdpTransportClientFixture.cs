using AutoFixture;
using NSubstitute;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp;
using Scarlet.Serilog.Sinks.Graylog.Tests.Fakes;
using Serilog.Debugging;
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
    [Collection(SelfLogCollection.Name)]
    public class UdpTransportClientFixture
    {
        private const string Host = "graylog.example.org";

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
        /// With no address ever resolved there is nothing to fall back to, so the send has to fail
        /// rather than drop the event silently.
        /// </summary>
        [Fact]
        public async Task Send_WhenTheHostNeverResolved_ThrowsAndReportsToSelfLog()
        {
            const string host = "scarlet-graylog-unresolvable.example.org";
            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress(host).Returns(Task.FromResult<IPAddress?>(null));
            using var target = new UdpTransportClient(new UdpTransportOptions { Host = host }, dnsInfoProvider);

            int reported = 0;

            // SelfLog is global and other classes run in parallel, so react only to this host.
            SelfLog.Enable(message =>
            {
                if (message.Contains("did not resolve to a usable address"))
                {
                    Interlocked.Increment(ref reported);
                }
            });

            try
            {
                InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => target.Send(new byte[] { 1 }));

                Assert.Equal("The Graylog endpoint could not be resolved.", exception.Message);
                Assert.Equal(1, Volatile.Read(ref reported));
            }
            finally
            {
                SelfLog.Disable();
            }
        }

        /// <summary>
        /// A resolver that throws is the same situation as one that resolved nothing.
        /// </summary>
        [Fact]
        public async Task Send_WhenTheResolverThrowsAndNothingWasResolvedBefore_Throws()
        {
            const string host = "scarlet-graylog-throwing.example.org";
            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress(host).Returns<Task<IPAddress?>>(_ => throw new SocketException());
            using var target = new UdpTransportClient(new UdpTransportOptions { Host = host }, dnsInfoProvider);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => target.Send(new byte[] { 1 }));

            Assert.Equal("The Graylog endpoint could not be resolved.", exception.Message);
        }

        /// <summary>
        /// The test this replaced sent to a port nothing was listening on and asserted nothing, which
        /// a UDP send can never fail. Binding a listener on loopback keeps it hermetic and lets the
        /// datagram itself be asserted.
        /// </summary>
        [Fact]
        public async Task Send_DeliversThePayloadToTheResolvedEndpoint()
        {
            using UdpLoopbackListener listener = UdpLoopbackListener.Start();
            using var target = new UdpTransportClient(OptionsFor(listener.Port), Dns());
            using CancellationTokenSource timeout = Timeout();

            byte[] payload = new Fixture().CreateMany<byte>(128).ToArray();
            Task<byte[]> receiving = listener.ReceiveAsync(timeout.Token);

            await target.Send(payload);

            Assert.Equal(payload, await receiving);
        }

        [Fact]
        public async Task Send_ResolvesTheHostnameOnceAndThenReusesTheEndpoint()
        {
            using UdpLoopbackListener listener = UdpLoopbackListener.Start();
            IDnsInfoProvider dnsInfoProvider = Dns();
            using var target = new UdpTransportClient(OptionsFor(listener.Port), dnsInfoProvider);

            await target.Send(new byte[] { 1 });
            await target.Send(new byte[] { 2 });

            await dnsInfoProvider.Received(1).GetIpAddress(Host);
        }

        [Fact]
        public async Task Send_DeliversThePayloadToAnIpv6Endpoint()
        {
            if (!Socket.OSSupportsIPv6)
            {
                return;
            }

            using UdpLoopbackListener listener = UdpLoopbackListener.Start(IPAddress.IPv6Loopback);
            using var target = new UdpTransportClient(OptionsFor(listener.Port), Dns(IPAddress.IPv6Loopback));
            using CancellationTokenSource timeout = Timeout();

            byte[] payload = { 1, 2, 3 };
            Task<byte[]> receiving = listener.ReceiveAsync(timeout.Token);

            await target.Send(payload);

            Assert.Equal(payload, await receiving);
        }

        /// <summary>
        /// An IP literal is not a name, so it must never reach the resolver.
        /// </summary>
        [Fact]
        public async Task Send_WhenTheHostIsAnIpLiteral_NeverResolves()
        {
            using UdpLoopbackListener listener = UdpLoopbackListener.Start();
            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();

            using var target = new UdpTransportClient(new UdpTransportOptions
            {
                Host = "127.0.0.1",
                Port = listener.Port
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
            using UdpLoopbackListener listener = UdpLoopbackListener.Start();
            IDnsInfoProvider dnsInfoProvider = Dns();

            long clock = 0;
            using var target = new UdpTransportClient(
                OptionsFor(listener.Port, o => o.DnsRefreshInterval = TimeSpan.FromSeconds(30)),
                dnsInfoProvider,
                () => clock);

            await target.Send(new byte[] { 1 });
            await target.Send(new byte[] { 2 });

            await dnsInfoProvider.Received(1).GetIpAddress(Host);

            clock += 31 * Stopwatch.Frequency;

            await target.Send(new byte[] { 3 });

            await dnsInfoProvider.Received(2).GetIpAddress(Host);
        }

        [Fact]
        public async Task Send_WhenTheRefreshIntervalIsNull_ResolvesOnlyOnce()
        {
            using UdpLoopbackListener listener = UdpLoopbackListener.Start();
            IDnsInfoProvider dnsInfoProvider = Dns();

            long clock = 0;
            using var target = new UdpTransportClient(
                OptionsFor(listener.Port, o => o.DnsRefreshInterval = null),
                dnsInfoProvider,
                () => clock);

            await target.Send(new byte[] { 1 });

            clock += 3600 * Stopwatch.Frequency;

            await target.Send(new byte[] { 2 });

            await dnsInfoProvider.Received(1).GetIpAddress(Host);
        }

        /// <summary>
        /// A refresh that fails must not cost events - the last address that worked stays in use.
        /// </summary>
        [Fact]
        public async Task Send_WhenARefreshFails_KeepsDeliveringToThePreviousEndpoint()
        {
            using UdpLoopbackListener listener = UdpLoopbackListener.Start();

            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress(Host).Returns(
                _ => Task.FromResult<IPAddress?>(IPAddress.Loopback),
                _ => Task.FromException<IPAddress?>(new SocketException()));

            long clock = 0;
            using var target = new UdpTransportClient(
                OptionsFor(listener.Port, o => o.DnsRefreshInterval = TimeSpan.FromSeconds(30)),
                dnsInfoProvider,
                () => clock);

            using CancellationTokenSource timeout = Timeout();

            await target.Send(new byte[] { 1 });

            // Drained, so the assertion below cannot pass on the datagram from the first send.
            await listener.ReceiveAsync(timeout.Token);

            clock += 31 * Stopwatch.Frequency;

            Task<byte[]> receiving = listener.ReceiveAsync(timeout.Token);

            byte[] payload = { 2, 3 };
            await target.Send(payload);

            Assert.Equal(payload, await receiving);
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

            using UdpLoopbackListener v4Listener = UdpLoopbackListener.Start();
            using UdpLoopbackListener v6Listener = UdpLoopbackListener.Start(IPAddress.IPv6Loopback);

            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress(Host).Returns(
                _ => Task.FromResult<IPAddress?>(IPAddress.Loopback),
                _ => Task.FromResult<IPAddress?>(IPAddress.IPv6Loopback));

            long clock = 0;
            UdpTransportOptions options = OptionsFor(v4Listener.Port, o => o.DnsRefreshInterval = TimeSpan.FromSeconds(30));

            using var target = new UdpTransportClient(options, dnsInfoProvider, () => clock);

            await target.Send(new byte[] { 1 });

            options.Port = v6Listener.Port;
            clock += 31 * Stopwatch.Frequency;

            using CancellationTokenSource timeout = Timeout();
            Task<byte[]> receiving = v6Listener.ReceiveAsync(timeout.Token);

            byte[] payload = { 4, 5, 6 };
            await target.Send(payload);

            Assert.Equal(payload, await receiving);
        }

        private static UdpTransportOptions OptionsFor(int port, Action<UdpTransportOptions>? configure = null)
        {
            var options = new UdpTransportOptions
            {
                Host = Host,
                Port = port
            };

            configure?.Invoke(options);

            return options;
        }

        private static IDnsInfoProvider Dns(IPAddress? address = null)
        {
            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress(Host).Returns(Task.FromResult<IPAddress?>(address ?? IPAddress.Loopback));

            return dnsInfoProvider;
        }

        /// <summary>
        /// Bounded so a datagram that never arrives fails the test instead of hanging it.
        /// </summary>
        private static CancellationTokenSource Timeout()
        {
            CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            return timeout;
        }
    }
}
