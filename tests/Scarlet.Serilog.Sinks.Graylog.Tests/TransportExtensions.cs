using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using System.Text;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Tests
{
    /// <summary>
    /// Sends a GELF payload written as a string, for the fixtures whose subject is the transport
    /// rather than the payload.
    /// </summary>
    /// <remarks>
    /// The transports take UTF-8, which is what the sink now produces. Encoding here keeps those
    /// fixtures readable - a JSON literal says what is on the wire, a byte array does not.
    /// </remarks>
    internal static class TransportExtensions
    {
        internal static Task Send(this ITransport transport, string message)
        {
            return transport.Send(Encoding.UTF8.GetBytes(message));
        }

        internal static Task Send(this ITransportClient client, string message)
        {
            return client.Send(Encoding.UTF8.GetBytes(message));
        }
    }
}
