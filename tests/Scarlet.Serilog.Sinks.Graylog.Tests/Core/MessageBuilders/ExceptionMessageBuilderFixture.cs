using Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders;
using System;
using System.Text.Json.Nodes;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.MessageBuilders
{
    /// <summary>
    /// Tests for the additional fields <see cref="ExceptionMessageBuilder"/> contributes on top of an
    /// ordinary GELF message.
    /// </summary>
    /// <remarks>
    /// The routing to this builder and the resulting payload are covered end-to-end by
    /// <see cref="GelfPayloadFixture"/>; what is unique here is <c>StackTraceDepth</c>, which decides
    /// how far the inner exception chain is walked. The test this replaced never called
    /// <see cref="ExceptionMessageBuilder.Build"/> at all and asserted nothing, despite being named
    /// for what Build does.
    /// </remarks>
    public class ExceptionMessageBuilderFixture
    {
        [Fact]
        public void Build_JoinsTheMessagesOfTheWholeExceptionChain()
        {
            JsonObject actual = Build(LogEventSource.NestedException(3));

            Assert.Equal("Level 3 exception - Level 2 exception - Level 1 exception", actual.Text("_ExceptionMessage"));
            Assert.Equal("System.InvalidOperationException", actual.Text("_ExceptionType"));
            Assert.Contains("--- Inner exception stack trace ---", actual.Text("_StackTrace"));
        }

        [Theory]
        [InlineData(1, "Level 3 exception")]
        [InlineData(2, "Level 3 exception - Level 2 exception")]
        [InlineData(3, "Level 3 exception - Level 2 exception - Level 1 exception")]
        // Deeper than the chain is not an error: the walk also stops when it runs out of inner exceptions.
        [InlineData(10, "Level 3 exception - Level 2 exception - Level 1 exception")]
        public void Build_StackTraceDepth_LimitsHowFarTheInnerExceptionChainIsWalked(int depth, string expected)
        {
            JsonObject actual = Build(LogEventSource.NestedException(3), o => o.StackTraceDepth = depth);

            Assert.Equal(expected, actual.Text("_ExceptionMessage"));
        }

        [Fact]
        public void Build_WhenTheExceptionWasNeverThrown_LeavesTheStackTraceFieldNull()
        {
            JsonObject actual = Build(new InvalidOperationException("never thrown"));

            Assert.Equal("never thrown", actual.Text("_ExceptionMessage"));
            Assert.True(actual.ContainsKey("_StackTrace"));
            Assert.Null(actual["_StackTrace"]);
        }

        private static JsonObject Build(Exception exception, Action<GelfOptions>? configure = null)
        {
            var options = new GelfOptions();

            configure?.Invoke(options);

            ExceptionMessageBuilder target = new("localhost", options);

            return target.Build(LogEventSource.GetExceptionLogEvent(DateTimeOffset.UnixEpoch, exception));
        }
    }
}
