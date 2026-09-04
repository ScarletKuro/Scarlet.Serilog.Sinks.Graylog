using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp
{
    /// <summary>
    /// Sends GELF messages over UDP, compressing and chunking them as configured.
    /// </summary>
    public sealed class UdpTransport : ITransport
    {
        private readonly ITransportClient _transportClient;
        private readonly IDataToChunkConverter _chunkConverter;
        private readonly UdpTransportOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="UdpTransport"/> class.
        /// </summary>
        /// <param name="transportClient">The transport client.</param>
        /// <param name="chunkConverter">The GELF chunk converter.</param>
        /// <param name="options">The UDP transport options.</param>
        public UdpTransport(ITransportClient transportClient, IDataToChunkConverter chunkConverter, UdpTransportOptions options)
        {
            _transportClient = transportClient;
            _chunkConverter = chunkConverter;
            _options = options;
        }

        /// <summary>
        /// Sends the specified message.
        /// </summary>
        /// <param name="message">The GELF payload, as UTF-8.</param>
        /// <exception cref="ArgumentException">message was too long</exception>
        public async Task Send(ReadOnlyMemory<byte> message)
        {
            if (_options.Compression != UdpCompression.Gzip)
            {
                await SendDatagrams(message).ConfigureAwait(false);

                return;
            }

            using var compressed = new PooledByteBuffer(message.Length);

            GzipCompressor.Compress(message, compressed);

            await SendDatagrams(compressed.WrittenMemory).ConfigureAwait(false);
        }

        /// <summary>
        /// Puts the finished payload on the wire, splitting it first if it is too large for one
        /// datagram.
        /// </summary>
        /// <remarks>
        /// A payload that fits goes out untouched, without asking the chunk converter for a list to
        /// hold it - the overwhelmingly common case, and the one worth keeping free of allocation.
        /// <para>
        /// The chunks of one message are sent one after another rather than started together. The
        /// client serializes its sends behind a lock anyway, so nothing was ever really in flight
        /// concurrently; all Task.WhenAll added was a task array and a LINQ pipeline per event.
        /// </para>
        /// </remarks>
        private async Task SendDatagrams(ReadOnlyMemory<byte> payload)
        {
            if (payload.Length <= _options.MaximumDatagramSize)
            {
                await _transportClient.Send(payload).ConfigureAwait(false);

                return;
            }

            IList<byte[]> chunks = _chunkConverter.ConvertToChunks(payload);

            foreach (byte[] chunk in chunks)
            {
                await _transportClient.Send(chunk).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        /// <remarks>The transport owns its client, so the socket is released with it.</remarks>
        public void Dispose()
        {
            _transportClient?.Dispose();
        }
    }
}
