using AutoFixture;
using NSubstitute;
using Scarlet.Serilog.Sinks.Graylog.Core.Extensions;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.Transport.Udp
{
    public class UdpTransportFixture
    {
        [Fact]
        public void WhenSend_ThenCallMethods()
        {
            var transportClient = Substitute.For<ITransportClient<byte[]>>();
            var dataToChunkConverter = Substitute.For<IDataToChunkConverter>();
            var options = new GraylogSinkOptions();

            var fixture = new Fixture();

            var stringData = fixture.Create<string>();

            byte[] data = stringData.ToGzip();

            List<byte[]> chunks = fixture.CreateMany<byte[]>(3).ToList();

            dataToChunkConverter.ConvertToChunks(Arg.Is<byte[]>(value => value.SequenceEqual(data))).Returns(chunks);

            UdpTransport target = new(transportClient, dataToChunkConverter, options);

            target.Send(stringData);

            dataToChunkConverter.Received(1).ConvertToChunks(Arg.Is<byte[]>(value => value.SequenceEqual(data)));

            foreach (byte[] chunk in chunks)
            {
                transportClient.Received(1).Send(Arg.Is<byte[]>(value => value.SequenceEqual(chunk)));
            }

        }
    }
}
