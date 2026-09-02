using Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.MessageBuilders
{
    /// <summary>
    /// Characterization tests for the JSON written for a single GELF additional field.
    /// </summary>
    /// <remarks>
    /// Every expectation here was captured from the reflection-based implementation before the
    /// Native AOT rework, so a failure means the wire format changed for existing consumers.
    /// <para>
    /// The same expectations are asserted twice: once through a normal <see cref="JsonSerializerOptions"/>
    /// (which resolves a contract and serializes exactly as before), and once through options whose
    /// resolver yields nothing, which forces the reflection-free path that Native AOT relies on. That
    /// second run is what proves the two paths agree.
    /// </para>
    /// </remarks>
    public class ScalarFieldFixture
    {
        /// <summary>
        /// Values whose JSON is identical on both the contract-based and reflection-free paths.
        /// </summary>
        public static IEnumerable<object[]> IdenticalOnBothPaths()
        {
            yield return new object[] { "abc", "\"abc\"" };
            yield return new object[] { "a\"b\\c\u00E9", "\"a\\u0022b\\\\c\\u00E9\"" };
            yield return new object[] { false, "false" };
            yield return new object[] { true, "true" };
            yield return new object[] { (byte)7, "7" };
            yield return new object[] { (sbyte)-7, "-7" };
            yield return new object[] { (short)-300, "-300" };
            yield return new object[] { (ushort)300, "300" };
            yield return new object[] { 42, "42" };
            yield return new object[] { 42u, "42" };
            yield return new object[] { -9007199254740993L, "-9007199254740993" };
            yield return new object[] { ulong.MaxValue, "18446744073709551615" };
            yield return new object[] { 'x', "\"x\"" };
            yield return new object[] { '"', "\"\\u0022\"" };
            yield return new object[] { 1.5f, "1.5" };
            yield return new object[] { 0.1f, "0.1" };
            yield return new object[] { 3.14159265358979d, "3.14159265358979" };
            yield return new object[] { 0.1d + 0.2d, "0.30000000000000004" };
            yield return new object[] { 1.500m, "1.500" };
            yield return new object[] { decimal.MaxValue, "79228162514264337593543950335" };
            yield return new object[] { new DateTime(2026, 9, 2, 13, 45, 30, DateTimeKind.Utc), "\"2026-09-02T13:45:30Z\"" };
            yield return new object[] { new DateTime(2026, 9, 2, 13, 45, 30, DateTimeKind.Unspecified), "\"2026-09-02T13:45:30\"" };
            yield return new object[] { new DateTime(2026, 9, 2, 13, 45, 30, 123, DateTimeKind.Utc), "\"2026-09-02T13:45:30.123Z\"" };
            yield return new object[] { TimeSpan.FromMinutes(5), "\"00:05:00\"" };
            yield return new object[] { TimeSpan.FromTicks(-864000000001), "\"-1.00:00:00.0000001\"" };
            yield return new object[] { new TimeSpan(3, 4, 5, 6, 7), "\"3.04:05:06.0070000\"" };
            yield return new object[] { TimeSpan.MaxValue, "\"10675199.02:48:05.4775807\"" };
            yield return new object[] { TimeSpan.Zero, "\"00:00:00\"" };
            yield return new object[] { Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), "\"0f8fad5b-d9cb-469f-a165-70867728950e\"" };
            yield return new object[] { new Uri("https://example.org/a b?x=1"), "\"https://example.org/a b?x=1\"" };
            yield return new object[] { new Uri("/a/b", UriKind.Relative), "\"/a/b\"" };
            yield return new object[] { new DateOnly(2026, 9, 2), "\"2026-09-02\"" };
            yield return new object[] { new TimeOnly(13, 45, 30), "\"13:45:30\"" };
            yield return new object[] { new TimeOnly(1, 2, 3, 456), "\"01:02:03.4560000\"" };
            yield return new object[] { new TimeOnly(0, 0, 0).Add(TimeSpan.FromTicks(1)), "\"00:00:00.0000001\"" };

            // Enums are numeric, which is what System.Text.Json does by default.
            yield return new object[] { LogEventLevel.Warning, "3" };
            yield return new object[] { ByteEnum.Value, "200" };
            yield return new object[] { UlongEnum.Max, "18446744073709551615" };
            yield return new object[] { LongEnum.Negative, "-5" };
            yield return new object[] { (ByteEnum)77, "77" };
        }

        [Theory]
        [MemberData(nameof(IdenticalOnBothPaths))]
        public void Build_WithContractAvailable_WritesExpectedJson(object value, string expected)
        {
            GelfMessageBuilder messageBuilder = new("localhost", OptionsWith(new JsonSerializerOptions()));

            string actual = FieldJson(messageBuilder, value);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [MemberData(nameof(IdenticalOnBothPaths))]
        public void Build_WithoutContractAvailable_WritesSameJsonAsContractPath(object value, string expected)
        {
            GelfMessageBuilder messageBuilder = new("localhost", OptionsWith(NoContracts()));

            string actual = FieldJson(messageBuilder, value);

            Assert.Equal(expected, actual);
        }

        /// <summary>
        /// The one value whose JSON differs between the two paths, and only in how an equivalent
        /// character is escaped.
        /// </summary>
        /// <remarks>
        /// The contract path stores the already-written JSON, so re-serializing escapes the '+' of the
        /// offset as \u002B. The reflection-free path stores a typed <see cref="DateTimeOffset"/>, which
        /// System.Text.Json's own converter writes without escaping. Both decode to the same instant, so
        /// this is asserted semantically rather than byte-for-byte.
        /// </remarks>
        [Fact]
        public void Build_DateTimeOffset_BothPathsDecodeToTheSameInstant()
        {
            DateTimeOffset value = new(2026, 9, 2, 13, 45, 30, TimeSpan.FromHours(3));
            GelfMessageBuilder contractBuilder = new("localhost", OptionsWith(new JsonSerializerOptions()));
            GelfMessageBuilder fallbackBuilder = new("localhost", OptionsWith(NoContracts()));

            string contractJson = FieldJson(contractBuilder, value);
            string fallbackJson = FieldJson(fallbackBuilder, value);

            Assert.Equal("\"2026-09-02T13:45:30\\u002B03:00\"", contractJson);
            Assert.Equal("\"2026-09-02T13:45:30+03:00\"", fallbackJson);
            Assert.Equal(value, JsonSerializer.Deserialize<DateTimeOffset>(contractJson));
            Assert.Equal(value, JsonSerializer.Deserialize<DateTimeOffset>(fallbackJson));
        }

        /// <summary>
        /// System.Text.Json cannot serialize these at all, so before the rework the whole event was lost.
        /// </summary>
        public static IEnumerable<object[]> PreviouslyUnserializable()
        {
            yield return new object[] { (IntPtr)123, "123" };
            yield return new object[] { (UIntPtr)123, "123" };
            yield return new object[] { typeof(GelfMessageBuilder), "\"Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders.GelfMessageBuilder\"" };
        }

        [Theory]
        [MemberData(nameof(PreviouslyUnserializable))]
        public void Build_TypesSystemTextJsonRejects_AreWrittenInsteadOfThrowing(object value, string expected)
        {
            GelfMessageBuilder messageBuilder = new("localhost", OptionsWith(new JsonSerializerOptions()));

            string actual = FieldJson(messageBuilder, value);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void Build_MemberInfo_IsWrittenAsItsSignature()
        {
            GelfMessageBuilder messageBuilder = new("localhost", OptionsWith(new JsonSerializerOptions()));
            MethodInfo value = typeof(string).GetMethod("Trim", Type.EmptyTypes)!;

            string actual = FieldJson(messageBuilder, value);

            Assert.Equal("\"System.String Trim()\"", actual);
        }

        [Fact]
        public void Build_NullScalar_IsWrittenAsJsonNull()
        {
            GelfMessageBuilder messageBuilder = new("localhost", OptionsWith(new JsonSerializerOptions()));
            LogEvent logEvent = LogEventSource.GetScalarEvent("Val", null);

            JsonObject actual = messageBuilder.Build(logEvent);

            Assert.True(actual.ContainsKey("_Val"));
            Assert.Null(actual["_Val"]);
        }

        /// <summary>
        /// A custom converter must keep winning over the reflection-free path.
        /// </summary>
        [Fact]
        public void Build_WithCustomConverter_StillHonoursTheConverter()
        {
            JsonSerializerOptions serializerOptions = new();
            serializerOptions.Converters.Add(new UpperCaseStringConverter());
            GelfMessageBuilder messageBuilder = new("localhost", OptionsWith(serializerOptions));

            string actual = FieldJson(messageBuilder, "abc");

            Assert.Equal("\"ABC\"", actual);
        }

        [Fact]
        public void Build_DictionaryValue_IsWrittenAsAJsonObjectOfRenderedKeys()
        {
            GelfMessageBuilder messageBuilder = new("localhost", OptionsWith(new JsonSerializerOptions()));
            DictionaryValue value = new([
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue("alpha"), new ScalarValue("one")),
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue(2), new ScalarValue("two"))
            ]);
            LogEvent logEvent = LogEventSource.GetPropertyEvent("Val", value);

            JsonObject actual = messageBuilder.Build(logEvent);

            Assert.Equal("{\"alpha\":\"one\",\"2\":\"two\"}", actual.Json("Val"));
        }

        private static GraylogSinkOptions OptionsWith(JsonSerializerOptions serializerOptions)
        {
            return new GraylogSinkOptions
            {
                Facility = "test",
                JsonSerializerOptions = serializerOptions
            };
        }

        /// <summary>
        /// Options whose resolver never yields a contract, which is how the Native AOT path is reached
        /// in-process without publishing.
        /// </summary>
        private static JsonSerializerOptions NoContracts()
        {
            return new JsonSerializerOptions
            {
                TypeInfoResolver = new EmptyTypeInfoResolver()
            };
        }

        private static string FieldJson(GelfMessageBuilder messageBuilder, object value)
        {
            LogEvent logEvent = LogEventSource.GetScalarEvent("Val", value);

            return messageBuilder.Build(logEvent).Json("_Val");
        }

        private sealed class EmptyTypeInfoResolver : IJsonTypeInfoResolver
        {
            public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => null;
        }

        private sealed class UpperCaseStringConverter : System.Text.Json.Serialization.JsonConverter<string>
        {
            public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
                => reader.GetString()!;

            public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
                => writer.WriteStringValue(value.ToUpperInvariant());
        }

        private enum ByteEnum : byte
        {
            Value = 200
        }

        private enum UlongEnum : ulong
        {
            Max = ulong.MaxValue
        }

        private enum LongEnum : long
        {
            Negative = -5
        }
    }
}
