using NSubstitute;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Tcp;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.Transport.Tcp
{
    public class TcpTransportFixture
    {
        [Theory]
        [InlineData("GELF message")]
        [InlineData("Tere, maailm!")]
        public async Task Send_EncodesUtf8AndAppendsANullTerminator(string message)
        {
            var transportClient = Substitute.For<ITransportClient<byte[]>>();
            var target = new TcpTransport(transportClient);

            await target.Send(message);

            byte[] expected = Encoding.UTF8.GetBytes(message + "\0");
            await transportClient.Received(1).Send(Arg.Is<byte[]>((byte[] value) => value.SequenceEqual(expected)));
        }

        /// <summary>
        /// The transport owns its client, so disposing the sink has to close the connection.
        /// </summary>
        [Fact]
        public void Dispose_DisposesTheTransportClient()
        {
            var transportClient = Substitute.For<ITransportClient<byte[]>>();
            var target = new TcpTransport(transportClient);

            target.Dispose();

            transportClient.Received(1).Dispose();
        }

        [Fact]
        public void Dispose_CalledTwice_DisposesTheTransportClientAgainWithoutFailing()
        {
            var transportClient = Substitute.For<ITransportClient<byte[]>>();
            var target = new TcpTransport(transportClient);

            target.Dispose();
            target.Dispose();

            transportClient.Received(2).Dispose();
        }
    }
}
