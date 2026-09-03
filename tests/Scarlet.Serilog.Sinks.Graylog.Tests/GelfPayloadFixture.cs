using Scarlet.Serilog.Sinks.Graylog.Tests.Fakes;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Exceptions;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Scarlet.Serilog.Sinks.Graylog.Core;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests
{
    /// <summary>
    /// Tests for the GELF payload a configured logger actually puts on the wire.
    /// </summary>
    /// <remarks>
    /// These replace the IntegrateSinkTestWith{Udp,Tcp,Http} fixtures, which logged to hostnames that
    /// have not resolved for years and asserted nothing at all. Every test here drives the real
    /// pipeline - Serilog's property capturing, <see cref="GelfConverter"/>, the message builders
    /// and <see cref="GraylogSink.Emit"/> - and captures the payload through
    /// <see cref="RecordingTransport"/> rather than a socket, so the assertions below describe the
    /// wire format rather than merely proving nothing threw.
    /// </remarks>
    public class GelfPayloadFixture
    {
        [Fact]
        public void Payload_CarriesTheGelfEnvelopeFields()
        {
            var transport = new RecordingTransport();

            using (Logger logger = LoggerFor(transport, o => o.Message.Facility = "edox-accounts"))
            {
                logger.Error("boom");
            }

            JsonObject payload = SinglePayload(transport);

            Assert.Equal("1.1", payload.Text("version"));
            Assert.Equal("edox-accounts", payload.Text("_facility"));
            Assert.Equal("boom", payload.Text("short_message"));
            Assert.Equal("Error", payload.Text("_stringLevel"));
            // GELF carries syslog severities, not Serilog levels: Error is 3.
            Assert.Equal(3, payload.Value<int>("level"));
            Assert.False(string.IsNullOrEmpty(payload.Text("host")));

            // full_message only appears when the rendered message did not fit short_message.
            Assert.False(payload.ContainsKey("full_message"));
        }

        /// <summary>
        /// No facility configured means no facility field, not a field explicitly set to null: Graylog
        /// stores the null, so every event carried an empty _facility that nobody set and nothing can
        /// usefully search on.
        /// </summary>
        [Fact]
        public void Payload_WithoutAConfiguredFacility_OmitsTheField()
        {
            var transport = new RecordingTransport();

            using (Logger logger = LoggerFor(transport))
            {
                logger.Error("boom");
            }

            JsonObject payload = SinglePayload(transport);

            Assert.False(payload.ContainsKey("_facility"));
        }

        [Fact]
        public void Timestamp_IsWrittenAsUnixSeconds()
        {
            var transport = new RecordingTransport();

            using (var sink = new GraylogSink(transport.SinkOptions()))
            {
                // Emitted straight to the sink because a logger will not let the timestamp be chosen.
                sink.Emit(LogEventSource.GetScalarEvent("Val", 1, DateTimeOffset.UnixEpoch));
            }

            Assert.Equal(0d, SinglePayload(transport).Value<double>("timestamp"));
        }

        [Fact]
        public void ShortMessageMaxLength_TruncatesShortMessageAndKeepsTheWholeTextInFullMessage()
        {
            var transport = new RecordingTransport();
            string message = new string('x', 120);

            using (Logger logger = LoggerFor(transport, o => o.Message.ShortMessageMaxLength = 50))
            {
                logger.Information(message);
            }

            JsonObject payload = SinglePayload(transport);

            Assert.Equal(message.Substring(0, 50), payload.Text("short_message"));
            Assert.Equal(message, payload.Text("full_message"));
        }

        [Fact]
        public void MinimumLogEventLevel_KeepsEventsBelowTheLevelOffTheTransport()
        {
            var transport = new RecordingTransport();

            // Verbose on the logger, so the sink's own level restriction is what does the filtering.
            using (Logger logger = new LoggerConfiguration()
                       .MinimumLevel.Verbose()
                       .WriteTo.Graylog(transport.SinkOptions(o => o.Delivery.MinimumLevel = LogEventLevel.Information))
                       .CreateLogger())
            {
                logger.Debug("dropped");
                Assert.Empty(transport.Payloads);

                logger.Error("kept");
            }

            Assert.Equal("kept", SinglePayload(transport).Text("short_message"));
        }

        [Fact]
        public void DestructuredObject_IsFlattenedIntoDotDelimitedAdditionalFields()
        {
            var transport = new RecordingTransport();

            using (Logger logger = LoggerFor(transport))
            {
                logger.Information("complex {@test}",
                    new { Id = 1, Flag = true, Bar = new { Prop = "whirlwind" } });
            }

            JsonObject payload = SinglePayload(transport);

            Assert.Equal(1, payload.Value<int>("_test.Id"));
            // Booleans go out as text, because Graylog drops boolean additional fields.
            Assert.Equal("true", payload.Text("_test.Flag"));
            Assert.Equal("whirlwind", payload.Text("_test.Bar.Prop"));

            // A structure contributes its leaves only; there is no field for the structure itself.
            Assert.False(payload.ContainsKey("_test"));
        }

        [Fact]
        public void TopLevelIdProperty_IsRenamedSoItCannotCollideWithGraylogsOwnIdField()
        {
            var transport = new RecordingTransport();

            using (Logger logger = LoggerFor(transport))
            {
                logger.Information("an event with an {id}", 42);
            }

            JsonObject payload = SinglePayload(transport);

            Assert.False(payload.ContainsKey("_id"));
            Assert.Equal(42, payload.Value<int>("_id_"));
        }

        [Fact]
        public void ParseArrayValues_WhenTrue_WritesTheRenderedSequenceAndEveryElement()
        {
            var transport = new RecordingTransport();

            using (Logger logger = LoggerFor(transport, o => o.Message.ParseArrayValues = true))
            {
                logger.Information("bars {@Bars}", (object)new[]
                {
                    new { Id = 1, Prop = "one" },
                    new { Id = 2, Prop = "two" }
                });
            }

            JsonObject payload = SinglePayload(transport);

            // The sequence itself is rendered as text alongside the expanded elements.
            Assert.False(string.IsNullOrEmpty(payload.Text("_Bars")));

            Assert.Equal(1, payload.Value<int>("_Bars.0.Id"));
            Assert.Equal("one", payload.Text("_Bars.0.Prop"));
            Assert.Equal(2, payload.Value<int>("_Bars.1.Id"));
            Assert.Equal("two", payload.Text("_Bars.1.Prop"));
        }

        [Fact]
        public void ParseArrayValues_WhenFalse_WritesOnlyTheRenderedSequence()
        {
            var transport = new RecordingTransport();

            using (Logger logger = LoggerFor(transport))
            {
                logger.Information("bars {@Bars}", (object)new[] { new { Id = 1, Prop = "one" } });
            }

            JsonObject payload = SinglePayload(transport);

            Assert.True(payload.ContainsKey("_Bars"));
            Assert.False(payload.ContainsKey("_Bars.0.Id"));
        }

        [Fact]
        public void DictionaryValue_WhenParseArrayValuesIsFalse_IsWrittenAsANestedObjectOfRenderedKeys()
        {
            var transport = new RecordingTransport();

            using (Logger logger = LoggerFor(transport))
            {
                logger.Information("response {@CommandResponse}",
                    (object)new Dictionary<int, string> { [0] = "zero", [1] = "one" });
            }

            JsonObject payload = SinglePayload(transport);

            Assert.Equal("{\"0\":\"zero\",\"1\":\"one\"}", payload.Json("_CommandResponse"));
        }

        [Fact]
        public void DictionaryValue_WhenParseArrayValuesIsTrue_IsExpandedIntoOneFieldPerEntry()
        {
            var transport = new RecordingTransport();

            using (Logger logger = LoggerFor(transport, o => o.Message.ParseArrayValues = true))
            {
                logger.Information("response {@CommandResponse}",
                    (object)new Dictionary<int, string> { [0] = "zero", [1] = "one" });
            }

            JsonObject payload = SinglePayload(transport);

            Assert.Equal("zero", payload.Text("_CommandResponse.0"));
            Assert.Equal("one", payload.Text("_CommandResponse.1"));
        }

        [Fact]
        public void IncludeMessageTemplate_AddsTheTemplateUnderTheDefaultFieldName()
        {
            var transport = new RecordingTransport();

            using (Logger logger = LoggerFor(transport, o => o.Message.IncludeMessageTemplate = true))
            {
                logger.Information("battle profile: {Name}", "Volkov");
            }

            Assert.Equal("battle profile: {Name}", SinglePayload(transport).Text("_message_template"));
        }

        [Fact]
        public void MessageTemplateFieldName_RenamesTheTemplateField()
        {
            var transport = new RecordingTransport();

            using (Logger logger = LoggerFor(transport, o =>
                   {
                       o.Message.IncludeMessageTemplate = true;
                       o.Message.MessageTemplateFieldName = "template";
                   }))
            {
                logger.Information("battle profile: {Name}", "Volkov");
            }

            JsonObject payload = SinglePayload(transport);

            Assert.Equal("battle profile: {Name}", payload.Text("_template"));
            Assert.False(payload.ContainsKey("_message_template"));
        }

        [Fact]
        public void ExcludeMessageTemplateProperties_OmitsThePropertiesNamedInTheTemplate()
        {
            var transport = new RecordingTransport();

            using (Logger logger = LoggerFor(transport, o => o.Message.ExcludeMessageTemplateProperties = true))
            {
                logger.ForContext("Enriched", "kept").Information("hello {Named}", "dropped");
            }

            JsonObject payload = SinglePayload(transport);

            Assert.False(payload.ContainsKey("_Named"));
            Assert.Equal("kept", payload.Text("_Enriched"));
        }

        [Fact]
        public void JsonSerializerOptions_ConvertersApplyToValuesInsideDestructuredObjects()
        {
            var transport = new RecordingTransport();

            using (Logger logger = LoggerFor(transport, o => o.Message.JsonSerializerOptions = new JsonSerializerOptions
                   {
                       Converters = { new JsonStringEnumConverter() }
                   }))
            {
                logger.Information("level {@payload}", new { Level = LogEventLevel.Warning });
            }

            // Numeric without the converter; see ScalarFieldFixture for the default enum encoding.
            Assert.Equal("Warning", SinglePayload(transport).Text("_payload.Level"));
        }

        [Fact]
        public void Exception_IsRoutedToTheExceptionBuilderAndFlattenedThroughTheNestedExceptions()
        {
            var transport = new RecordingTransport();

            using (Logger logger = LoggerFor(transport))
            {
                logger.Error(NestedException(), "test exception with object {@test}", new { Id = 1 });
            }

            JsonObject payload = SinglePayload(transport);

            Assert.Equal("Nested Exception - Level One exception", payload.Text("_ExceptionMessage"));
            Assert.Equal("System.NotImplementedException", payload.Text("_ExceptionType"));
            Assert.Equal(typeof(GelfPayloadFixture).Assembly.GetName().Name, payload.Text("_ExceptionSource"));
            Assert.Contains("--- Inner exception stack trace ---", payload.Text("_StackTrace"));

            // The exception builder delegates to the message builder, so ordinary properties survive.
            Assert.Equal(1, payload.Value<int>("_test.Id"));
        }

        /// <summary>
        /// Serilog.Exceptions contributes its detail as a dictionary property, which the sink writes
        /// as a nested JSON object of rendered values under an un-prefixed key - not as flattened
        /// underscore-delimited fields the way a destructured object is.
        /// </summary>
        [Fact]
        public void SerilogExceptionsEnricher_DetailIsWrittenAsANestedObject()
        {
            var transport = new RecordingTransport();

            using (Logger logger = new LoggerConfiguration()
                       .Enrich.WithExceptionDetails()
                       .WriteTo.Graylog(transport.SinkOptions())
                       .CreateLogger())
            {
                logger.Error(new InvalidOperationException("Test exception"), "failed");
            }

            JsonObject payload = SinglePayload(transport);

            JsonObject detail = payload.Object("_ExceptionDetail");

            Assert.Equal("System.InvalidOperationException", detail.Text("Type"));
            Assert.Equal("Test exception", detail.Text("Message"));
        }

        private static Logger LoggerFor(RecordingTransport transport, Action<GraylogSinkOptions>? configure = null)
        {
            return new LoggerConfiguration()
                .WriteTo.Graylog(transport.SinkOptions(configure))
                .CreateLogger();
        }

        private static JsonObject SinglePayload(RecordingTransport transport)
        {
            JsonNode? payload = JsonNode.Parse(Assert.Single(transport.Payloads));

            Assert.NotNull(payload);

            return payload.AsObject();
        }

        /// <summary>
        /// A thrown-and-caught exception with a thrown-and-caught inner exception, so both carry a
        /// real stack trace.
        /// </summary>
        private static Exception NestedException()
        {
            try
            {
                try
                {
                    throw new InvalidOperationException("Level One exception");
                } catch (Exception inner)
                {
                    throw new NotImplementedException("Nested Exception", inner);
                }
            } catch (Exception outer)
            {
                return outer;
            }
        }
    }
}
