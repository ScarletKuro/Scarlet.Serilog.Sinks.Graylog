using NSubstitute;
using Scarlet.Serilog.Sinks.Graylog.Core;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Http;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Tcp;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp;
using Scarlet.Serilog.Sinks.Graylog.Tests.Fakes;
using System;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core
{
    /// <summary>
    /// Which transport and converter the sink builds for a given set of options.
    /// </summary>
    /// <remarks>
    /// Nothing here connects: every transport builds its client lazily or on first send, so the
    /// selection can be asserted without a Graylog instance.
    /// </remarks>
    public class SinkComponentsBuilderFixture
    {
        [Fact]
        public void MakeTransport_ForUdp_BuildsAUdpTransport()
        {
            var target = new SinkComponentsBuilder(new GraylogSinkOptions
            {
                TransportType = TransportType.Udp,
                Udp = new UdpTransportOptions { Host = "graylog.example.org" }
            });

            using ITransport transport = target.MakeTransport();

            Assert.IsType<UdpTransport>(transport);
        }

        [Fact]
        public void MakeTransport_ForTcp_BuildsATcpTransport()
        {
            var target = new SinkComponentsBuilder(new GraylogSinkOptions
            {
                TransportType = TransportType.Tcp,
                Tcp = new TcpTransportOptions { Host = "graylog.example.org" }
            });

            using ITransport transport = target.MakeTransport();

            Assert.IsType<TcpTransport>(transport);
        }

        [Fact]
        public void MakeTransport_ForHttp_BuildsAnHttpTransport()
        {
            var target = new SinkComponentsBuilder(new GraylogSinkOptions
            {
                TransportType = TransportType.Http,
                Http = new HttpTransportOptions { Endpoint = new Uri("http://graylog.example.org/gelf") }
            });

            using ITransport transport = target.MakeTransport();

            Assert.IsType<HttpTransport>(transport);
        }

        [Fact]
        public void MakeTransport_ForACustomTransport_ReturnsTheFactoryResult()
        {
            using var expected = new RecordingTransport();
            var target = new SinkComponentsBuilder(expected.SinkOptions());

            using ITransport transport = target.MakeTransport();

            Assert.Same(expected, transport);
        }

        /// <summary>
        /// A second guard behind <c>GraylogSinkOptionsValidator</c>, for a factory cleared after the
        /// options were validated.
        /// </summary>
        [Fact]
        public void MakeTransport_ForACustomTransportWithoutAFactory_Throws()
        {
            var target = new SinkComponentsBuilder(new GraylogSinkOptions
            {
                TransportType = TransportType.Custom,
                Custom = new CustomTransportOptions()
            });

            Assert.Throws<InvalidOperationException>(() => target.MakeTransport());
        }

        [Fact]
        public void MakeTransport_ForAnUnsupportedTransport_Throws()
        {
            var target = new SinkComponentsBuilder(new GraylogSinkOptions { TransportType = (TransportType)99 });

            Assert.Throws<ArgumentOutOfRangeException>(() => target.MakeTransport());
        }

        [Fact]
        public void MakeGelfConverter_WithoutAConfiguredConverter_BuildsTheDefault()
        {
            var target = new SinkComponentsBuilder(new GraylogSinkOptions());

            IGelfConverter converter = target.MakeGelfConverter();

            Assert.IsType<GelfConverter>(converter);
        }

        [Fact]
        public void MakeGelfConverter_WithAConfiguredConverter_ReturnsIt()
        {
            var expected = Substitute.For<IGelfConverter>();
            var target = new SinkComponentsBuilder(new GraylogSinkOptions
            {
                Message = new GelfOptions { Converter = expected }
            });

            IGelfConverter converter = target.MakeGelfConverter();

            Assert.Same(expected, converter);
        }
    }
}
