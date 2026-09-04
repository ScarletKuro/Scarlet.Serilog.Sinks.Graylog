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
        Task Send(ReadOnlyMemory<byte> message);
    }
}
