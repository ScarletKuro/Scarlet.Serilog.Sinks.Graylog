using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp
{
    /// <summary>
    /// The sizing and identifier settings used when splitting a GELF payload into UDP chunks.
    /// </summary>
    internal sealed class ChunkSettings
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ChunkSettings"/> class.
        /// </summary>
        /// <param name="messageIdGeneratorType">How the identifier shared by a message's chunks is generated.</param>
        /// <param name="maxMessageSizeInUdp">The largest datagram to send.</param>
        public ChunkSettings(MessageIdGeneratorType messageIdGeneratorType, int maxMessageSizeInUdp)
        {
            MessageIdGeneratorType = messageIdGeneratorType;
            MaxMessageSizeInUdp = maxMessageSizeInUdp;
        }

        /// <summary>How the identifier shared by the chunks of one message is generated.</summary>
        public MessageIdGeneratorType MessageIdGeneratorType { get; }

        /// <summary>The GELF chunk header size.</summary>
        public const byte PrefixSize = 12;

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
