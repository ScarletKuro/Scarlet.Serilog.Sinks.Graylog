using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Http;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Tcp;
using Scarlet.Serilog.Sinks.Graylog.Tests.Fakes;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
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
        private const int TlsPort = 12204;

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

        /// <summary>
        /// Graylog drops boolean additional fields, which is why the builder writes them as text. That
        /// is a claim about the server, so only this test can hold it honest.
        /// </summary>
        [Fact]
        public async Task Booleans_SurviveAsText()
        {
            GraylogServer server = _fixture.RequireServer();
            string correlation = GraylogServer.NewCorrelationId();

            using (Logger logger = LoggerFor(HttpOptions(server)))
            {
                logger.Information("booleans {Correlation} {Enabled} {Disabled}", correlation, true, false);
            }

            JsonObject message = await server.WaitForMessage($"Correlation:{correlation}", Indexing, Token());

            Assert.Equal("true", message["Enabled"]?.GetValue<string>());
            Assert.Equal("false", message["Disabled"]?.GetValue<string>());
        }

        /// <summary>
        /// Graylog sets these fields itself and discards an incoming field of the same name, so the
        /// builder appends an underscore. Sent unescaped, every one of these values is lost.
        /// </summary>
        [Fact]
        public async Task ReservedPropertyNames_AreEscapedAndSurvive()
        {
            GraylogServer server = _fixture.RequireServer();
            string correlation = GraylogServer.NewCorrelationId();

            using (Logger logger = LoggerFor(HttpOptions(server)))
            {
                logger.Information(
                    "reserved {Correlation} {message} {source} {timestamp} {level} {host} {id}",
                    correlation, "mine", "mine", "mine", "mine", "mine", "mine");
            }

            JsonObject message = await server.WaitForMessage($"Correlation:{correlation}", Indexing, Token());

            foreach (string reserved in new[] { "message", "source", "timestamp", "level", "host", "id" })
            {
                Assert.Equal("mine", message[reserved + "_"]?.GetValue<string>());
            }

            // ...and Graylog's own fields are still its own.
            Assert.Contains("reserved", message["message"]?.GetValue<string>() ?? string.Empty);
            Assert.NotEqual("mine", message["source"]?.GetValue<string>());
        }

        /// <summary>
        /// Graylog validates field names and drops a field whose name carries anything outside
        /// <c>^[\w.\-]*$</c>, so an unsanitized dictionary key loses its value without a trace.
        /// </summary>
        [Fact]
        public async Task FieldNamesWithIllegalCharacters_AreSanitizedAndSurvive()
        {
            GraylogServer server = _fixture.RequireServer();
            string correlation = GraylogServer.NewCorrelationId();

            using (Logger logger = LoggerFor(HttpOptions(server), o => o.Message.ParseArrayValues = true))
            {
                logger.Information("illegal names {Correlation} {@Bag}", correlation, new Dictionary<string, string>
                {
                    ["k8s:pod"] = "colon",
                    ["has space"] = "space",
                    ["kept.name-1"] = "legal"
                });
            }

            JsonObject message = await server.WaitForMessage($"Correlation:{correlation}", Indexing, Token());

            // The sink replaces the colon and the space, which Graylog would otherwise drop the field
            // over. Graylog then replaces the dots of its own accord - a dot means object nesting to
            // the search backend - so the separator the builder uses between a structure and its
            // members arrives as an underscore. Dashes survive both.
            Assert.Equal("colon", message["Bag_k8s_pod"]?.GetValue<string>());
            Assert.Equal("space", message["Bag_has_space"]?.GetValue<string>());
            Assert.Equal("legal", message["Bag_kept_name-1"]?.GetValue<string>());
        }

        /// <summary>
        /// GELF defines no chunking over HTTP, so an event past the input's <c>max_chunk_size</c> is
        /// refused outright. The status code alone says nothing about which setting to change.
        /// </summary>
        [Fact]
        public async Task Http_WithAMessageOverTheInputLimit_ExplainsWhichSettingRefusedIt()
        {
            GraylogServer server = _fixture.RequireServer();

            using var client = new HttpTransportClient(new HttpTransportOptions
            {
                Endpoint = new Uri($"http://{server.Host}:{HttpPort}")
            });

            // The input is created with the default 65536-byte cap; this is comfortably past it.
            string payload = "{\"version\":\"1.1\",\"host\":\"probe\",\"short_message\":\"over\",\"_big\":\""
                             + new string('x', 200_000) + "\"}";

            HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.Send(payload));

            Assert.Contains("max_chunk_size", exception.Message);
        }

        private static GraylogSinkOptions HttpOptions(GraylogServer server)
        {
            return new GraylogSinkOptions
            {
                TransportType = TransportType.Http,
                Http = new HttpTransportOptions { Endpoint = new Uri($"http://{server.Host}:{HttpPort}") }
            };
        }

        private static Logger LoggerFor(GraylogSinkOptions options, Action<GraylogSinkOptions>? configure)
        {
            configure?.Invoke(options);

            return LoggerFor(options);
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

        /// <summary>
        /// TLS against the real thing: the in-process tests prove the handshake works against a
        /// loopback SslStream, not that Graylog's own GELF TCP input accepts what the sink negotiates.
        /// </summary>
        /// <remarks>
        /// The input serves a self-signed certificate the platform will not trust, so the client pins
        /// its thumbprint through <see cref="TcpTransportClient.ValidateServerCertificate"/> - which is
        /// exactly what a consumer does for an internally signed Graylog, so the override point is
        /// under test here too. Wired through <c>Custom.Factory</c> because that override is the only
        /// way to reach it, and it is the documented one.
        /// </remarks>
        [Fact]
        public async Task Tcp_OverTls_DeliversAMessageGraylogStores()
        {
            GraylogServer server = _fixture.RequireServer();
            string thumbprint = _fixture.TlsThumbprint
                                ?? throw new InvalidOperationException("The TLS input was not prepared.");
            string correlation = GraylogServer.NewCorrelationId();

            var tcp = new TcpTransportOptions
            {
                Host = server.Host,
                Port = TlsPort,
                Tls = new TlsOptions { ServerName = "localhost" }
            };

            using (Logger logger = LoggerFor(new GraylogSinkOptions
            {
                TransportType = TransportType.Custom,
                Custom = new CustomTransportOptions
                {
                    Factory = () => new TcpTransport(new PinnedTcpTransportClient(tcp, new DnsWrapper(), thumbprint))
                },
                Message = new GelfOptions { Facility = "integration-tcp-tls" }
            }))
            {
                logger.Information("tcp over tls {Correlation}", correlation);
            }

            JsonObject message = await server.WaitForMessage($"Correlation:{correlation}", Indexing, Token());

            Assert.Equal(correlation, message["Correlation"]?.GetValue<string>());
            Assert.Equal("integration-tcp-tls", message["facility"]?.GetValue<string>());
        }

        /// <summary>
        /// Accepts one certificate and no other, so the test cannot pass against a server presenting
        /// something else.
        /// </summary>
        private sealed class PinnedTcpTransportClient : TcpTransportClient
        {
            private readonly string _thumbprint;

            public PinnedTcpTransportClient(TcpTransportOptions options, IDnsInfoProvider dns, string thumbprint)
                : base(options, dns)
            {
                _thumbprint = thumbprint;
            }

            protected override bool ValidateServerCertificate(
                object sender,
                X509Certificate? certificate,
                X509Chain? chain,
                SslPolicyErrors sslPolicyErrors)
            {
                return certificate != null
                       && string.Equals(certificate.GetCertHashString(), _thumbprint, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
