using AutoFixture;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.Transport.Udp
{
    using Scarlet.Serilog.Sinks.Graylog.Core.Transport;

    public class UdpTransportClientFixture
    {
        [Fact]
        public async Task TrySendSomeData()
        {
            var fixture = new Fixture();
            var bytes = fixture.CreateMany<byte>(128);

            var client = new UdpTransportClient(new GraylogSinkOptions
            {
                HostnameOrAddress = "127.0.0.1",
                Port = 3128
            }, new DnsWrapper());

            await client.Send(bytes.ToArray());
        }
    }
}
