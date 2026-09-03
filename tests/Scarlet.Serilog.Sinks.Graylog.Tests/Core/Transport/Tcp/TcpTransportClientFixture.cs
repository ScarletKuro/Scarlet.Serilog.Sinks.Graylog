using NSubstitute;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Tcp;
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
        [Fact]
        public async Task Send_AfterAConnectionFailure_ReconnectsWithANewClient()
        {
            int port;
            using (var portReservation = new TcpListener(IPAddress.Loopback, 0))
            {
                portReservation.Start();
                port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
            }

            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress("graylog.example.org").Returns(Task.FromResult<IPAddress?>(IPAddress.Loopback));
            using var target = new TcpTransportClient(new TcpTransportOptions
            {
                Host = "graylog.example.org",
                Port = port
            }, dnsInfoProvider);

            await Assert.ThrowsAnyAsync<SocketException>(() => target.Send(new byte[] { 1 }));

            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            ValueTask<TcpClient> accepting = listener.AcceptTcpClientAsync(timeout.Token);

            byte[] payload = { 2, 0 };
            await target.Send(payload);

            using TcpClient connection = await accepting;
            byte[] received = new byte[payload.Length];
            await connection.GetStream().ReadExactlyAsync(received, timeout.Token);

            Assert.Equal(payload, received);
        }

        [Fact]
        public async Task Send_WhenEndpointCannotBeResolved_ThrowsAClearError()
        {
            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress("graylog.example.org").Returns(Task.FromResult<IPAddress?>(null));

            using var target = new TcpTransportClient(new TcpTransportOptions
            {
                Host = "graylog.example.org"
            }, dnsInfoProvider);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => target.Send(new byte[] { 1 }));

            Assert.Equal("The Graylog endpoint could not be resolved.", exception.Message);
        }

        [Fact]
        public async Task Send_DeliversThePayloadToTheResolvedEndpoint()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress("graylog.example.org").Returns(Task.FromResult<IPAddress?>(IPAddress.Loopback));
            byte[] payload = { 1, 2, 3, 0 };

            using var target = new TcpTransportClient(new TcpTransportOptions
            {
                Host = "graylog.example.org",
                Port = port
            }, dnsInfoProvider);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            ValueTask<TcpClient> accepting = listener.AcceptTcpClientAsync(timeout.Token);

            await target.Send(payload);

            using TcpClient connection = await accepting;
            NetworkStream stream = connection.GetStream();
            byte[] received = new byte[payload.Length];
            await stream.ReadExactlyAsync(received, timeout.Token);

            Assert.Equal(payload, received);
            await dnsInfoProvider.Received(1).GetIpAddress("graylog.example.org");
        }

        [Fact]
        public async Task Send_ConcurrentCallsKeepNullTerminatedFramesIntact()
        {
            const int messageCount = 16;
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress("graylog.example.org").Returns(Task.FromResult<IPAddress?>(IPAddress.Loopback));

            using var target = new TcpTransportClient(new TcpTransportOptions
            {
                Host = "graylog.example.org",
                Port = port
            }, dnsInfoProvider);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            ValueTask<TcpClient> accepting = listener.AcceptTcpClientAsync(timeout.Token);

            await Task.WhenAll(Enumerable.Range(1, messageCount)
                .Select(value => target.Send(new byte[] { (byte)value, 0 })));

            using TcpClient connection = await accepting;
            byte[] received = new byte[messageCount * 2];
            await connection.GetStream().ReadExactlyAsync(received, timeout.Token);

            Assert.All(Enumerable.Range(0, messageCount), index => Assert.Equal(0, received[(index * 2) + 1]));
            Assert.Equal(Enumerable.Range(1, messageCount).Select(value => (byte)value),
                received.Where((_, index) => index % 2 == 0).OrderBy(value => value));
        }

        [Fact]
        public async Task Send_DeliversThePayloadToAnIpv6Endpoint()
        {
            if (!Socket.OSSupportsIPv6)
            {
                return;
            }

            using var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress("graylog.example.org").Returns(Task.FromResult<IPAddress?>(IPAddress.IPv6Loopback));
            byte[] payload = { 1, 2, 3, 0 };

            using var target = new TcpTransportClient(new TcpTransportOptions
            {
                Host = "graylog.example.org",
                Port = port
            }, dnsInfoProvider);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            ValueTask<TcpClient> accepting = listener.AcceptTcpClientAsync(timeout.Token);

            await target.Send(payload);

            using TcpClient connection = await accepting;
            byte[] received = new byte[payload.Length];
            await connection.GetStream().ReadExactlyAsync(received, timeout.Token);

            Assert.Equal(payload, received);
        }
    }
}
