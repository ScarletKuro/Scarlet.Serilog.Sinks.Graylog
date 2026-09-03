using System;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport.Http
{
    /// <summary>
    /// Sends GELF messages over HTTP.
    /// </summary>
    public class HttpTransport : ITransport
    {
        private readonly ITransportClient<string> _transportClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpTransport"/> class.
        /// </summary>
        /// <param name="transportClient">The transport client that posts the message.</param>
        public HttpTransport(ITransportClient<string> transportClient)
        {
            _transportClient = transportClient;
        }

        /// <inheritdoc />
        public Task Send(string message)
        {
            return _transportClient.Send(message);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the resources used by this transport.
        /// </summary>
        /// <param name="disposing"><c>true</c> when called from <see cref="Dispose()"/>; the transport client is disposed with it.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _transportClient?.Dispose();
            }
        }
    }
}
