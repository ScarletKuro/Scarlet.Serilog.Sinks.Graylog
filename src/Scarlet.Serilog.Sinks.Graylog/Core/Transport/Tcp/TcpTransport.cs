using System;
using System.Buffers;
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
        /// The payload is copied once, into a pooled buffer one byte longer, because GELF over TCP
        /// terminates each frame with a null and the sink's buffer belongs to the sink. Copying beats
        /// writing the terminator separately: a second write on the stream is a second syscall per
        /// event, and it would let a concurrent frame interleave between the two.
        /// <para>
        /// On the frameworks without a cancellable stream API the frame is returned to the pool only
        /// after a send that finished; see the remark inside the method.
        /// </para>
        /// </remarks>
        public async Task Send(ReadOnlyMemory<byte> message)
        {
            byte[] frame = ArrayPool<byte>.Shared.Rent(message.Length + 1);
            var payload = new ReadOnlyMemory<byte>(frame, 0, message.Length + 1);

            message.Span.CopyTo(frame);
            frame[message.Length] = 0x00;

#if NET
            try
            {
                await _tcpClient.Send(payload).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(frame);
            }
#else
            try
            {
                await _tcpClient.Send(payload).ConfigureAwait(false);
            }
            catch (TcpTransportClient.AbandonedTcpWriteException)
            {
                // These targets have no cancellable stream API. TcpTransportClient implements its
                // write timeout by stopping its wait on Stream.WriteAsync, which can still be reading
                // this frame. Leave that one array to the garbage collector rather than let the next
                // event rent and overwrite it mid-write. Connect and flush timeouts take the normal
                // catch below because neither operation reads this buffer.
                throw;
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(frame);
                throw;
            }

            ArrayPool<byte>.Shared.Return(frame);
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
