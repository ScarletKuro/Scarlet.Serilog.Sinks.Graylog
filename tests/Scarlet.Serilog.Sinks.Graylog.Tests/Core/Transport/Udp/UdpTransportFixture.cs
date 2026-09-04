using NSubstitute;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.Transport.Udp
{
    public class UdpTransportFixture
    {
        private const int MaxDatagram = 8192;

        /// <summary>
        /// A payload that fits in one datagram is sent as it is, and the chunk converter is not
        /// involved at all - asking it for a one-element list was an allocation on every single event.
        /// </summary>
        [Fact]
        public async Task Send_WithGzip_CompressesAndSendsOneDatagram()
        {
            var transportClient = Substitute.For<ITransportClient>();
            var dataToChunkConverter = Substitute.For<IDataToChunkConverter>();
            var options = new UdpTransportOptions { MaximumDatagramSize = MaxDatagram };
            const string message = "{\"short_message\":\"Tere, maailm!\"}";

            byte[]? sent = null;
            _ = transportClient.Send(Arg.Do<ReadOnlyMemory<byte>>(value => sent = value.ToArray()));

            UdpTransport target = new(transportClient, dataToChunkConverter, options);

            await target.Send(message);

            Assert.NotNull(sent);
            // RFC 1952 header: the magic bytes and the compression method, all platform-independent.
            Assert.Equal(0x1f, sent[0]);
            Assert.Equal(0x8b, sent[1]);
            Assert.Equal(8, sent[2]);
            Assert.Equal(message, Decompress(sent));

            dataToChunkConverter.DidNotReceiveWithAnyArgs().ConvertToChunks(default);
        }

        /// <summary>
        /// Without compression the payload reaches the wire as plain UTF-8, not gzip.
        /// </summary>
        [Fact]
        public async Task Send_WithoutCompression_SendsThePlainUtf8Payload()
        {
            var transportClient = Substitute.For<ITransportClient>();
            var dataToChunkConverter = Substitute.For<IDataToChunkConverter>();
            var options = new UdpTransportOptions { Compression = UdpCompression.None, MaximumDatagramSize = MaxDatagram };
            const string message = "GELF message";

            UdpTransport target = new(transportClient, dataToChunkConverter, options);

            await target.Send(message);

            byte[] expected = Encoding.UTF8.GetBytes(message);

            await transportClient.Received(1).Send(Arg.Is<ReadOnlyMemory<byte>>(value => value.ToArray().SequenceEqual(expected)));
            dataToChunkConverter.DidNotReceiveWithAnyArgs().ConvertToChunks(default);
        }

        /// <summary>
        /// A payload too large for one datagram goes to the chunk converter, and every chunk it
        /// produces is sent.
        /// </summary>
        [Fact]
        public async Task Send_WhenThePayloadNeedsSplitting_SendsEveryChunk()
        {
            var transportClient = Substitute.For<ITransportClient>();
            var dataToChunkConverter = Substitute.For<IDataToChunkConverter>();
            var options = new UdpTransportOptions { Compression = UdpCompression.None, MaximumDatagramSize = MaxDatagram };

            string message = new string('x', MaxDatagram + 1);
            var chunks = new List<byte[]> { new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 } };

            dataToChunkConverter.ConvertToChunks(Arg.Any<ReadOnlyMemory<byte>>()).Returns(chunks);

            UdpTransport target = new(transportClient, dataToChunkConverter, options);

            await target.Send(message);

            dataToChunkConverter.Received(1).ConvertToChunks(
                Arg.Is<ReadOnlyMemory<byte>>(value => value.Length == MaxDatagram + 1));

            foreach (byte[] chunk in chunks)
            {
                await transportClient.Received(1).Send(Arg.Is<ReadOnlyMemory<byte>>(value => value.ToArray().SequenceEqual(chunk)));
            }
        }

        /// <summary>
        /// The transport owns its client, so disposing the sink has to release the socket.
        /// </summary>
        [Fact]
        public void Dispose_DisposesTheTransportClient()
        {
            var transportClient = Substitute.For<ITransportClient>();
            var dataToChunkConverter = Substitute.For<IDataToChunkConverter>();
            UdpTransport target = new(transportClient, dataToChunkConverter, new UdpTransportOptions());

            target.Dispose();

            transportClient.Received(1).Dispose();
        }

        [Fact]
        public void Dispose_CalledTwice_DisposesTheTransportClientAgainWithoutFailing()
        {
            var transportClient = Substitute.For<ITransportClient>();
            var dataToChunkConverter = Substitute.For<IDataToChunkConverter>();
            UdpTransport target = new(transportClient, dataToChunkConverter, new UdpTransportOptions());

            target.Dispose();
            target.Dispose();

            transportClient.Received(2).Dispose();
        }

        private static string Decompress(byte[] compressed)
        {
            using var input = new MemoryStream(compressed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            gzip.CopyTo(output);

            return Encoding.UTF8.GetString(output.ToArray());
        }
    }
}
