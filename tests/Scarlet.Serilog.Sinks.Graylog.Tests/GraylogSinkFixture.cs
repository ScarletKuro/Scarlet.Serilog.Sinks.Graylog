using NSubstitute;
using Newtonsoft.Json.Linq;
using Serilog.Events;
using Serilog.Parsing;
using Scarlet.Serilog.Sinks.Graylog.Core;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests
{
    public class GraylogSinkFixture
    {
        [Fact(Skip = "This test not work anymore because IMessageBuilder gets from internal dictionary")]
        public void WhenEmit_ThenSendData()
        {
            var gelfConverter = Substitute.For<IGelfConverter>();
            var transport = Substitute.For<ITransport>();

            var options = new GraylogSinkOptions
            {
                GelfConverter = gelfConverter,
                TransportType = TransportType.Udp,
                HostnameOrAddress = "localhost"
            };

            GraylogSink target = new(options);

            var logEvent = new LogEvent(DateTimeOffset.Now, LogEventLevel.Fatal, null,
                new MessageTemplate("O_o", new List<MessageTemplateToken>()), new List<LogEventProperty>());

            var jObject = new JObject();
            transport.Send(jObject.ToString(Newtonsoft.Json.Formatting.None)).Returns(Task.CompletedTask);


            //gelfConverter.GetGelfJson(logEvent).Returns(jObject);

            target.Emit(logEvent);

            transport.Received().Send(Arg.Any<string>());
        }
    }
}
