using AutoFixture;
using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.Helpers
{
    public class MessageIdGeneratorFixture
    {
        private readonly Fixture _fixture;

        public MessageIdGeneratorFixture()
        {
            _fixture = new Fixture();
        }

        [Fact]
        public void GenerateMessageId_ReturnsTheEightBytesTheChunkHeaderReserves()
        {
            byte[] given = _fixture.CreateMany<byte>(10).ToArray();
            var target = new RandomMessageIdGenerator();

            byte[] actual = target.GenerateMessageId(given);

            Assert.Equal(8, actual.Length);
        }

        /// <summary>
        /// Graylog groups the chunks of one message by this id and gives up on a partial message after
        /// five seconds, so two messages chunked close together must never share one - their chunks
        /// would be merged and both messages lost.
        /// </summary>
        [Fact]
        public void GenerateMessageId_EveryIdIsDistinct()
        {
            byte[] given = _fixture.CreateMany<byte>(10).ToArray();
            var target = new RandomMessageIdGenerator();

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < 10000; i++)
            {
                ids.Add(Convert.ToBase64String(target.GenerateMessageId(given)));
            }

            Assert.Equal(10000, ids.Count);
        }

        /// <summary>
        /// The id must not be derived from the payload: two identical messages in flight at once would
        /// otherwise collide, which is exactly the case a content hash cannot distinguish.
        /// </summary>
        [Fact]
        public void GenerateMessageId_ForTheSamePayload_StillDiffers()
        {
            byte[] given = _fixture.CreateMany<byte>(10).ToArray();
            var target = new RandomMessageIdGenerator();

            Assert.NotEqual(target.GenerateMessageId(given), target.GenerateMessageId(given));
        }
    }
}
