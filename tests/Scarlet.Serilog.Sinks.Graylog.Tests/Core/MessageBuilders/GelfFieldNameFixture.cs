using Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders;
using Serilog.Events;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.MessageBuilders
{
    /// <summary>
    /// Tests for the names the builder gives GELF additional fields.
    /// </summary>
    /// <remarks>
    /// Graylog only promotes underscore-prefixed fields to additional fields, reserves <c>_id</c>, and
    /// verifies names against <c>^[\w\.\-]*$</c>. A field that breaks any of those is dropped on the
    /// server, silently, so these are wire-compatibility tests rather than cosmetics.
    /// </remarks>
    public class GelfFieldNameFixture
    {
        /// <summary>
        /// The sequence and dictionary branches used to write the bare property name, so an array or
        /// dictionary property never became an additional field at all - and with the default
        /// <c>ParseArrayValues = false</c> it was the only representation, so the value was lost.
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
        /// GELF reserves <c>_id</c>, so a property called <c>id</c> has to move out of the way.
        /// </summary>
        [Theory]
        [InlineData("id")]
        [InlineData("Id")]
        [InlineData("ID")]
        public void Build_PropertyNamedId_IsRenamed(string propertyName)
        {
            JsonObject actual = Build(propertyName, new ScalarValue(42));

            Assert.Equal(42, actual["_id_"]!.GetValue<int>());
            Assert.False(actual.ContainsKey("_id"));
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

            Assert.Equal("one", actual["_Bag.my_key"]!.GetValue<string>());
            Assert.Equal("two", actual["_Bag.a_b"]!.GetValue<string>());
            // Word characters, dots and dashes are legal and must survive untouched.
            Assert.Equal("three", actual["_Bag.kept.name-1"]!.GetValue<string>());
        }

        /// <summary>
        /// Two names that sanitize to the same field must not take the event down with them:
        /// <c>JsonObject.Add</c> throws on a duplicate key, so the write has to be last-wins.
        /// </summary>
        [Fact]
        public void Build_NamesThatCollideAfterSanitizing_DoNotThrow()
        {
            DictionaryValue value = new([
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue("a b"), new ScalarValue("first")),
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue("a/b"), new ScalarValue("second"))
            ]);

            JsonObject actual = Build("Bag", value, parseArrayValues: true);

            Assert.Equal("second", actual["_Bag.a_b"]!.GetValue<string>());
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

        private static JsonObject Build(string propertyName, LogEventPropertyValue value, bool parseArrayValues = false)
        {
            var options = new GelfOptions { Facility = "test", ParseArrayValues = parseArrayValues };
            GelfMessageBuilder messageBuilder = new("localhost", options);

            return messageBuilder.Build(LogEventSource.GetPropertyEvent(propertyName, value));
        }
    }
}
