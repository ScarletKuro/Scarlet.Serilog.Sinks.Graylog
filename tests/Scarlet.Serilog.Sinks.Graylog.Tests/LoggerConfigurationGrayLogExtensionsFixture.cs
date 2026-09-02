using Serilog;
using Microsoft.Extensions.Configuration;
using Serilog.Configuration;
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
                MinimumLogEventLevel = LogEventLevel.Information,
                Facility = "VolkovTestFacility",
                HostnameOrAddress = "localhost",
                Port = 12201
            });

            var logger = loggerConfig.CreateLogger();
            Assert.NotNull(logger);
        }

        [Fact]
        public void CanApplyExtensionWithIntegralParameterTypes()
        {
            var loggerConfig = new LoggerConfiguration();

            loggerConfig.WriteTo.Graylog("localhost", 12201, TransportType.Udp, false,
                LogEventLevel.Information);

            var logger = loggerConfig.CreateLogger();
            Assert.NotNull(logger);
        }

        //[Fact(Skip="Integration test")]
        [Fact]
        public void CanReadHostPropertyConfiguration()
        {
            //arrange
            IConfigurationRoot configuration = ConfigurationFromResource(
                "Scarlet.Serilog.Sinks.Graylog.Tests.Configurations.AppSettingsWithGraylogSinkContainingHostProperty.json");

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();

            //act
            Log.Information("Hello {ApplicationName}.", "SerilogGraylogSink");

            //assert
        }

        [Fact]
        public void WithoutBatchingArguments_EventsAreWrittenImmediately()
        {
            var transport = new RecordingTransport();

            using (var logger = new LoggerConfiguration().WriteTo.Graylog(OptionsFor(transport)).CreateLogger())
            {
                logger.Information("hello");

                Assert.Single(transport.Payloads);
            }
        }

        [Fact]
        public void WithBatching_EventsAreHeldUntilTheLoggerIsDisposed()
        {
            var transport = new RecordingTransport();

            var options = OptionsFor(transport);
            options.Batching = new BatchingOptions
            {
                EagerlyEmitFirstEvent = false,
                BufferingTimeLimit = TimeSpan.FromMinutes(5)
            };

            var logger = new LoggerConfiguration().WriteTo.Graylog(options).CreateLogger();

            logger.Information("hello");
            Assert.Empty(transport.Payloads);

            logger.Dispose();
            Assert.Single(transport.Payloads);
        }

        [Fact]
        public void ThereIsExactlyOneGraylogConvenienceOverload()
        {
            // Two convenience overloads starting (string, int, TransportType, ...) with everything
            // after optional would be ambiguous at the call site, and would also make
            // Serilog.Settings.Configuration unable to pick a candidate from JSON args.
            var graylogMethods = typeof(GraylogSink).Assembly.GetTypes()
                .Where(t => t.IsSealed && t.IsAbstract)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(m => m.Name == "Graylog"
                            && m.IsDefined(typeof(ExtensionAttribute), false)
                            && m.GetParameters()[0].ParameterType == typeof(LoggerSinkConfiguration))
                .ToList();

            Assert.Equal(2, graylogMethods.Count);
            Assert.Equal(1, graylogMethods.Count(m => m.GetParameters().Length > 2));
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

        private static GraylogSinkOptions OptionsFor(ITransport transport)
        {
            return new GraylogSinkOptions
            {
                HostnameOrAddress = "localhost",
                Port = 12201,
                TransportType = TransportType.Custom,
                TransportFactory = () => transport
            };
        }
    }
}
