using System;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport.Http
{
    /// <summary>
    /// Sends GELF messages over HTTP.
    /// </summary>
    public sealed class HttpTransport : ITransport
    {
        private readonly ITransportClient _transportClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpTransport"/> class.
        /// </summary>
        /// <param name="transportClient">The transport client that posts the message.</param>
        public HttpTransport(ITransportClient transportClient)
        {
            _transportClient = transportClient;
        }

        /// <inheritdoc />
        public Task Send(ReadOnlyMemory<byte> message)
        {
            return _transportClient.Send(message);
        }

        /// <inheritdoc />
        /// <remarks>The transport owns its client, so the client is disposed with it.</remarks>
        public void Dispose()
        {
            _transportClient.Dispose();
        }
    }
}
