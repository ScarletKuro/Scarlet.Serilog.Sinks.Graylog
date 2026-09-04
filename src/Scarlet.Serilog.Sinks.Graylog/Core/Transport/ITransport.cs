using System;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport
{
    /// <summary>
    /// The Transport interface
    /// </summary>
    public interface ITransport : IDisposable
    {
        /// <summary>
        /// Sends the specified GELF message.
        /// </summary>
        /// <param name="message">The GELF payload, as UTF-8.</param>
        /// <remarks>
        /// <paramref name="message"/> is only valid until the returned task completes. It is a slice
        /// of a pooled buffer the sink hands back afterwards, so an implementation that needs the
        /// payload beyond the send has to copy it.
        /// </remarks>
        Task Send(ReadOnlyMemory<byte> message);
    }
}
