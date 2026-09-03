using Serilog.Events;
using Serilog.Parsing;
using Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders;
using System;
using System.Collections.Generic;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.MessageBuilders
{
    public class GelfMessageBuilderFixture
    {
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
