using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Tests.Fakes;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests
{
    /// <summary>
    /// Sends GELF to a real Graylog over every transport and reads the stored message back out of it.
    /// </summary>
    /// <remarks>
    /// Everything else in this project proves what the sink puts on the wire; only these tests prove
    /// that Graylog accepts it. That is a different question, and the answer has been wrong before:
    /// a field name Graylog silently drops, a TCP frame it never finishes reading, and a chunked
    /// datagram it cannot reassemble all produce a payload that looks perfect against a loopback fake
    /// and no message at all in the search results.
    /// <para>
    /// The server comes from <c>tests/integration/docker-compose.yml</c>; see
    /// <see cref="GraylogServerFixture"/> for what happens when it is not running.
    /// </para>
    /// </remarks>
    [Trait("Category", "Integration")]
    public class GraylogEndToEndFixture : IClassFixture<GraylogServerFixture>
    {
        private const int UdpPort = 12201;
        private const int TcpPort = 12202;
        private const int HttpPort = 12203;

        private static readonly TimeSpan Indexing = TimeSpan.FromSeconds(90);

        private readonly GraylogServerFixture _fixture;

        public GraylogEndToEndFixture(GraylogServerFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Udp_DeliversAMessageGraylogStores()
        {
            GraylogServer server = _fixture.RequireServer();
            string correlation = GraylogServer.NewCorrelationId();

            using (Logger logger = LoggerFor(new GraylogSinkOptions
            {
                TransportType = TransportType.Udp,
                Udp = new UdpTransportOptions { Host = server.Host, Port = UdpPort },
                Message = new GelfOptions { Facility = "integration-udp" }
            }))
            {
                logger.Information("udp end to end {Correlation}", correlation);
            }

            JsonObject message = await server.WaitForMessage($"Correlation:{correlation}", Indexing, Token());

            Assert.Equal(correlation, message["Correlation"]?.GetValue<string>());
            Assert.Equal("integration-udp", message["facility"]?.GetValue<string>());
            Assert.Contains("udp end to end", message["message"]?.GetValue<string>() ?? string.Empty);
            Assert.Equal("Information", message["stringLevel"]?.GetValue<string>());
        }

        /// <summary>
        /// A message past the datagram size is split into GELF chunks, and only Graylog can say whether
        /// they reassemble - the chunk header is written by hand, one byte at a time.
        /// </summary>
        [Fact]
        public async Task Udp_WithAMessageLargerThanOneDatagram_DeliversEveryChunk()
        {
            GraylogServer server = _fixture.RequireServer();
            string correlation = GraylogServer.NewCorrelationId();
            // Many datagrams' worth, but under the 32766-byte term limit the search backend puts on a
            // single field - past that the message reaches Graylog and is then rejected at indexing,
            // which tests the wrong thing.
            string payload = new string('x', 20000);

            using (Logger logger = LoggerFor(new GraylogSinkOptions
            {
                TransportType = TransportType.Udp,
                Udp = new UdpTransportOptions
                {
                    Host = server.Host,
                    Port = UdpPort,
                    // Small enough that even a modest event has to be chunked.
                    MaximumDatagramSize = 1400
                },
                Message = new GelfOptions { Facility = "integration-udp-chunked" }
            }))
            {
                logger.Information("chunked {Correlation} {Payload}", correlation, payload);
            }

            JsonObject message = await server.WaitForMessage($"Correlation:{correlation}", Indexing, Token());

            Assert.Equal(correlation, message["Correlation"]?.GetValue<string>());
            Assert.Equal(payload, message["Payload"]?.GetValue<string>());
        }

        [Fact]
        public async Task Tcp_DeliversAMessageGraylogStores()
        {
            GraylogServer server = _fixture.RequireServer();
            string correlation = GraylogServer.NewCorrelationId();

            using (Logger logger = LoggerFor(new GraylogSinkOptions
            {
                TransportType = TransportType.Tcp,
                Tcp = new TcpTransportOptions { Host = server.Host, Port = TcpPort },
                Message = new GelfOptions { Facility = "integration-tcp" }
            }))
            {
                logger.Warning("tcp end to end {Correlation}", correlation);
            }

            JsonObject message = await server.WaitForMessage($"Correlation:{correlation}", Indexing, Token());

            Assert.Equal(correlation, message["Correlation"]?.GetValue<string>());
            Assert.Equal("integration-tcp", message["facility"]?.GetValue<string>());
            Assert.Equal("Warning", message["stringLevel"]?.GetValue<string>());
        }

        /// <summary>
        /// The TCP client keeps one connection and writes null-terminated frames into it, so a second
        /// message exercises framing in a way the first cannot.
        /// </summary>
        [Fact]
        public async Task Tcp_WithSeveralEventsOnOneConnection_DeliversEachAsItsOwnMessage()
        {
            GraylogServer server = _fixture.RequireServer();
            string first = GraylogServer.NewCorrelationId();
            string second = GraylogServer.NewCorrelationId();

            using (Logger logger = LoggerFor(new GraylogSinkOptions
            {
                TransportType = TransportType.Tcp,
                Tcp = new TcpTransportOptions { Host = server.Host, Port = TcpPort }
            }))
            {
                logger.Information("first frame {Correlation}", first);
                logger.Information("second frame {Correlation}", second);
            }

            JsonObject firstMessage = await server.WaitForMessage($"Correlation:{first}", Indexing, Token());
            JsonObject secondMessage = await server.WaitForMessage($"Correlation:{second}", Indexing, Token());

            Assert.Contains("first frame", firstMessage["message"]?.GetValue<string>() ?? string.Empty);
            Assert.Contains("second frame", secondMessage["message"]?.GetValue<string>() ?? string.Empty);
        }

        [Fact]
        public async Task Http_DeliversAMessageGraylogStores()
        {
            GraylogServer server = _fixture.RequireServer();
            string correlation = GraylogServer.NewCorrelationId();

            using (Logger logger = LoggerFor(new GraylogSinkOptions
            {
                TransportType = TransportType.Http,
                Http = new HttpTransportOptions { Endpoint = new Uri($"http://{server.Host}:{HttpPort}") },
                Message = new GelfOptions { Facility = "integration-http" }
            }))
            {
                logger.Information("http end to end {Correlation}", correlation);
            }

            JsonObject message = await server.WaitForMessage($"Correlation:{correlation}", Indexing, Token());

            Assert.Equal(correlation, message["Correlation"]?.GetValue<string>());
            Assert.Equal("integration-http", message["facility"]?.GetValue<string>());
        }

        /// <summary>
        /// The exception fields are the sink's own invention, so whether Graylog keeps them under those
        /// names is only answerable here.
        /// </summary>
        [Fact]
        public async Task Http_WithAnException_StoresTheExceptionFields()
        {
            GraylogServer server = _fixture.RequireServer();
            string correlation = GraylogServer.NewCorrelationId();

            using (Logger logger = LoggerFor(new GraylogSinkOptions
            {
                TransportType = TransportType.Http,
                Http = new HttpTransportOptions { Endpoint = new Uri($"http://{server.Host}:{HttpPort}") }
            }))
            {
                logger.Error(Thrown("integration blew up"), "failed {Correlation}", correlation);
            }

            JsonObject message = await server.WaitForMessage($"Correlation:{correlation}", Indexing, Token());

            Assert.Equal("System.InvalidOperationException", message["ExceptionType"]?.GetValue<string>());
            Assert.Equal("integration blew up", message["ExceptionMessage"]?.GetValue<string>());
            Assert.False(string.IsNullOrEmpty(message["StackTrace"]?.GetValue<string>()));
        }

        /// <summary>
        /// Batching drives a different sink entry point, and the batch has to reach Graylog as separate
        /// messages rather than one blob.
        /// </summary>
        [Fact]
        public async Task Batched_DeliversEveryEventInTheBatch()
        {
            GraylogServer server = _fixture.RequireServer();
            string correlation = GraylogServer.NewCorrelationId();

            using (Logger logger = LoggerFor(new GraylogSinkOptions
            {
                TransportType = TransportType.Udp,
                Udp = new UdpTransportOptions { Host = server.Host, Port = UdpPort },
                Delivery = new DeliveryOptions
                {
                    Batching = new BatchingOptions
                    {
                        BatchSizeLimit = 10,
                        BufferingTimeLimit = TimeSpan.FromMilliseconds(500)
                    }
                }
            }))
            {
                for (int index = 0; index < 5; index++)
                {
                    logger.Information("batched {Index} {Correlation}", index, correlation);
                }
            }

            // One is enough to prove the batch left the process; the search returns the newest match.
            JsonObject message = await server.WaitForMessage($"Correlation:{correlation}", Indexing, Token());

            Assert.Equal(correlation, message["Correlation"]?.GetValue<string>());
        }

        private static Logger LoggerFor(GraylogSinkOptions options)
        {
            return new LoggerConfiguration()
                .WriteTo.Graylog(options)
                .CreateLogger();
        }

        /// <summary>
        /// An exception with a real stack trace: one that was never thrown has none.
        /// </summary>
        private static Exception Thrown(string message)
        {
            try
            {
                throw new InvalidOperationException(message);
            }
            catch (InvalidOperationException exception)
            {
                return exception;
            }
        }

        private static CancellationToken Token()
        {
            return TestContext.Current.CancellationToken;
        }
    }
}
