using NSubstitute;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Tcp;
using System;
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
        public async Task Send_DeliversThePayloadToTheResolvedEndpoint()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var dnsInfoProvider = Substitute.For<IDnsInfoProvider>();
            dnsInfoProvider.GetIpAddress("graylog.example.org").Returns(Task.FromResult<IPAddress?>(IPAddress.Loopback));
            byte[] payload = { 1, 2, 3, 0 };

            using var target = new TcpTransportClient(new GraylogSinkOptions
            {
                HostnameOrAddress = "graylog.example.org",
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
    }
}
