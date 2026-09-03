using AutoFixture;
using NSubstitute;
using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.Transport.Udp
{
    public class DataToChunkConverterFixture
    {
        private readonly ChunkSettings _settings;
        private readonly Fixture _fixture;
        private readonly IMessageIdGenerator _messageIdGenerator;

        private const int MaxDatagram = 8192;
        private const int MaxChunkPayload = MaxDatagram - ChunkSettings.PrefixSize;
        private const int MaxChunks = ChunkSettings.MaxNumberOfChunksAllowed;

        public DataToChunkConverterFixture()
        {
            _settings = new ChunkSettings(MaxDatagram);
            _fixture = new Fixture();
            _messageIdGenerator = Substitute.For<IMessageIdGenerator>();
        }

        /// <summary>
        /// The chunk count is a ceiling division. It used to be <c>length / chunkSize + 1</c>, which
        /// appended an empty chunk on every exact multiple and rejected a payload that needed
        /// exactly the 128 chunks GELF allows. Every case here sits on one of those boundaries.
        /// </summary>
        [Theory]
        // Fits in one datagram, so it goes out verbatim with no chunk header.
        [InlineData(MaxDatagram, 1)]
        [InlineData(MaxDatagram + 1, 2)]
        // Exact multiples: the extra empty chunk the old formula produced.
        [InlineData(2 * MaxChunkPayload, 2)]
        [InlineData(3 * MaxChunkPayload, 3)]
        // The ceiling itself, which the old formula counted as 129 and threw on.
        [InlineData(MaxChunks * MaxChunkPayload, MaxChunks)]
        public void ConvertToChunks_AtAChunkBoundary_ProducesTheExactChunkCount(int payloadLength, int expectedChunks)
        {
            var target = new DataToChunkConverter(_settings, StubGenerator());

            IList<byte[]> actual = target.ConvertToChunks(new byte[payloadLength]);

            Assert.Equal(expectedChunks, actual.Count);
            // No chunk may be header-only: an empty chunk is a wasted datagram Graylog still waits for.
            Assert.All(actual, chunk => Assert.True(chunk.Length > ChunkSettings.PrefixSize || actual.Count == 1));
            Assert.Equal(payloadLength, actual.Count == 1
                ? actual[0].Length
                : actual.Sum(chunk => chunk.Length - ChunkSettings.PrefixSize));
        }

        [Fact]
        public void ConvertToChunks_OneByteBeyondTheChunkCeiling_Throws()
        {
            var target = new DataToChunkConverter(_settings, StubGenerator());

            Assert.Throws<ArgumentException>(() => target.ConvertToChunks(new byte[(MaxChunks * MaxChunkPayload) + 1]));
        }

        private IMessageIdGenerator StubGenerator()
        {
            _messageIdGenerator.GenerateMessageId(Arg.Any<byte[]>()).Returns(new byte[8]);

            return _messageIdGenerator;
        }

        [Fact]
        public void WhenConvertToChunkWithSmallData_ThenReturnsOneChunk()
        {
            var target = new DataToChunkConverter(_settings, _messageIdGenerator);

            byte[] data = _fixture.CreateMany<byte>(1000).ToArray();
            IList<byte[]> actual = target.ConvertToChunks(data);

            var expected = new List<byte[]>
            {
                data
            };

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void WhenChunksWasTooMany_ThenThrowsException()
        {
            byte[] data = new byte[10000000];

            var target = new DataToChunkConverter(_settings, _messageIdGenerator);

            Assert.Throws<ArgumentException>(() => target.ConvertToChunks(data));
        }

        [Fact]
        public void WhenMessageIsLong_ThenSplitItToChunks()
        {
            byte[] data = new byte[100000];

            var messageId = _fixture.CreateMany<byte>(8).ToArray();

            _messageIdGenerator.GenerateMessageId(data).Returns(messageId);

            var target = new DataToChunkConverter(_settings, _messageIdGenerator);

            var actual = target.ConvertToChunks(data);


            Assert.True(actual.Count == 13);

            for (int i = 0; i < actual.Count; i++)
            {
                Assert.Equal(new byte[] { 0x1e, 0x0f }, actual[i].Take(2).ToArray());
                Assert.Equal(messageId, actual[i].Skip(2).Take(8).ToArray());
                Assert.Equal((byte)i, actual[i].Skip(10).First());
                Assert.Equal(13, actual[i].Skip(11).First());
                Assert.True(actual[i].Skip(12).All(c => c == 0));
            }
        }
    }
}
