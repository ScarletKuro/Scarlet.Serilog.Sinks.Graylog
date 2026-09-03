using Scarlet.Serilog.Sinks.Graylog.Core.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp
{
    /// <summary>
    /// Sends GELF messages over UDP, compressing and chunking them as configured.
    /// </summary>
    public sealed class UdpTransport : ITransport
    {
        private readonly ITransportClient<byte[]> _transportClient;
        private readonly IDataToChunkConverter _chunkConverter;
        private readonly UdpTransportOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="UdpTransport"/> class.
        /// </summary>
        /// <param name="transportClient">The transport client.</param>
        /// <param name="chunkConverter">The GELF chunk converter.</param>
        /// <param name="options">The UDP transport options.</param>
        public UdpTransport(ITransportClient<byte[]> transportClient, IDataToChunkConverter chunkConverter, UdpTransportOptions options)
        {
            _transportClient = transportClient;
            _chunkConverter = chunkConverter;
            _options = options;
        }


        /// <summary>
        /// Sends the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <exception cref="ArgumentException">message was too long</exception>
        public Task Send(string message)
        {
            var payload = _options.Compression == UdpCompression.Gzip ? message.ToGzip() : message.ToByteArray();
            IList<byte[]> chunks = _chunkConverter.ConvertToChunks(payload);

            IEnumerable<Task> sendTasks = chunks.Select(c => _transportClient.Send(c));
            return Task.WhenAll(sendTasks.ToArray());
        }

        /// <inheritdoc />
        /// <remarks>The transport owns its client, so the socket is released with it.</remarks>
        public void Dispose()
        {
            _transportClient?.Dispose();
        }
    }
}
