using System.Net;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport;

/// <summary>
/// Resolves a Graylog host name to the address a transport connects to.
/// </summary>
public interface IDnsInfoProvider
{
    /// <summary>
    /// Gets the host addresses.
    /// </summary>
    /// <param name="hostNameOrAddress">The host name or address.</param>
    /// <returns>Every address the name resolves to.</returns>
    Task<IPAddress[]> GetHostAddresses(string hostNameOrAddress);

    /// <summary>
    /// Gets the single address a transport should connect to.
    /// </summary>
    /// <param name="hostNameOrAddress">The host name or address.</param>
    /// <returns>
    /// The address to use, or <c>null</c> when <paramref name="hostNameOrAddress"/> is empty or
    /// resolves to nothing usable.
    /// </returns>
    Task<IPAddress?> GetIpAddress(string hostNameOrAddress);
}
