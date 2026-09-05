using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using System;
using System.Collections.Generic;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp
{
    /// <inheritdoc/>
    internal sealed class DataToChunkConverter : IDataToChunkConverter
    {
        private readonly ChunkSettings _settings;

        /// <summary>
        /// Initializes a new instance of the <see cref="DataToChunkConverter"/> class.
        /// </summary>
        /// <param name="settings">The settings.</param>
        public DataToChunkConverter(ChunkSettings settings)
        {
            _settings = settings;
        }

        /// <inheritdoc />
        public IReadOnlyList<byte[]> ConvertToChunks(ReadOnlyMemory<byte> message)
        {
            int messageLength = message.Length;
            if (messageLength <= _settings.MaxMessageSizeInUdp)
            {
                return new[] { message.ToArray() };
            }

            int maxChunkPayload = _settings.MaxMessageSizeInChunk;
            int chunksCount = (messageLength + maxChunkPayload - 1) / maxChunkPayload;
            if (chunksCount > ChunkSettings.MaxNumberOfChunksAllowed)
            {
                throw new ArgumentException("message was too long", nameof(message));
            }

            byte[] messageId = SecureRandom.NextBytes(8);
            ReadOnlySpan<byte> payload = message.Span;

            var result = new byte[chunksCount][];
            for (byte i = 0; i < chunksCount; i++)
            {
                int offset = i * maxChunkPayload;
                int length = Math.Min(maxChunkPayload, messageLength - offset);

                // Written straight into the datagram. Going through LINQ and a List<byte> instead cost
                // three copies of every chunk - measured at 3x the payload size in garbage, where the
                // datagrams themselves are the floor.
                var chunk = new byte[ChunkSettings.PrefixSize + length];

                chunk[0] = ChunkSettings.GelfMagicBytes[0];
                chunk[1] = ChunkSettings.GelfMagicBytes[1];
                Buffer.BlockCopy(messageId, 0, chunk, 2, ChunkSettings.MessageIdSize);
                chunk[ChunkSettings.PrefixSize - 2] = i;
                chunk[ChunkSettings.PrefixSize - 1] = (byte)chunksCount;
                payload.Slice(offset, length).CopyTo(new Span<byte>(chunk, ChunkSettings.PrefixSize, length));

                result[i] = chunk;
            }

            return result;
        }
    }
}
