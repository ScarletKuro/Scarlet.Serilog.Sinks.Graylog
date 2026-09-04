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
        /// <inheritdoc />
        public async Task<IPAddress?> GetIpAddress(string hostNameOrAddress)
        {
            if (string.IsNullOrEmpty(hostNameOrAddress))
            {
                return null;
            }

            IPAddress[] addresses = await Dns.GetHostAddressesAsync(hostNameOrAddress).ConfigureAwait(false);
            // Prefer IPv4 for backwards compatibility, but use IPv6 when it is the only route.
            var result = addresses.FirstOrDefault(c => c.AddressFamily == AddressFamily.InterNetwork)
                ?? addresses.FirstOrDefault(c => c.AddressFamily == AddressFamily.InterNetworkV6);
            return result;
        }
    }
}
