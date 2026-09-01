using AutoFixture;
using NSubstitute;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Http;
using System.Threading.Tasks;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Tests.Transport.Http
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
            var transportClient = Substitute.For<ITransportClient<string>>();

            var target = new HttpTransport(transportClient);

            var payload = _fixture.Create<string>();

            await target.Send(payload);

            await transportClient.Received(1).Send(payload);
        }
    }
}
