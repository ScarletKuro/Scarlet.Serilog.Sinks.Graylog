using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp;
using Scarlet.Serilog.Sinks.Graylog.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests
{
    /// <summary>
    /// Every rejection the sink applies to its options before it builds anything.
    /// </summary>
    /// <remarks>
    /// The parameter name is asserted alongside the exception type: it is what tells a consumer which
    /// option to fix, and it is the part that silently rots when a property is renamed.
    /// </remarks>
    public class GraylogSinkOptionsValidatorFixture
    {
        public static TheoryData<TimeSpan> NonPositiveTimeouts()
        {
            return new TheoryData<TimeSpan> { TimeSpan.Zero, TimeSpan.FromMilliseconds(-1) };
        }

        public static TheoryData<int> InvalidPorts()
        {
            return new TheoryData<int> { 0, -1, 65536 };
        }

        [Fact]
        public void Validate_WithDefaults_Accepts()
        {
            GraylogSinkOptions options = Options(TransportType.Udp, o => o.Udp.Host = "graylog.example.org");

            GraylogSinkOptionsValidator.Validate(options);
        }

        [Fact]
        public void Validate_WithoutMessageOptions_Throws()
        {
            GraylogSinkOptions options = Options(TransportType.Udp, o => o.Message = null!);

            Assert.Equal("Message", Throws<ArgumentNullException>(options).ParamName);
        }

        [Fact]
        public void Validate_WithoutJsonSerializerOptions_Throws()
        {
            GraylogSinkOptions options = Options(
                TransportType.Udp,
                o => o.Message.JsonSerializerOptions = null!);

            Assert.Equal("JsonSerializerOptions", Throws<ArgumentNullException>(options).ParamName);
        }

        [Fact]
        public void Validate_WithoutDeliveryOptions_Throws()
        {
            GraylogSinkOptions options = Options(TransportType.Udp, o => o.Delivery = null!);

            Assert.Equal("Delivery", Throws<ArgumentNullException>(options).ParamName);
        }

        [Theory]
        [MemberData(nameof(NonPositiveTimeouts))]
        public void Validate_WithANonPositiveShutdownTimeout_Throws(TimeSpan shutdownTimeout)
        {
            GraylogSinkOptions options = Options(TransportType.Udp, o =>
            {
                o.Udp.Host = "graylog.example.org";
                o.Delivery.ShutdownTimeout = shutdownTimeout;
            });

            Assert.Equal("ShutdownTimeout", Throws<ArgumentOutOfRangeException>(options).ParamName);
        }

        [Fact]
        public void Validate_WithoutAShutdownTimeout_Accepts()
        {
            GraylogSinkOptions options = Options(TransportType.Udp, o =>
            {
                o.Udp.Host = "graylog.example.org";
                o.Delivery.ShutdownTimeout = null;
            });

            GraylogSinkOptionsValidator.Validate(options);
        }

        [Fact]
        public void Validate_WithAnUnsupportedTransport_Throws()
        {
            GraylogSinkOptions options = Options((TransportType)99, _ => { });

            Assert.Equal("TransportType", Throws<ArgumentOutOfRangeException>(options).ParamName);
        }

        [Fact]
        public void Validate_Udp_WithoutTransportOptions_Throws()
        {
            GraylogSinkOptions options = Options(TransportType.Udp, o => o.Udp = null!);

            Assert.Equal("Udp", Throws<ArgumentNullException>(options).ParamName);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Validate_Udp_WithoutAHost_Throws(string? host)
        {
            GraylogSinkOptions options = Options(TransportType.Udp, o => o.Udp.Host = host);

            ArgumentException exception = Throws<ArgumentException>(options);

            Assert.Equal("Host", exception.ParamName);
            Assert.Contains("UDP host", exception.Message);
        }

        [Theory]
        [MemberData(nameof(InvalidPorts))]
        public void Validate_Udp_WithAPortOutsideTheValidRange_Throws(int port)
        {
            GraylogSinkOptions options = Options(TransportType.Udp, o =>
            {
                o.Udp.Host = "graylog.example.org";
                o.Udp.Port = port;
            });

            Assert.Equal("Port", Throws<ArgumentOutOfRangeException>(options).ParamName);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(ChunkSettings.PrefixSize)]
        public void Validate_Udp_WithADatagramSizeThatCannotHoldAChunkHeader_Throws(int maximumDatagramSize)
        {
            GraylogSinkOptions options = Options(TransportType.Udp, o =>
            {
                o.Udp.Host = "graylog.example.org";
                o.Udp.MaximumDatagramSize = maximumDatagramSize;
            });

            Assert.Equal("MaximumDatagramSize", Throws<ArgumentOutOfRangeException>(options).ParamName);
        }

        [Fact]
        public void Validate_Udp_WithADatagramSizeAboveTheIpv4Maximum_Throws()
        {
            GraylogSinkOptions options = Options(TransportType.Udp, o =>
            {
                o.Udp.Host = "graylog.example.org";
                o.Udp.MaximumDatagramSize = 65508;
            });

            ArgumentOutOfRangeException exception = Throws<ArgumentOutOfRangeException>(options);

            Assert.Equal("MaximumDatagramSize", exception.ParamName);
            Assert.Contains("65507", exception.Message);
        }

        [Theory]
        [MemberData(nameof(NonPositiveTimeouts))]
        public void Validate_Udp_WithANonPositiveDnsRefreshInterval_Throws(TimeSpan dnsRefreshInterval)
        {
            GraylogSinkOptions options = Options(TransportType.Udp, o =>
            {
                o.Udp.Host = "graylog.example.org";
                o.Udp.DnsRefreshInterval = dnsRefreshInterval;
            });

            Assert.Equal("DnsRefreshInterval", Throws<ArgumentOutOfRangeException>(options).ParamName);
        }

        [Fact]
        public void Validate_Tcp_WithAHostAndPort_Accepts()
        {
            GraylogSinkOptions options = Options(TransportType.Tcp, o => o.Tcp.Host = "graylog.example.org");

            GraylogSinkOptionsValidator.Validate(options);
        }

        [Fact]
        public void Validate_Tcp_WithoutTransportOptions_Throws()
        {
            GraylogSinkOptions options = Options(TransportType.Tcp, o => o.Tcp = null!);

            Assert.Equal("Tcp", Throws<ArgumentNullException>(options).ParamName);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Validate_Tcp_WithoutAHost_Throws(string? host)
        {
            GraylogSinkOptions options = Options(TransportType.Tcp, o => o.Tcp.Host = host);

            ArgumentException exception = Throws<ArgumentException>(options);

            Assert.Equal("Host", exception.ParamName);
            Assert.Contains("TCP host", exception.Message);
        }

        [Theory]
        [MemberData(nameof(InvalidPorts))]
        public void Validate_Tcp_WithAPortOutsideTheValidRange_Throws(int port)
        {
            GraylogSinkOptions options = Options(TransportType.Tcp, o =>
            {
                o.Tcp.Host = "graylog.example.org";
                o.Tcp.Port = port;
            });

            Assert.Equal("Port", Throws<ArgumentOutOfRangeException>(options).ParamName);
        }

        [Theory]
        [MemberData(nameof(NonPositiveTimeouts))]
        public void Validate_Tcp_WithANonPositiveConnectTimeout_Throws(TimeSpan connectTimeout)
        {
            GraylogSinkOptions options = Options(TransportType.Tcp, o =>
            {
                o.Tcp.Host = "graylog.example.org";
                o.Tcp.ConnectTimeout = connectTimeout;
            });

            Assert.Equal("ConnectTimeout", Throws<ArgumentOutOfRangeException>(options).ParamName);
        }

        [Theory]
        [MemberData(nameof(NonPositiveTimeouts))]
        public void Validate_Tcp_WithANonPositiveWriteTimeout_Throws(TimeSpan writeTimeout)
        {
            GraylogSinkOptions options = Options(TransportType.Tcp, o =>
            {
                o.Tcp.Host = "graylog.example.org";
                o.Tcp.WriteTimeout = writeTimeout;
            });

            Assert.Equal("WriteTimeout", Throws<ArgumentOutOfRangeException>(options).ParamName);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Validate_Tcp_WithABlankTlsServerName_Throws(string serverName)
        {
            GraylogSinkOptions options = Options(TransportType.Tcp, o =>
            {
                o.Tcp.Host = "graylog.example.org";
                o.Tcp.Tls = new TlsOptions { ServerName = serverName };
            });

            ArgumentException exception = Throws<ArgumentException>(options);

            Assert.Equal("ServerName", exception.ParamName);
            Assert.Contains("TCP TLS server name", exception.Message);
        }

        [Fact]
        public void Validate_Tcp_WithAClientCertificatePasswordAndNoPath_Throws()
        {
            GraylogSinkOptions options = Options(TransportType.Tcp, o =>
            {
                o.Tcp.Host = "graylog.example.org";
                o.Tcp.Tls = new TlsOptions { ClientCertificatePassword = "secret" };
            });

            ArgumentException exception = Throws<ArgumentException>(options);

            Assert.Equal("ClientCertificatePassword", exception.ParamName);
            Assert.Contains("TCP TLS client certificate password", exception.Message);
        }

        [Fact]
        public void Validate_Tcp_WithACompleteTlsConfiguration_Accepts()
        {
            GraylogSinkOptions options = Options(TransportType.Tcp, o =>
            {
                o.Tcp.Host = "graylog.example.org";
                o.Tcp.Tls = new TlsOptions
                {
                    ServerName = "graylog.example.org",
                    ClientCertificatePath = "client.pfx",
                    ClientCertificatePassword = "secret"
                };
            });

            GraylogSinkOptionsValidator.Validate(options);
        }

        [Fact]
        public void Validate_Tcp_WithAnInMemoryClientCertificate_Accepts()
        {
            using X509Certificate2 certificate = TestCertificates.CreateSelfSigned();
            GraylogSinkOptions options = Options(TransportType.Tcp, o =>
            {
                o.Tcp.Host = "graylog.example.org";
                o.Tcp.Tls = new TlsOptions { ClientCertificate = certificate };
            });

            GraylogSinkOptionsValidator.Validate(options);
        }

        /// <summary>
        /// Two sources for one certificate is a configuration mistake, not a precedence question.
        /// </summary>
        [Fact]
        public void Validate_Tcp_WithBothAClientCertificateAndAPath_Throws()
        {
            using X509Certificate2 certificate = TestCertificates.CreateSelfSigned();
            GraylogSinkOptions options = Options(TransportType.Tcp, o =>
            {
                o.Tcp.Host = "graylog.example.org";
                o.Tcp.Tls = new TlsOptions
                {
                    ClientCertificate = certificate,
                    ClientCertificatePath = "client.pfx"
                };
            });

            ArgumentException exception = Throws<ArgumentException>(options);

            Assert.Equal("ClientCertificate", exception.ParamName);
            Assert.Contains("cannot set both", exception.Message);
        }

        [Fact]
        public void Validate_Tcp_WithAnInMemoryClientCertificateAndAPassword_Throws()
        {
            using X509Certificate2 certificate = TestCertificates.CreateSelfSigned();
            GraylogSinkOptions options = Options(TransportType.Tcp, o =>
            {
                o.Tcp.Host = "graylog.example.org";
                o.Tcp.Tls = new TlsOptions
                {
                    ClientCertificate = certificate,
                    ClientCertificatePassword = "secret"
                };
            });

            ArgumentException exception = Throws<ArgumentException>(options);

            Assert.Equal("ClientCertificatePassword", exception.ParamName);
            Assert.Contains("needs none", exception.Message);
        }

        /// <summary>
        /// Without a private key the certificate cannot answer the server's request, and the failure
        /// would otherwise surface as an opaque handshake error at the first send.
        /// </summary>
        [Fact]
        public void Validate_Tcp_WithAClientCertificateThatHasNoPrivateKey_Throws()
        {
            using X509Certificate2 certificate = TestCertificates.CreateWithoutPrivateKey();
            GraylogSinkOptions options = Options(TransportType.Tcp, o =>
            {
                o.Tcp.Host = "graylog.example.org";
                o.Tcp.Tls = new TlsOptions { ClientCertificate = certificate };
            });

            ArgumentException exception = Throws<ArgumentException>(options);

            Assert.Equal("ClientCertificate", exception.ParamName);
            Assert.Contains("private key", exception.Message);
        }

        [Fact]
        public void Validate_Http_WithAnInMemoryClientCertificateOverHttps_Accepts()
        {
            using X509Certificate2 certificate = TestCertificates.CreateSelfSigned();
            GraylogSinkOptions options = Options(TransportType.Http, o =>
            {
                o.Http.Endpoint = new Uri("https://graylog.example.org/gelf");
                o.Http.Tls = new TlsOptions { ClientCertificate = certificate };
            });

            GraylogSinkOptionsValidator.Validate(options);
        }

        [Fact]
        public void Validate_Http_WithBothAClientCertificateAndAPath_Throws()
        {
            using X509Certificate2 certificate = TestCertificates.CreateSelfSigned();
            GraylogSinkOptions options = Options(TransportType.Http, o =>
            {
                o.Http.Endpoint = new Uri("https://graylog.example.org/gelf");
                o.Http.Tls = new TlsOptions
                {
                    ClientCertificate = certificate,
                    ClientCertificatePath = "client.pfx"
                };
            });

            ArgumentException exception = Throws<ArgumentException>(options);

            Assert.Equal("ClientCertificate", exception.ParamName);
            Assert.Contains("HTTP TLS options", exception.Message);
        }

        [Fact]
        public void Validate_Http_WithoutTransportOptions_Throws()
        {
            GraylogSinkOptions options = Options(TransportType.Http, o => o.Http = null!);

            Assert.Equal("Http", Throws<ArgumentNullException>(options).ParamName);
        }

        [Fact]
        public void Validate_Http_WithoutAnEndpoint_Throws()
        {
            GraylogSinkOptions options = Options(TransportType.Http, _ => { });

            Assert.Equal("Endpoint", Throws<ArgumentException>(options).ParamName);
        }

        [Fact]
        public void Validate_Http_WithARelativeEndpoint_Throws()
        {
            GraylogSinkOptions options = Options(TransportType.Http, o => o.Http.Endpoint = new Uri("/gelf", UriKind.Relative));

            Assert.Equal("Endpoint", Throws<ArgumentException>(options).ParamName);
        }

        [Fact]
        public void Validate_Http_WithANonHttpEndpoint_Throws()
        {
            GraylogSinkOptions options = Options(TransportType.Http, o => o.Http.Endpoint = new Uri("ftp://graylog.example.org/gelf"));

            Assert.Equal("Endpoint", Throws<ArgumentException>(options).ParamName);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Validate_Http_WithABlankHeaderName_Throws(string headerName)
        {
            GraylogSinkOptions options = HttpOptions(o => o.Headers = new Dictionary<string, string> { [headerName] = "value" });

            Assert.Equal("Headers", Throws<ArgumentException>(options).ParamName);
        }

        [Theory]
        [InlineData("Content-Type")]
        [InlineData("content-type")]
        public void Validate_Http_WithAHeaderOverridingTheContentType_Throws(string headerName)
        {
            GraylogSinkOptions options = HttpOptions(o => o.Headers = new Dictionary<string, string> { [headerName] = "text/plain" });

            Assert.Equal("Headers", Throws<ArgumentException>(options).ParamName);
        }

        [Fact]
        public void Validate_Http_WithAcceptableHeaders_Accepts()
        {
            GraylogSinkOptions options = HttpOptions(o => o.Headers = new Dictionary<string, string> { ["X-Tenant"] = "scarlet" });

            GraylogSinkOptionsValidator.Validate(options);
        }

        [Theory]
        [InlineData(null, "password")]
        [InlineData("", "password")]
        [InlineData("   ", "password")]
        [InlineData("user", null)]
        public void Validate_Http_WithIncompleteBasicAuthentication_Throws(string? username, string? password)
        {
            GraylogSinkOptions options = HttpOptions(o => o.BasicAuthentication = new HttpBasicAuthenticationOptions
            {
                Username = username,
                Password = password
            });

            Assert.Equal("BasicAuthentication", Throws<ArgumentException>(options).ParamName);
        }

        [Fact]
        public void Validate_Http_WithCompleteBasicAuthentication_Accepts()
        {
            GraylogSinkOptions options = HttpOptions(o => o.BasicAuthentication = new HttpBasicAuthenticationOptions
            {
                Username = "user",
                Password = string.Empty
            });

            GraylogSinkOptionsValidator.Validate(options);
        }

        [Fact]
        public void Validate_Http_WithTlsOptionsAgainstAPlainHttpEndpoint_Throws()
        {
            GraylogSinkOptions options = HttpOptions(o => o.Tls = new TlsOptions());

            ArgumentException exception = Throws<ArgumentException>(options);

            Assert.Equal("Tls", exception.ParamName);
            Assert.Contains("HTTPS endpoint", exception.Message);
        }

        /// <summary>
        /// The HTTP transport takes the name to validate from the endpoint, so a server name set here
        /// would be ignored rather than honoured - which is worth an error instead of a surprise.
        /// </summary>
        [Fact]
        public void Validate_Http_WithATlsServerName_Throws()
        {
            GraylogSinkOptions options = Options(TransportType.Http, o =>
            {
                o.Http.Endpoint = new Uri("https://graylog.example.org/gelf");
                o.Http.Tls = new TlsOptions { ServerName = "graylog.example.org" };
            });

            Assert.Equal("Tls", Throws<ArgumentException>(options).ParamName);
        }

        [Fact]
        public void Validate_Http_WithAClientCertificatePasswordAndNoPath_Throws()
        {
            GraylogSinkOptions options = Options(TransportType.Http, o =>
            {
                o.Http.Endpoint = new Uri("https://graylog.example.org/gelf");
                o.Http.Tls = new TlsOptions { ClientCertificatePassword = "secret" };
            });

            ArgumentException exception = Throws<ArgumentException>(options);

            Assert.Equal("ClientCertificatePassword", exception.ParamName);
            Assert.Contains("HTTP TLS client certificate password", exception.Message);
        }

        [Fact]
        public void Validate_Http_WithATlsClientCertificateOverHttps_Accepts()
        {
            GraylogSinkOptions options = Options(TransportType.Http, o =>
            {
                o.Http.Endpoint = new Uri("https://graylog.example.org/gelf");
                o.Http.Tls = new TlsOptions
                {
                    ClientCertificatePath = "client.pfx",
                    ClientCertificatePassword = "secret"
                };
            });

            GraylogSinkOptionsValidator.Validate(options);
        }

        [Fact]
        public void Validate_Custom_WithoutTransportOptions_Throws()
        {
            GraylogSinkOptions options = Options(TransportType.Custom, o => o.Custom = null!);

            Assert.Equal("Custom", Throws<ArgumentNullException>(options).ParamName);
        }

        [Fact]
        public void Validate_Custom_WithoutAFactory_Throws()
        {
            GraylogSinkOptions options = Options(TransportType.Custom, _ => { });

            Assert.Equal("Factory", Throws<ArgumentException>(options).ParamName);
        }

        [Theory]
        [MemberData(nameof(NonPositiveTimeouts))]
        public void Validate_Http_WithANonPositiveTimeout_Throws(TimeSpan timeout)
        {
            GraylogSinkOptions options = HttpOptions(o => o.Timeout = timeout);

            Assert.Equal("Timeout", Throws<ArgumentOutOfRangeException>(options).ParamName);
        }

        [Theory]
        [MemberData(nameof(NonPositiveTimeouts))]
        public void Validate_Http_WithANonPositiveConnectionLifetime_Throws(TimeSpan connectionLifetime)
        {
            GraylogSinkOptions options = HttpOptions(o => o.ConnectionLifetime = connectionLifetime);

            Assert.Equal("ConnectionLifetime", Throws<ArgumentOutOfRangeException>(options).ParamName);
        }

        [Fact]
        public void Validate_Http_WithoutATimeoutOrConnectionLifetime_Accepts()
        {
            GraylogSinkOptions options = HttpOptions(o =>
            {
                o.Timeout = null;
                o.ConnectionLifetime = null;
            });

            GraylogSinkOptionsValidator.Validate(options);
        }

        [Fact]
        public void Validate_Custom_WithAFactory_Accepts()
        {
            using var transport = new RecordingTransport();
            GraylogSinkOptions options = Options(TransportType.Custom, o => o.Custom.Factory = () => transport);

            GraylogSinkOptionsValidator.Validate(options);
        }

        private static GraylogSinkOptions Options(TransportType transportType, Action<GraylogSinkOptions> configure)
        {
            var options = new GraylogSinkOptions { TransportType = transportType };
            configure(options);

            return options;
        }

        private static GraylogSinkOptions HttpOptions(Action<HttpTransportOptions> configure)
        {
            return Options(TransportType.Http, o =>
            {
                o.Http.Endpoint = new Uri("http://graylog.example.org/gelf");
                configure(o.Http);
            });
        }

        private static T Throws<T>(GraylogSinkOptions options)
            where T : ArgumentException
        {
            return Assert.Throws<T>(() => GraylogSinkOptionsValidator.Validate(options));
        }
    }
}
