using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Text;
using Scarlet.Serilog.Sinks.Graylog.Core.Extensions;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.Extensions
{
    public class StringExtensionsFixture
    {
        [Fact]
        public void WhenCompressMessage_ThenResultShouldBeExpected()
        {
            var giwen = "Some string";
            var expected = new byte[]
            {
                31,139,8,0,0,0,0,0,0,10,11,206,207,77,85,40,46,41,202,204,75,7,0,142,183,209,127,11,0,0,0
            };

            byte[] actual = giwen.ToGzip();
            Assert.Equal(expected, actual);
        }

        /// <summary>
        /// The uncompressed bytes go through a buffer rented from the shared pool, which is handed
        /// back dirty and comes back oversized. Only the bytes the payload actually occupies may
        /// reach the compressor.
        /// </summary>
        [Fact]
        public void ToGzip_WithADirtyPooledBuffer_CompressesOnlyThePayload()
        {
            string given = new string('x', 5000) + " end";

            // Dirty the buffer the rent inside ToGzip is about to be handed.
            byte[] dirty = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetByteCount(given));
            dirty.AsSpan().Fill(0xAB);
            ArrayPool<byte>.Shared.Return(dirty);

            byte[] compressed = given.ToGzip();

            using var input = new MemoryStream(compressed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);

            Assert.Equal(given, Encoding.UTF8.GetString(output.ToArray()));
        }

        [Theory]
        [InlineData("SomeTestString", "Some", 4)]
        [InlineData("SomeTestString", "SomeTest", 8)]
        [InlineData("SomeTestString", "SomeTestString", 200)]
        public void WhenShortMessage_ThenResultShouldBeExpected(string given, string expected, int length)
        {
            var actual = given.Truncate(length);

            Assert.Equal(expected, actual);
        }
    }
}
