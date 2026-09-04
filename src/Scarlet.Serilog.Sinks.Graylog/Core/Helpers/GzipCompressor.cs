using System;
using System.IO;
using System.IO.Compression;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Helpers
{
    /// <summary>
    /// Gzip-compresses a GELF payload into a growable byte buffer.
    /// </summary>
    /// <remarks>
    /// This runs on every compressed UDP event. It used to take the payload as a
    /// <see cref="string"/>, encode it to UTF-8 and compress into a <see cref="MemoryStream"/> whose
    /// <c>ToArray</c> then copied the result out again - so the largest thing the send allocated was
    /// allocated twice. The payload now arrives as UTF-8 already and the compressor writes straight
    /// into the caller's buffer, which leaves the datagrams themselves as the only garbage.
    /// <para>
    /// Gzip dominates the time either way; this is about the allocations, not the microseconds.
    /// </para>
    /// </remarks>
    internal static class GzipCompressor
    {
        public static void Compress(ReadOnlyMemory<byte> source, ByteBufferWriter destination)
        {
            using var target = new BufferWriterStream(destination);

            // leaveOpen, so each stream is owned and disposed exactly once, and the gzip stream is
            // closed first - its trailer is only written when it is.
            using (var gzip = new GZipStream(target, CompressionMode.Compress, leaveOpen: true))
            {
#if NET
                gzip.Write(source.Span);
#else
                ArraySegment<byte> segment = ByteBufferWriter.AsArraySegment(source);

                gzip.Write(segment.Array!, segment.Offset, segment.Count);
#endif
            }
        }

        /// <summary>
        /// A write-only <see cref="Stream"/> over a <see cref="ByteBufferWriter"/>, so a stream-based
        /// compressor can produce its output without an intermediate array.
        /// </summary>
        private sealed class BufferWriterStream : Stream
        {
            private readonly ByteBufferWriter _buffer;

            public BufferWriterStream(ByteBufferWriter buffer)
            {
                _buffer = buffer;
            }

            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => _buffer.WrittenCount;

            public override long Position
            {
                get => _buffer.WrittenCount;
                set => throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                _buffer.Write(new ReadOnlySpan<byte>(buffer, offset, count));
            }

#if NET
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                _buffer.Write(buffer);
            }
#endif

            public override void WriteByte(byte value)
            {
                _buffer.WriteByte(value);
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
