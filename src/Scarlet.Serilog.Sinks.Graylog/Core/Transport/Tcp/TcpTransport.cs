using System;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport.Tcp
{
    /// <summary>
    /// Sends GELF messages over TCP as null-terminated UTF-8 frames.
    /// </summary>
    public sealed class TcpTransport : ITransport
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
            // Sized once and filled in place. GetBytes followed by Array.Resize allocated the payload
            // twice and copied all of it, for the sake of one trailing null byte.
            int byteCount = System.Text.Encoding.UTF8.GetByteCount(message);
            var payload = new byte[byteCount + 1];
            System.Text.Encoding.UTF8.GetBytes(message, 0, message.Length, payload, 0);
            payload[byteCount] = 0x00;

            return _tcpClient.Send(payload);
#endif
        }

        /// <inheritdoc />
        /// <remarks>The transport owns its client, so the connection is closed with it.</remarks>
        public void Dispose()
        {
            _tcpClient?.Dispose();
        }
    }
}
