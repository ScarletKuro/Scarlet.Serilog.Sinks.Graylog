using NSubstitute;
using Scarlet.Serilog.Sinks.Graylog.Core;
using Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders;
using System;
using System.Collections.Generic;
using System.Buffers;
using System.Text.Json;
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

            using var writer = new Utf8JsonWriter(new ArrayBufferWriter<byte>());

            target.WriteGelfJson(simpleEvent, writer);

            errorBuilder.DidNotReceive().Build(simpleEvent, Arg.Any<Utf8JsonWriter>());
            messageBuilder.Received(1).Build(simpleEvent, Arg.Any<Utf8JsonWriter>());
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

            using var writer = new Utf8JsonWriter(new ArrayBufferWriter<byte>());

            target.WriteGelfJson(simpleEvent, writer);

            errorBuilder.Received(1).Build(simpleEvent, Arg.Any<Utf8JsonWriter>());
            messageBuilder.DidNotReceive().Build(simpleEvent, Arg.Any<Utf8JsonWriter>());
        }
    }
}
