using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Helpers
{
    /// <summary>
    /// A small growable <see cref="IBufferWriter{T}"/> backed by an ordinary byte array.
    /// </summary>
    /// <remarks>
    /// <see cref="ArrayBufferWriter{T}"/> serves the same purpose on modern .NET, but is unavailable
    /// on every target this package supports. The array is deliberately not pooled: each asynchronous
    /// transport owns an immutable payload with no reuse or lifetime coordination to get wrong.
    /// </remarks>
    internal sealed class ByteBufferWriter : IBufferWriter<byte>
    {
        private const int DefaultCapacity = 256;

        private byte[] _buffer;
        private int _written;

        public ByteBufferWriter(int capacity = DefaultCapacity)
        {
            _buffer = new byte[capacity < DefaultCapacity ? DefaultCapacity : capacity];
        }

        public int WrittenCount => _written;

        public ReadOnlyMemory<byte> WrittenMemory => new ReadOnlyMemory<byte>(_buffer, 0, _written);

        public ReadOnlySpan<byte> WrittenSpan => new ReadOnlySpan<byte>(_buffer, 0, _written);

        public void Advance(int count)
        {
            _written += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);

            return new Memory<byte>(_buffer, _written, _buffer.Length - _written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);

            return new Span<byte>(_buffer, _written, _buffer.Length - _written);
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

            if (_buffer.Length - _written >= sizeHint)
            {
                return;
            }

            Grow(sizeHint);
        }

        private void Grow(int sizeHint)
        {
            int required = _written + sizeHint;
            int doubled = (int)Math.Min((long)_buffer.Length * 2, int.MaxValue);
            var grown = new byte[required > doubled ? required : doubled];

            _buffer.AsSpan(0, _written).CopyTo(grown);

            _buffer = grown;
        }

        /// <summary>
        /// Exposes array-backed memory as its underlying segment, copying only when necessary.
        /// </summary>
        public static ArraySegment<byte> AsArraySegment(ReadOnlyMemory<byte> memory)
        {
            return MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment)
                ? segment
                : new ArraySegment<byte>(memory.ToArray());
        }
    }
}
