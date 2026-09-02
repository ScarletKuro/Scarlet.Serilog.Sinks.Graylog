using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.Transport
{
    /// <summary>
    /// "localhost" resolves without leaving the machine, so these stay unit tests. The single test
    /// this replaced resolved github.com, putting a live DNS lookup in the CI unit-test run.
    /// </summary>
    public class DnsWrapperFixture
    {
        [Fact]
        public async Task GetHostAddresses_ReturnsTheLoopbackAddresses()
        {
            var target = new DnsWrapper();

            IPAddress[] actual = await target.GetHostAddresses("localhost");

            Assert.NotEmpty(actual);
            Assert.All(actual, address => Assert.True(IPAddress.IsLoopback(address)));
        }

        [Fact]
        public async Task GetIpAddress_ReturnsTheFirstIpv4Address()
        {
            var target = new DnsWrapper();

            IPAddress? actual = await target.GetIpAddress("localhost");

            Assert.Equal(IPAddress.Loopback, actual);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public async Task GetIpAddress_WithoutAHostname_ReturnsNullRatherThanResolving(string? hostNameOrAddress)
        {
            var target = new DnsWrapper();

            Assert.Null(await target.GetIpAddress(hostNameOrAddress!));
        }
    }
}
