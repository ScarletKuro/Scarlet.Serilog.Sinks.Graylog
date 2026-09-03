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
            var options = new UdpTransportOptions();

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

        /// <summary>
        /// Without compression the payload reaches the chunk converter as plain UTF-8, not gzip.
        /// </summary>
        [Fact]
        public void Send_WithoutCompression_ChunksThePlainUtf8Payload()
        {
            var transportClient = Substitute.For<ITransportClient<byte[]>>();
            var dataToChunkConverter = Substitute.For<IDataToChunkConverter>();
            var options = new UdpTransportOptions { Compression = UdpCompression.None };
            const string message = "GELF message";
            byte[] expected = message.ToByteArray();
            dataToChunkConverter.ConvertToChunks(Arg.Any<byte[]>()).Returns(new List<byte[]> { expected });

            UdpTransport target = new(transportClient, dataToChunkConverter, options);

            target.Send(message);

            dataToChunkConverter.Received(1).ConvertToChunks(Arg.Is<byte[]>(value => value.SequenceEqual(expected)));
            transportClient.Received(1).Send(Arg.Is<byte[]>(value => value.SequenceEqual(expected)));
        }

        /// <summary>
        /// The transport owns its client, so disposing the sink has to release the socket.
        /// </summary>
        [Fact]
        public void Dispose_DisposesTheTransportClient()
        {
            var transportClient = Substitute.For<ITransportClient<byte[]>>();
            var dataToChunkConverter = Substitute.For<IDataToChunkConverter>();
            UdpTransport target = new(transportClient, dataToChunkConverter, new UdpTransportOptions());

            target.Dispose();

            transportClient.Received(1).Dispose();
        }

        [Fact]
        public void Dispose_CalledTwice_DisposesTheTransportClientAgainWithoutFailing()
        {
            var transportClient = Substitute.For<ITransportClient<byte[]>>();
            var dataToChunkConverter = Substitute.For<IDataToChunkConverter>();
            UdpTransport target = new(transportClient, dataToChunkConverter, new UdpTransportOptions());

            target.Dispose();
            target.Dispose();

            transportClient.Received(2).Dispose();
        }
    }
}
