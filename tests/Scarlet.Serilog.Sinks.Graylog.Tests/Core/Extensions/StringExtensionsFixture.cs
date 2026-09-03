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
        /// <summary>
        /// The contract is a gzip stream Graylog can inflate, not one exact byte sequence.
        /// </summary>
        /// <remarks>
        /// This used to assert a hard-coded array captured on Windows. RFC 1952 reserves header byte 9
        /// for the operating system the stream was produced on, so .NET writes 10 there on Windows and
        /// 3 on Linux - the assertion could only ever pass on one of them, and it started failing when
        /// CI moved to ubuntu-latest.
        /// </remarks>
        [Fact]
        public void WhenCompressMessage_ThenResultShouldBeExpected()
        {
            const string given = "Some string";

            byte[] actual = given.ToGzip();

            // RFC 1952 header: the magic bytes and the compression method, all platform-independent.
            Assert.Equal(0x1f, actual[0]);
            Assert.Equal(0x8b, actual[1]);
            Assert.Equal(8, actual[2]);

            Assert.Equal(given, Decompress(actual));
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

            Assert.Equal(given, Decompress(compressed));
        }

        [Fact]
        public void ToGzip_WithMultiByteCharacters_RoundTrips()
        {
            // GetByteCount and GetBytes have to agree about the buffer size for anything outside ASCII.
            const string given = "Ω 日本語 🚀 emoji and accents: café";

            Assert.Equal(given, Decompress(given.ToGzip()));
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

        /// <summary>
        /// What an uncompressed UDP datagram carries.
        /// </summary>
        [Theory]
        [InlineData("GELF message")]
        [InlineData("Ω 日本語 🚀")]
        [InlineData("")]
        public void ToByteArray_ReturnsTheUtf8Bytes(string given)
        {
            byte[] actual = given.ToByteArray();

            Assert.Equal(Encoding.UTF8.GetBytes(given), actual);
        }

        [Fact]
        public void Expand_ReplacesEnvironmentVariables()
        {
            const string variable = "SCARLET_GRAYLOG_EXPAND_TEST";
            Environment.SetEnvironmentVariable(variable, "graylog.example.org");

            try
            {
                Assert.Equal("graylog.example.org", $"%{variable}%".Expand());
            } finally
            {
                Environment.SetEnvironmentVariable(variable, null);
            }
        }

        [Fact]
        public void Expand_WithoutAnyVariable_ReturnsTheSourceUnchanged()
        {
            Assert.Equal("graylog.example.org", "graylog.example.org".Expand());
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
