using System;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport
{
    /// <summary>
    /// The Transport client interface
    /// </summary>
    public interface ITransportClient : IDisposable
    {
        /// <summary>
        /// Sends the specified payload.
        /// </summary>
        /// <param name="payload">The bytes to put on the wire.</param>
        /// <remarks>
        /// <paramref name="payload"/> is only valid until the returned task completes; see
        /// <see cref="ITransport.Send"/>.
        /// </remarks>
        Task Send(ReadOnlyMemory<byte> payload);
    }
}
