using AutoFixture;
using NSubstitute;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Http;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.Transport.Http
{
    public class HttpTransportFixture
    {
        private readonly Fixture _fixture;

        public HttpTransportFixture()
        {
            _fixture = new Fixture();
        }

        [Fact]
        public async Task WhenCallSend_ThenCallSendWithoutAnyChanges()
        {
            var transportClient = Substitute.For<ITransportClient>();

            var target = new HttpTransport(transportClient);

            var payload = _fixture.Create<string>();
            byte[] expected = Encoding.UTF8.GetBytes(payload);

            await target.Send(payload);

            await transportClient.Received(1).Send(Arg.Is<ReadOnlyMemory<byte>>(value => value.ToArray().SequenceEqual(expected)));
        }
    }
}
