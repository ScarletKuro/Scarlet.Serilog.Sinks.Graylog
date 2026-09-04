using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using System;
using System.Buffers;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.Helpers
{
    public class ByteBufferWriterFixture
    {
        [Fact]
        public void Write_WhenTheBufferGrows_PreservesEverythingAlreadyWritten()
        {
            var target = new ByteBufferWriter(1);

            Memory<byte> initial = target.GetMemory();
            initial.Span[0] = 1;
            target.Advance(1);

            target.Write(new byte[255]);
            target.WriteByte(2);

            Memory<byte> largeRemainder = target.GetMemory(1024);

            Assert.True(largeRemainder.Length >= 1024);
            Assert.Equal(257, target.WrittenCount);
            Assert.Equal(257, target.WrittenMemory.Length);
            Assert.Equal(1, target.WrittenSpan[0]);
            Assert.Equal(2, target.WrittenSpan[256]);
        }

        [Fact]
        public void AsArraySegment_WithArrayBackedMemory_ReturnsTheOriginalSlice()
        {
            byte[] source = { 1, 2, 3, 4 };

            ArraySegment<byte> actual = ByteBufferWriter.AsArraySegment(source.AsMemory(1, 2));

            Assert.Same(source, actual.Array);
            Assert.Equal(1, actual.Offset);
            Assert.Equal(2, actual.Count);
        }

        [Fact]
        public void AsArraySegment_WithNonArrayMemory_CopiesTheSlice()
        {
            using var owner = new NonArrayMemoryManager(new byte[] { 1, 2, 3, 4 });

            ArraySegment<byte> actual = ByteBufferWriter.AsArraySegment(owner.Memory.Slice(1, 2));

            Assert.Equal(new byte[] { 2, 3 }, actual.AsSpan().ToArray());
            Assert.Equal(0, actual.Offset);
            Assert.Equal(2, actual.Count);
        }

        private sealed class NonArrayMemoryManager : MemoryManager<byte>
        {
            private readonly byte[] _buffer;

            public NonArrayMemoryManager(byte[] buffer)
            {
                _buffer = buffer;
            }

            public override Span<byte> GetSpan() => _buffer;

            public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

            public override void Unpin()
            {
            }

            protected override void Dispose(bool disposing)
            {
            }
        }
    }
}
