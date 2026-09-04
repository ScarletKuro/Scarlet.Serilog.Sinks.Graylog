using Serilog.Events;
using Serilog.Parsing;
using Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.MessageBuilders
{
    public class GelfMessageBuilderFixture
    {
        [Fact]
        public void Constructor_WithoutOptions_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new GelfMessageBuilder("localhost", null!));

            Assert.Equal("options", exception.ParamName);
        }

        [Fact]
        public void InternalConstructor_WithoutOptions_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new GelfMessageBuilder("localhost", null!, new JsonSerializerOptions()));

            Assert.Equal("options", exception.ParamName);
        }

        [Fact]
        public void Constructor_WithoutSerializerOptions_Throws()
        {
            var options = new GelfOptions { JsonSerializerOptions = null! };

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new GelfMessageBuilder("localhost", options));

            Assert.Equal("serializerOptions", exception.ParamName);
        }

        [Fact]
        public void Build_WithoutLogEvent_Throws()
        {
            GelfMessageBuilder messageBuilder = new("localhost", new GelfOptions());
            var buffer = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(buffer);

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => messageBuilder.Build(null!, writer));

            Assert.Equal("logEvent", exception.ParamName);
        }

        [Fact]
        public void Build_WithoutWriter_Throws()
        {
            GelfMessageBuilder messageBuilder = new("localhost", new GelfOptions());
            LogEvent logEvent = LogEventSource.GetSimpleLogEvent(DateTimeOffset.UnixEpoch);

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => messageBuilder.Build(logEvent, null!));

            Assert.Equal("writer", exception.ParamName);
        }

        [Fact]
        public void GetSimpleLogEvent_GraylogSinkOptionsContainsHost_ReturnsOptionsHost()
        {
            GelfOptions options = new()
            {
                HostnameOverride = "my_host"
            };
            GelfMessageBuilder messageBuilder = new("localhost", options);

            LogEvent logEvent = LogEventSource.GetSimpleLogEvent(DateTime.UtcNow);
            var host = messageBuilder.Build(logEvent)["host"];

            Assert.NotNull(host);
            Assert.Equal("my_host", host.AsValue().ToString());
        }

        [Fact]
        public static void WhenTryCreateLogEventWithNullKeyOrValue_ThenThrow()
        {
            //If in future this test fail then should add check for null in GelfMessageBuilder
            Assert.Throws<ArgumentNullException>(() =>
            {
                _ = new LogEvent(DateTimeOffset.Now, LogEventLevel.Information, null,
                    new MessageTemplate("abcdef{TestProp}", new List<MessageTemplateToken>
                    {
                        new TextToken("abcdef"),
                        new PropertyToken("TestProp", "zxc", alignment: new Alignment(AlignmentDirection.Left, 3))

                    }), new List<LogEventProperty>
                    {
                        new("TestProp", new ScalarValue("zxc")),
                        new("id", new ScalarValue("asd")),
                        new("Oo", null!),
                        new(null!, null!),
                        new("StructuredProperty",
                            new StructureValue(new List<LogEventProperty>
                            {
                                new("id", new ScalarValue(1)),
                                new("_TestProp", new ScalarValue(3)),
                            }, "TypeTag"))
                    });
            });
        }
    }
}
