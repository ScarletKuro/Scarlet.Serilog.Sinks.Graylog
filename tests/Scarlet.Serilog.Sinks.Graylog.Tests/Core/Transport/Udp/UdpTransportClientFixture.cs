using AutoFixture;
using NSubstitute;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp;
using System;
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

            using var target = new UdpTransportClient(new GraylogSinkOptions
            {
                HostnameOrAddress = "graylog.example.org",
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

            using var target = new UdpTransportClient(new GraylogSinkOptions
            {
                HostnameOrAddress = "graylog.example.org",
                Port = port
            }, dnsInfoProvider);

            await target.Send(new byte[] { 1 });
            await target.Send(new byte[] { 2 });

            await dnsInfoProvider.Received(1).GetIpAddress("graylog.example.org");
        }
    }
}
