using Scarlet.Serilog.Sinks.Graylog.Core;
using System;
using System.Text.Json;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core
{
    public class GelfConverterFixture
    {
        [Fact]
        public void WriteGelfJson_WithoutAnException_WritesAnOrdinaryMessage()
        {
            GelfConverter target = new("localhost", new GelfOptions(), new JsonSerializerOptions());

            var payload = target.Convert(LogEventSource.GetSimpleLogEvent(DateTimeOffset.Now));

            Assert.False(payload.ContainsKey("_ExceptionType"));
        }

        [Fact]
        public void WriteGelfJson_WithAnException_WritesExceptionFields()
        {
            GelfConverter target = new("localhost", new GelfOptions(), new JsonSerializerOptions());

            var payload = target.Convert(LogEventSource.GetErrorEvent(DateTimeOffset.Now));

            Assert.Equal(typeof(InvalidCastException).FullName, payload["_ExceptionType"]!.GetValue<string>());
        }
    }
}
