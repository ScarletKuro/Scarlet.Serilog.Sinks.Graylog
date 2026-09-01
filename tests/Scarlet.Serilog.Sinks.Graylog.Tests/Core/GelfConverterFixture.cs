using NSubstitute;
using Scarlet.Serilog.Sinks.Graylog.Core;
using Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders;
using Scarlet.Serilog.Sinks.Graylog.Tests;
using System;
using System.Collections.Generic;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core
{
    public class GelfConverterFixture
    {
        [Fact]
        public void WhenLogEvent_ThenMessageBuilderShouldBeCalled()
        {
            var errorBuilder = Substitute.For<IMessageBuilder>();
            var messageBuilder = Substitute.For<IMessageBuilder>();

            var messageBuilders = new Dictionary<BuilderType, Lazy<IMessageBuilder>>
            {
                [BuilderType.Exception] = new Lazy<IMessageBuilder>(() => errorBuilder),
                [BuilderType.Message] = new Lazy<IMessageBuilder>(() => messageBuilder)
            };

            GelfConverter target = new(messageBuilders);

            var simpleEvent = LogEventSource.GetSimpleLogEvent(DateTimeOffset.Now);

            target.GetGelfJson(simpleEvent);

            errorBuilder.DidNotReceive().Build(simpleEvent);
            messageBuilder.Received(1).Build(simpleEvent);
        }

        [Fact]
        public void WhenLogErrorEvent_ThenErrorMessageBuilderShouldBeCalled()
        {
            var errorBuilder = Substitute.For<IMessageBuilder>();
            var messageBuilder = Substitute.For<IMessageBuilder>();

            var messageBuilders = new Dictionary<BuilderType, Lazy<IMessageBuilder>>
            {
                [BuilderType.Exception] = new Lazy<IMessageBuilder>(() => errorBuilder),
                [BuilderType.Message] = new Lazy<IMessageBuilder>(() => messageBuilder)
            };

            GelfConverter target = new(messageBuilders);

            var simpleEvent = LogEventSource.GetErrorEvent(DateTimeOffset.Now);

            target.GetGelfJson(simpleEvent);

            errorBuilder.Received(1).Build(simpleEvent);
            messageBuilder.DidNotReceive().Build(simpleEvent);
        }
    }
}
