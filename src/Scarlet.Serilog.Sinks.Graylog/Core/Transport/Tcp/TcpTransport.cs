using System;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport.Tcp
{
    /// <summary>
    /// Sends GELF messages over TCP as null-terminated UTF-8 frames.
    /// </summary>
    public sealed class TcpTransport : ITransport
    {
        private readonly ITransportClient _tcpClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="TcpTransport"/> class.
        /// </summary>
        /// <param name="tcpClient">The transport client that owns the connection.</param>
        public TcpTransport(ITransportClient tcpClient)
        {
            _tcpClient = tcpClient;
        }

        /// <inheritdoc />
        /// <remarks>
        /// The payload is copied once into an array one byte longer because GELF over TCP terminates
        /// each frame with a null. Copying beats
        /// writing the terminator separately: a second write on the stream is a second syscall per
        /// event, and it would let a concurrent frame interleave between the two.
        /// </remarks>
        public Task Send(ReadOnlyMemory<byte> message)
        {
            var frame = new byte[message.Length + 1];
            var payload = new ReadOnlyMemory<byte>(frame, 0, message.Length + 1);

            message.Span.CopyTo(frame);
            frame[message.Length] = 0x00;

            return _tcpClient.Send(payload);
        }

        /// <inheritdoc />
        /// <remarks>The transport owns its client, so the connection is closed with it.</remarks>
        public void Dispose()
        {
            _tcpClient?.Dispose();
        }
    }
}
