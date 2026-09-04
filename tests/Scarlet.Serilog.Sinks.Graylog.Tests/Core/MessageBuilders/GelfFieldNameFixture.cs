using Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders;
using Serilog.Events;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.MessageBuilders
{
    /// <summary>
    /// Tests for the names the builder gives GELF additional fields.
    /// </summary>
    /// <remarks>
    /// GELF requires additional fields to carry a leading underscore, reserves <c>_id</c>, and verifies
    /// names against <c>^[\w\.\-]*$</c>. Graylog drops a field whose name breaks the character rule
    /// outright, so these are wire-compatibility tests rather than cosmetics.
    /// </remarks>
    public class GelfFieldNameFixture
    {
        /// <summary>
        /// The sequence and dictionary branches wrote the bare property name, so the payload was not a
        /// valid GELF message. Graylog itself strips the prefix and would have taken it either way, but
        /// anything else that speaks GELF is entitled to require it.
        /// </summary>
        [Fact]
        public void Build_SequenceValue_IsUnderscorePrefixed()
        {
            SequenceValue value = new([new ScalarValue(1), new ScalarValue(2)]);

            JsonObject actual = Build("Numbers", value);

            Assert.True(actual.ContainsKey("_Numbers"));
            Assert.False(actual.ContainsKey("Numbers"));
        }

        [Fact]
        public void Build_DictionaryValue_IsUnderscorePrefixed()
        {
            DictionaryValue value = new([
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue("alpha"), new ScalarValue("one"))
            ]);

            JsonObject actual = Build("Bag", value);

            Assert.True(actual.ContainsKey("_Bag"));
            Assert.False(actual.ContainsKey("Bag"));
        }

        /// <summary>
        /// The GELF spec tells libraries not to emit <c>_id</c>, so a property called <c>id</c> has to
        /// move out of the way - in any casing, since a consumer stricter than Graylog may fold it.
        /// The original casing is kept, so <c>Id</c> becomes <c>_Id_</c> rather than <c>_id_</c>.
        /// </summary>
        [Theory]
        [InlineData("id")]
        [InlineData("Id")]
        [InlineData("ID")]
        public void Build_PropertyNamedId_IsRenamed(string propertyName)
        {
            JsonObject actual = Build(propertyName, new ScalarValue(42));
            JsonNode? renamedField = actual[$"_{propertyName}_"];

            Assert.NotNull(renamedField);
            Assert.Equal(42, renamedField.GetValue<int>());
            Assert.False(actual.ContainsKey($"_{propertyName}"));
        }

        /// <summary>
        /// Dictionary keys are arbitrary rendered scalars, so they are the realistic source of a name
        /// GELF would reject.
        /// </summary>
        [Fact]
        public void Build_DictionaryKeysWithIllegalCharacters_AreSanitized()
        {
            DictionaryValue value = new([
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue("my key"), new ScalarValue("one")),
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue("a/b"), new ScalarValue("two")),
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue("kept.name-1"), new ScalarValue("three"))
            ]);

            JsonObject actual = Build("Bag", value, parseArrayValues: true);
            JsonNode? spaceField = actual["_Bag.my_key"];
            JsonNode? slashField = actual["_Bag.a_b"];
            JsonNode? unchangedField = actual["_Bag.kept.name-1"];

            Assert.NotNull(spaceField);
            Assert.Equal("one", spaceField.GetValue<string>());
            Assert.NotNull(slashField);
            Assert.Equal("two", slashField.GetValue<string>());
            // Word characters, dots and dashes are legal and must survive untouched.
            Assert.NotNull(unchangedField);
            Assert.Equal("three", unchangedField.GetValue<string>());
        }

        /// <summary>
        /// Two names that sanitize to the same field must not take the event down with them, and must
        /// not put the same key in the payload twice. The first one written is the one that survives.
        /// </summary>
        /// <remarks>
        /// This used to be last-wins, because the payload was assembled as a <c>JsonObject</c> whose
        /// indexer replaced the earlier value. The payload is now written straight out, and a writer
        /// cannot go back and replace a field it has already emitted - so the later value is dropped
        /// instead of the earlier one. Which of the two survives was arbitrary either way; what the
        /// sink owes the caller is a well-formed message and a live event.
        /// </remarks>
        [Fact]
        public void Build_NamesThatCollideAfterSanitizing_KeepTheFirstAndDoNotThrow()
        {
            DictionaryValue value = new([
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue("a b"), new ScalarValue("first")),
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue("a/b"), new ScalarValue("second"))
            ]);

            JsonObject actual = Build("Bag", value, parseArrayValues: true);
            JsonNode? field = actual["_Bag.a_b"];

            Assert.NotNull(field);
            Assert.Equal("first", field.GetValue<string>());
        }

        [Fact]
        public void Build_DictionaryWhoseNameCollidesWithAnEarlierField_IsIgnored()
        {
            DictionaryValue dictionary = new([
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                    new ScalarValue("key"), new ScalarValue("dictionary-value"))
            ]);
            StructureValue value = new([
                new LogEventProperty("a b", new ScalarValue("first")),
                new LogEventProperty("a/b", dictionary)
            ]);

            JsonObject actual = Build("Bag", value);
            JsonNode? field = actual["_Bag.a_b"];

            Assert.NotNull(field);
            Assert.Equal("first", field.GetValue<string>());
        }

        /// <summary>
        /// A name that already carries the prefix is left alone rather than gaining a second one.
        /// </summary>
        [Fact]
        public void Build_PropertyAlreadyPrefixed_IsNotPrefixedTwice()
        {
            JsonObject actual = Build("_already", new ScalarValue("value"));

            Assert.True(actual.ContainsKey("_already"));
            Assert.False(actual.ContainsKey("__already"));
        }

        /// <summary>
        /// The template field name comes from configuration, so it is user input like any other.
        /// </summary>
        [Fact]
        public void Build_MessageTemplateFieldName_IsSanitizedAndPrefixed()
        {
            var options = new GelfOptions
            {
                IncludeMessageTemplate = true,
                MessageTemplateFieldName = "my template"
            };
            GelfMessageBuilder messageBuilder = new("localhost", options);

            JsonObject actual = messageBuilder.Build(LogEventSource.GetScalarEvent("Val", 1));

            Assert.True(actual.ContainsKey("_my_template"));
        }

        /// <summary>
        /// Graylog sets these fields itself and silently discards an incoming field of the same
        /// name, so they have to move out of the way.
        /// </summary>
        /// <remarks>
        /// Verified against Graylog 6.1: a GELF message carrying <c>_message</c>, <c>_source</c>,
        /// <c>_timestamp</c>, <c>_level</c>, <c>_host</c> or a <c>_gl2_</c>-prefixed field arrives
        /// with those fields missing entirely.
        /// </remarks>
        [Theory]
        [InlineData("message")]
        [InlineData("source")]
        [InlineData("timestamp")]
        [InlineData("level")]
        [InlineData("host")]
        [InlineData("full_message")]
        [InlineData("gl2_custom")]
        public void Build_ReservedPropertyName_IsEscaped(string propertyName)
        {
            JsonObject actual = Build(propertyName, new ScalarValue("value"));

            Assert.True(actual.ContainsKey($"_{propertyName}_"));
            Assert.False(actual.ContainsKey($"_{propertyName}"));
        }

        /// <summary>
        /// Graylog compares those names case-sensitively, so the PascalCase spelling Serilog
        /// properties usually carry is kept as-is rather than churned.
        /// </summary>
        [Theory]
        [InlineData("Message")]
        [InlineData("Source")]
        [InlineData("Timestamp")]
        public void Build_ReservedNameInAnotherCasing_IsLeftAlone(string propertyName)
        {
            JsonObject actual = Build(propertyName, new ScalarValue("value"));

            Assert.True(actual.ContainsKey($"_{propertyName}"));
        }

        /// <summary>
        /// Graylog drops boolean additional fields outright, so they go out as text.
        /// </summary>
        [Theory]
        [InlineData(true, "true")]
        [InlineData(false, "false")]
        public void Build_BooleanScalar_IsWrittenAsText(bool given, string expected)
        {
            JsonObject actual = Build("Flag", new ScalarValue(given));
            JsonNode? field = actual["_Flag"];

            Assert.NotNull(field);
            Assert.Equal(JsonValueKind.String, field.GetValueKind());
            Assert.Equal(expected, field.GetValue<string>());
        }

        private static JsonObject Build(string propertyName, LogEventPropertyValue value, bool parseArrayValues = false)
        {
            var options = new GelfOptions { Facility = "test", ParseArrayValues = parseArrayValues };
            GelfMessageBuilder messageBuilder = new("localhost", options);

            return messageBuilder.Build(LogEventSource.GetPropertyEvent(propertyName, value));
        }
    }
}
