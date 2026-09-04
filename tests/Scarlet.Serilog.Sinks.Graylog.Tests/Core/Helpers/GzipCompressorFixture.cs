using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.Helpers
{
    /// <summary>
    /// Tests for the gzip step every compressed UDP event goes through.
    /// </summary>
    /// <remarks>
    /// These moved off <c>StringExtensions.ToGzip</c>, which took the payload as a string and
    /// encoded it here. Payloads reach the transports as UTF-8 now, so the compressor takes bytes
    /// and writes into the caller's pooled buffer.
    /// </remarks>
    public class GzipCompressorFixture
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
        public void Compress_ProducesAGzipStreamThatRoundTrips()
        {
            const string given = "Some string";

            byte[] actual = Compress(given);

            // RFC 1952 header: the magic bytes and the compression method, all platform-independent.
            Assert.Equal(0x1f, actual[0]);
            Assert.Equal(0x8b, actual[1]);
            Assert.Equal(8, actual[2]);

            Assert.Equal(given, Decompress(actual));
        }

        /// <summary>
        /// The destination buffer comes from the shared pool, so it arrives dirty and oversized. Only
        /// the bytes the compressed payload actually occupies may be handed on.
        /// </summary>
        [Fact]
        public void Compress_WithADirtyPooledBuffer_YieldsOnlyThePayload()
        {
            string given = new string('x', 5000) + " end";
            byte[] source = Encoding.UTF8.GetBytes(given);

            // Dirty the buffer the rent inside the compressor is about to be handed.
            byte[] dirty = ArrayPool<byte>.Shared.Rent(source.Length);
            dirty.AsSpan().Fill(0xAB);
            ArrayPool<byte>.Shared.Return(dirty);

            Assert.Equal(given, Decompress(Compress(given)));
        }

        [Fact]
        public void Compress_WithMultiByteCharacters_RoundTrips()
        {
            const string given = "Omega and: 日本語 rocket emoji and accents: cafe";

            Assert.Equal(given, Decompress(Compress(given)));
        }

        /// <summary>
        /// A payload that gzip cannot shrink still has to come back out intact - the destination
        /// buffer has to grow past the size it was rented at.
        /// </summary>
        [Fact]
        public void Compress_WhenTheOutputIsLargerThanTheInput_GrowsTheBuffer()
        {
            var random = new Random(20260904);
            var incompressible = new byte[64];

            random.NextBytes(incompressible);

            using var destination = new PooledByteBuffer(1);

            GzipCompressor.Compress(incompressible, destination);

            byte[] compressed = destination.WrittenSpan.ToArray();

            Assert.True(compressed.Length > incompressible.Length);

            using var input = new MemoryStream(compressed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            gzip.CopyTo(output);

            Assert.Equal(incompressible, output.ToArray());
        }

        private static byte[] Compress(string source)
        {
            using var destination = new PooledByteBuffer();

            GzipCompressor.Compress(Encoding.UTF8.GetBytes(source), destination);

            return destination.WrittenSpan.ToArray();
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
