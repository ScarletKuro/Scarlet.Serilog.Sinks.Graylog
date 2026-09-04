using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Helpers
{
    /// <summary>
    /// A growable <see cref="IBufferWriter{T}"/> over an array rented from <see cref="ArrayPool{T}"/>.
    /// </summary>
    /// <remarks>
    /// This is what a GELF payload is written into, so the largest per-event buffer can be reused by
    /// the shared pool after the send completes instead of allocating a fresh byte array each time.
    /// <para>
    /// <see cref="ArrayBufferWriter{T}"/> is not a substitute. It allocates a fresh array on every
    /// growth and never returns one, and it is unavailable on the netstandard2.0 target.
    /// </para>
    /// <para>
    /// The buffer is returned to the pool without clearing. It holds log content that is about to go
    /// out over the wire regardless, and the pool is process-local.
    /// </para>
    /// </remarks>
    internal sealed class PooledByteBuffer : IBufferWriter<byte>, IDisposable
    {
        private const int DefaultCapacity = 1024;

        private byte[]? _buffer;
        private int _written;

        public PooledByteBuffer(int capacity = DefaultCapacity)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(capacity < DefaultCapacity ? DefaultCapacity : capacity);
        }

        public int WrittenCount => _written;

        public ReadOnlyMemory<byte> WrittenMemory => new ReadOnlyMemory<byte>(Buffer, 0, _written);

        public ReadOnlySpan<byte> WrittenSpan => new ReadOnlySpan<byte>(Buffer, 0, _written);

        /// <summary>The rented array, as an <see cref="ArraySegment{T}"/> over the written bytes.</summary>
        /// <remarks>
        /// For the framework targets whose socket, stream and HTTP content APIs take an array rather
        /// than a <see cref="ReadOnlyMemory{T}"/>. The segment is only valid until the next write.
        /// </remarks>
        public ArraySegment<byte> WrittenSegment => new ArraySegment<byte>(Buffer, 0, _written);

        private byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(PooledByteBuffer));

        public void Advance(int count)
        {
            _written += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);

            return new Memory<byte>(_buffer, _written, _buffer!.Length - _written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);

            return new Span<byte>(_buffer, _written, _buffer!.Length - _written);
        }

        public void Write(ReadOnlySpan<byte> value)
        {
            value.CopyTo(GetSpan(value.Length));

            _written += value.Length;
        }

        public void WriteByte(byte value)
        {
            GetSpan(1)[0] = value;

            _written++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCapacity(int sizeHint)
        {
            if (sizeHint < 1)
            {
                sizeHint = 1;
            }

            if (Buffer.Length - _written >= sizeHint)
            {
                return;
            }

            Grow(sizeHint);
        }

        private void Grow(int sizeHint)
        {
            byte[] current = Buffer;
            int required = _written + sizeHint;
            int doubled = current.Length > int.MaxValue / 2 ? int.MaxValue : current.Length * 2;

            byte[] grown = ArrayPool<byte>.Shared.Rent(required > doubled ? required : doubled);

            new ReadOnlySpan<byte>(current, 0, _written).CopyTo(grown);

            _buffer = grown;
            ArrayPool<byte>.Shared.Return(current);
        }

        /// <summary>
        /// Exposes a <see cref="ReadOnlyMemory{T}"/> as the array segment underneath it, copying only
        /// if it is not array-backed.
        /// </summary>
        /// <remarks>
        /// Every payload the sink produces comes out of this class, so the copy is unreachable in
        /// practice; it is there because a custom <c>ITransport</c> may hand a client memory of its
        /// own. Used by the transports on the frameworks that have no span-based socket or stream API.
        /// </remarks>
        public static ArraySegment<byte> AsArraySegment(ReadOnlyMemory<byte> memory)
        {
            return MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment)
                ? segment
                : new ArraySegment<byte>(memory.ToArray());
        }

        public void Dispose()
        {
            byte[]? buffer = _buffer;

            if (buffer == null)
            {
                return;
            }

            _buffer = null;
            _written = 0;
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
