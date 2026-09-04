using Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders;
using Serilog.Events;
using Serilog.Parsing;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.MessageBuilders
{
    /// <summary>
    /// Tests that the payload is a well-formed GELF message, whatever the event carries.
    /// </summary>
    /// <remarks>
    /// The builder writes the payload straight out rather than assembling a <c>JsonObject</c> first,
    /// so nothing stops it emitting the same key twice except the field writer keeping track. These
    /// are the cases where it has to.
    /// </remarks>
    public class GelfPayloadShapeFixture
    {
        /// <summary>
        /// The sink adds <c>_stringLevel</c> and <c>_facility</c> itself, and a property can be named
        /// so as to collide with either once the GELF prefix is applied.
        /// </summary>
        /// <remarks>
        /// A duplicate key is not something a JSON writer refuses - both would go out, and which one
        /// Graylog stored would be up to its parser. The configured value is written first and wins.
        /// </remarks>
        [Theory]
        [InlineData("stringLevel", "_stringLevel")]
        [InlineData("facility", "_facility")]
        public void Build_WhenAPropertyCollidesWithAFieldTheSinkAdds_WritesTheKeyOnce(string propertyName, string field)
        {
            var options = new GelfOptions { Facility = "configured-facility" };
            GelfMessageBuilder target = new("localhost", options);

            string payload = target.BuildPayload(
                LogEventSource.GetScalarEvent(propertyName, "from-the-property"));

            Assert.Equal(1, CountKeys(payload, field));
        }

        [Fact]
        public void Build_WhenAPropertyCollidesWithTheFacility_KeepsTheConfiguredValue()
        {
            var options = new GelfOptions { Facility = "configured-facility" };
            GelfMessageBuilder target = new("localhost", options);

            var actual = target.Build(LogEventSource.GetScalarEvent("facility", "from-the-property"));

            Assert.Equal("configured-facility", actual.Text("_facility"));
        }

        /// <summary>
        /// Two properties whose names differ only outside the GELF character set are one field, and
        /// the payload has to say so once.
        /// </summary>
        [Fact]
        public void Build_WhenTwoPropertiesSanitizeToOneName_WritesTheKeyOnce()
        {
            DictionaryValue value = new([
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue("a b"), new ScalarValue("first")),
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue("a/b"), new ScalarValue("second"))
            ]);
            GelfMessageBuilder target = new("localhost", new GelfOptions { ParseArrayValues = true });

            string payload = target.BuildPayload(LogEventSource.GetPropertyEvent("Bag", value));

            Assert.Equal(1, CountKeys(payload, "_Bag.a_b"));
        }

        /// <summary>
        /// A message cut at <see cref="GelfOptions.ShortMessageMaxLength"/> can be cut between the two
        /// halves of a surrogate pair, which is not valid UTF-16 on its own.
        /// </summary>
        /// <remarks>
        /// The rendered message is handed to the writer as a span now rather than as a string, so this
        /// pins that the writer still accepts it. System.Text.Json substitutes the Unicode replacement
        /// character for the orphaned half; what matters is that the event survives and the payload
        /// parses.
        /// </remarks>
        [Fact]
        public void Build_WhenTheShortMessageCutFallsInsideASurrogatePair_StillWritesAValidPayload()
        {
            const int limit = 8;
            // Seven ASCII characters, then an emoji: the cut lands between its two halves.
            string message = new string('a', limit - 1) + "\U0001F680 tail";
            var options = new GelfOptions { ShortMessageMaxLength = limit };
            GelfMessageBuilder target = new("localhost", options);

            var logEvent = new LogEvent(DateTimeOffset.UnixEpoch, LogEventLevel.Information, null,
                new MessageTemplate(message, new List<MessageTemplateToken> { new TextToken(message) }),
                Array.Empty<LogEventProperty>());

            var actual = target.Build(logEvent);

            Assert.Equal(limit, actual.Text("short_message").Length);
            Assert.Equal(message, actual.Text("full_message"));
        }

        /// <summary>
        /// A level outside <see cref="LogEventLevel"/> is clamped to the nearest syslog level rather
        /// than costing the event.
        /// </summary>
        /// <remarks>
        /// <c>LogEventLevel</c> is an <see cref="int"/> underneath and nothing stops a cast, so an
        /// enricher, a custom <c>ILogEventSink</c> pipeline or a hand-built <see cref="LogEvent"/> can
        /// present one. The level map was a dictionary, so such an event was dropped with a
        /// <c>KeyNotFoundException</c> - the level is unrecognized, but the message is not.
        /// </remarks>
        [Theory]
        [InlineData(99, 0)]
        [InlineData(-1, 7)]
        public void Build_WithALevelOutsideTheEnum_ClampsItInsteadOfThrowing(int level, int expected)
        {
            GelfMessageBuilder target = new("localhost", new GelfOptions());

            var logEvent = new LogEvent(DateTimeOffset.UnixEpoch, (LogEventLevel)level, null,
                new MessageTemplate("hello", new List<MessageTemplateToken> { new TextToken("hello") }),
                Array.Empty<LogEventProperty>());

            var actual = target.Build(logEvent);

            Assert.Equal(expected, actual.Value<int>("level"));
            Assert.Equal(level.ToString(), actual.Text("_stringLevel"));
        }

        private static int CountKeys(string payload, string name)
        {
            var count = 0;
            var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(payload));

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1 && reader.ValueTextEquals(name))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
