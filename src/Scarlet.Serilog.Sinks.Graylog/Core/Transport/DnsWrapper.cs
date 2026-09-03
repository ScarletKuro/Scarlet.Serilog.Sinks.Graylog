using System.Net;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport
{
    using System.Linq;
    using System.Net.Sockets;

    /// <summary>
    /// The default <see cref="IDnsInfoProvider"/>, backed by <see cref="Dns"/>.
    /// </summary>
    internal class DnsWrapper : IDnsInfoProvider
    {
        /// <summary>
        /// Gets the host addresses.
        /// </summary>
        /// <param name="hostNameOrAddress">The host name or address.</param>
        /// <returns>Every address the name resolves to.</returns>
        /// <exception cref="System.Net.Sockets.SocketException">Resolving <paramref name="hostNameOrAddress" /> failed.</exception>
        public Task<IPAddress[]> GetHostAddresses(string hostNameOrAddress)
        {
            return Dns.GetHostAddressesAsync(hostNameOrAddress);
        }

        /// <inheritdoc />
        public async Task<IPAddress?> GetIpAddress(string hostNameOrAddress)
        {
            if (string.IsNullOrEmpty(hostNameOrAddress))
            {
                return null;
            }

            var addresses = await GetHostAddresses(hostNameOrAddress).ConfigureAwait(false);
            // Prefer IPv4 for backwards compatibility, but use IPv6 when it is the only route.
            var result = addresses.FirstOrDefault(c => c.AddressFamily == AddressFamily.InterNetwork)
                ?? addresses.FirstOrDefault(c => c.AddressFamily == AddressFamily.InterNetworkV6);
            return result;
        }
    }
}
