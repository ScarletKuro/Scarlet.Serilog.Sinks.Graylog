using System;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport.Tcp
{
    /// <summary>
    /// Sends GELF messages over TCP as null-terminated UTF-8 frames.
    /// </summary>
    public class TcpTransport : ITransport
    {
        private readonly ITransportClient<byte[]> _tcpClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="TcpTransport"/> class.
        /// </summary>
        /// <param name="tcpClient">The transport client that owns the connection.</param>
        public TcpTransport(ITransportClient<byte[]> tcpClient)
        {
            _tcpClient = tcpClient;
        }

        /// <inheritdoc />
        public Task Send(string message)
        {
#if NET
            int byteCount = System.Text.Encoding.UTF8.GetByteCount(message);
            var payload = new byte[byteCount + 1];
            System.Text.Encoding.UTF8.GetBytes(message.AsSpan(), payload.AsSpan());
            payload[^1] = 0x00;

            return _tcpClient.Send(payload);
#else            
            var payload = System.Text.Encoding.UTF8.GetBytes(message);

            Array.Resize(ref payload, payload.Length + 1);
            payload[payload.Length - 1] = 0x00;

            return _tcpClient.Send(payload);
#endif
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
                _tcpClient?.Dispose();
            }
        }
    }
}
