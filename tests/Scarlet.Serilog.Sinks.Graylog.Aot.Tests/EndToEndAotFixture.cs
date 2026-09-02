using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Serilog;
using Serilog.Core;
using Serilog.Debugging;
using System;
using System.Text;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Aot.Tests
{
    /// <summary>
    /// Drives a real logger through the sink so the transport, GELF envelope and final
    /// <c>ToJsonString</c> are all exercised, not just the scalar writer.
    /// </summary>
    public class EndToEndAotFixture
    {
        [Fact]
        public void Logger_WritesTheExpectedGelfPayload()
        {
            var selfLog = new StringBuilder();

            SelfLog.Enable(message => selfLog.Append(message));

            try
            {
                var transport = new RecordingTransport();

                using (Logger logger = new LoggerConfiguration()
                    .WriteTo.Graylog(new GraylogSinkOptions
                    {
                        Facility = "aot-harness",
                        HostnameOverride = "harness-host",
                        TransportType = TransportType.Custom,
                        TransportFactory = () => transport
                    })
                    .CreateLogger())
                {
                    logger.Information("Ordered {Count} of {Sku} at {When}", 3, "ABC-123",
                        new DateTime(2026, 9, 2, 13, 45, 30, DateTimeKind.Utc));
                }

                string payload = Assert.Single(transport.Payloads);

                Assert.Contains("\"host\":\"harness-host\"", payload, StringComparison.Ordinal);
                Assert.Contains("\"_facility\":\"aot-harness\"", payload, StringComparison.Ordinal);
                Assert.Contains("\"_Count\":3", payload, StringComparison.Ordinal);
                Assert.Contains("\"_Sku\":\"ABC-123\"", payload, StringComparison.Ordinal);
                Assert.Contains("\"_When\":\"2026-09-02T13:45:30Z\"", payload, StringComparison.Ordinal);

                // A guard rather than a proof: Emit reports failures to SelfLog from a faulted-task
                // continuation, and this transport's Send never faults, so an empty SelfLog is expected
                // rather than earned. It stays because a trimming regression that broke serialization
                // outright would surface here, and it carries over from the console harness.
                Assert.Equal(string.Empty, selfLog.ToString());
            } finally
            {
                SelfLog.Disable();
            }
        }
    }
}
