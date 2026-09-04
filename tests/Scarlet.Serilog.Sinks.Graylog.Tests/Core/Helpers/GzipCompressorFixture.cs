using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using System;
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
    /// and writes into the caller's growable byte buffer.
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
        /// The destination normally has spare capacity. Only the bytes the compressed payload
        /// actually occupies may be handed on.
        /// </summary>
        [Fact]
        public void Compress_ExposesOnlyTheCompressedPayload()
        {
            string given = new string('x', 5000) + " end";

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

            var destination = new ByteBufferWriter(1);

            GzipCompressor.Compress(incompressible, destination);

            byte[] compressed = destination.WrittenSpan.ToArray();

            Assert.True(compressed.Length > incompressible.Length);

            using var input = new MemoryStream(compressed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            gzip.CopyTo(output);

            Assert.Equal(incompressible, output.ToArray());
        }

        [Fact]
        public void BufferWriterStream_ImplementsTheWriteOnlyStreamContract()
        {
            var buffer = new ByteBufferWriter();
            using Stream target = new GzipCompressor.BufferWriterStream(buffer);

            Assert.False(target.CanRead);
            Assert.False(target.CanSeek);
            Assert.True(target.CanWrite);
            Assert.Equal(0, target.Length);
            Assert.Equal(0, target.Position);

            target.Write(new byte[] { 1, 2, 3 }, 1, 2);
            target.Write(new byte[] { 4, 5 });
            target.WriteByte(6);
            target.Flush();

            Assert.Equal(5, target.Length);
            Assert.Equal(5, target.Position);
            Assert.Equal(new byte[] { 2, 3, 4, 5, 6 }, buffer.WrittenSpan.ToArray());
            Assert.Throws<NotSupportedException>(() => target.Position = 0);
            Assert.Throws<NotSupportedException>(() => target.Read(new byte[1], 0, 1));
            Assert.Throws<NotSupportedException>(() => target.Seek(0, SeekOrigin.Begin));
            Assert.Throws<NotSupportedException>(() => target.SetLength(0));
        }

        private static byte[] Compress(string source)
        {
            var destination = new ByteBufferWriter();

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
