namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp
{
    /// <summary>
    /// The sizing settings used when splitting a GELF payload into UDP chunks.
    /// </summary>
    internal sealed class ChunkSettings
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ChunkSettings"/> class.
        /// </summary>
        /// <param name="maxMessageSizeInUdp">The largest datagram to send.</param>
        public ChunkSettings(int maxMessageSizeInUdp)
        {
            MaxMessageSizeInUdp = maxMessageSizeInUdp;
        }

        /// <summary>The GELF chunk header size: two magic bytes, the message id, then the sequence number and count.</summary>
        public const byte PrefixSize = 12;

        /// <summary>The size of the message id inside the chunk header.</summary>
        public const byte MessageIdSize = 8;

        /// <summary>The maximum number of GELF chunks allowed.</summary>
        public const byte MaxNumberOfChunksAllowed = 128;

        /// <summary>
        /// The maximum message size in UDP
        /// <remarks>
        /// UDP chunks are usually limited to a size of 8192 bytes
        /// </remarks>
        /// </summary>
        public int MaxMessageSizeInUdp { get; }

        /// <summary>The two bytes that mark a datagram as a GELF chunk.</summary>
        public static readonly byte[] GelfMagicBytes = { 0x1e, 0x0f };

        /// <summary>
        /// The maximum message size in chunk
        /// </summary>
        public int MaxMessageSizeInChunk => MaxMessageSizeInUdp - PrefixSize;
    }
}
