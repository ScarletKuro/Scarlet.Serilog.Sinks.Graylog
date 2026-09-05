using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using System;
using System.Collections.Generic;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.Helpers
{
    public class SecureRandomFixture
    {
        [Fact]
        public void NextBytes_ReturnsTheRequestedLength()
        {
            byte[] actual = SecureRandom.NextBytes(8);

            Assert.Equal(8, actual.Length);
        }

        /// <summary>
        /// Graylog groups the chunks of one message by this id and gives up on a partial message after
        /// five seconds, so two messages chunked close together must never share one - their chunks
        /// would be merged and both messages lost.
        /// </summary>
        [Fact]
        public void NextBytes_EveryCallIsDistinct()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < 10000; i++)
            {
                ids.Add(Convert.ToBase64String(SecureRandom.NextBytes(8)));
            }

            Assert.Equal(10000, ids.Count);
        }
    }
}
