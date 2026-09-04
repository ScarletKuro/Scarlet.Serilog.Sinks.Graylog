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
        Task Send(ReadOnlyMemory<byte> payload);
    }
}
