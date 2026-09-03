using Serilog;
using Microsoft.Extensions.Configuration;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Tests.Fakes;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests
{
    public class LoggerConfigurationGrayLogExtensionsFixture
    {
        [Fact]
        public void CanApplyExtension()
        {
            var loggerConfig = new LoggerConfiguration();

            loggerConfig.WriteTo.Graylog(new GraylogSinkOptions
            {
                Delivery = new DeliveryOptions { MinimumLevel = LogEventLevel.Information },
                Message = new GelfOptions { Facility = "VolkovTestFacility" },
                Udp = new UdpTransportOptions { Host = "localhost", Port = 12201 }
            });

            var logger = loggerConfig.CreateLogger();
            Assert.NotNull(logger);
        }

        [Fact]
        public void OptionsObjectIsTheOnlyPublicGraylogConfigurationOverload()
        {
            var loggerConfig = new LoggerConfiguration();

            loggerConfig.WriteTo.Graylog(new GraylogSinkOptions
            {
                TransportType = TransportType.Udp,
                Udp = new UdpTransportOptions { Host = "localhost", Port = 12201 },
                Delivery = new DeliveryOptions { MinimumLevel = LogEventLevel.Information }
            });

            var logger = loggerConfig.CreateLogger();
            Assert.NotNull(logger);
        }

        /// <summary>
        /// A configuration whose Graylog args include "host" has to bind: Serilog.Settings.Configuration
        /// will not pick a candidate method at all if the JSON supplies an argument no parameter matches,
        /// and it reports that to SelfLog rather than throwing - so a renamed parameter turns the sink
        /// off silently. <see cref="ThereIsExactlyOneGraylogConvenienceOverload"/> guards the shape of
        /// the overload that has to match.
        /// </summary>
        /// <remarks>
        /// The configured sink points at a port nothing listens on, and JSON cannot supply a
        /// <c>TransportFactory</c>, so delivery is out of reach here. That the "host" argument reaches
        /// the payload as GELF's host field is covered by
        /// <c>GelfMessageBuilderFixture.GetSimpleLogEvent_GraylogSinkOptionsContainsHost_ReturnsOptionsHost</c>.
        /// </remarks>
        [Fact]
        public void CanReadHostPropertyConfiguration()
        {
            IConfigurationRoot configuration = ConfigurationFromResource(
                "Scarlet.Serilog.Sinks.Graylog.Tests.Configurations.AppSettingsWithGraylogSinkContainingHostProperty.json");

            using Logger logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();

            Assert.NotNull(logger);
            Assert.Null(Record.Exception(() => logger.Information("Hello {ApplicationName}.", "SerilogGraylogSink")));
        }

        [Fact]
        public void WithoutBatchingArguments_EventsAreWrittenImmediately()
        {
            var transport = new RecordingTransport();

            using var logger = new LoggerConfiguration().WriteTo.Graylog(transport.SinkOptions()).CreateLogger();
            logger.Information("hello");

            Assert.Single(transport.Payloads);
        }

        [Fact]
        public void WithBatching_EventsAreHeldUntilTheLoggerIsDisposed()
        {
            var transport = new RecordingTransport();

            GraylogSinkOptions options = transport.SinkOptions(o => o.Delivery.Batching = new BatchingOptions
            {
                EagerlyEmitFirstEvent = false,
                BufferingTimeLimit = TimeSpan.FromMinutes(5)
            });

            var logger = new LoggerConfiguration().WriteTo.Graylog(options).CreateLogger();

            logger.Information("hello");
            Assert.Empty(transport.Payloads);

            logger.Dispose();
            Assert.Single(transport.Payloads);
        }

        [Fact]
        public void ThereIsExactlyOneGraylogOptionsOverload()
        {
            // Two convenience overloads starting (string, int, TransportType, ...) with everything
            // after optional would be ambiguous at the call site, and would also make
            // Serilog.Settings.Configuration unable to pick a candidate from JSON args.
            var graylogMethods = typeof(GraylogSink).Assembly.GetTypes()
                .Where(t => t is { IsSealed: true, IsAbstract: true })
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(m => m.Name == "Graylog"
                            && m.IsDefined(typeof(ExtensionAttribute), false)
                            && m.GetParameters()[0].ParameterType == typeof(LoggerSinkConfiguration))
                .ToList();

            Assert.Single(graylogMethods);
            Assert.Equal(typeof(GraylogSinkOptions), graylogMethods[0].GetParameters()[1].ParameterType);
        }

        [Fact]
        public void CanReadBatchedConfiguration()
        {
            IConfigurationRoot configuration = ConfigurationFromResource(
                "Scarlet.Serilog.Sinks.Graylog.Tests.Configurations.AppSettingsWithBatchedGraylogSink.json");

            using var logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();

            Assert.NotNull(logger);
        }

        /// <summary>
        /// Reads an embedded JSON configuration file. <see cref="Assembly.GetManifestResourceStream(string)"/>
        /// returns null when the resource is not embedded, which would otherwise surface as an
        /// unexplained NullReferenceException if one of the files were renamed.
        /// </summary>
        private static IConfigurationRoot ConfigurationFromResource(string resourceName)
        {
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");

            return new ConfigurationBuilder()
                .AddJsonStream(stream)
                .Build();
        }
    }
}
