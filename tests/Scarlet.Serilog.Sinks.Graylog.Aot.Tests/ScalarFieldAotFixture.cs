using Serilog.Events;
using System;
using System.Collections.Generic;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Aot.Tests
{
    /// <summary>
    /// One case per scalar type Serilog can hand a sink, with the JSON captured from the
    /// reflection-based implementation.
    /// </summary>
    /// <remarks>
    /// Each case pins one arm of <c>ScalarJsonWriter.WriteWithoutReflection</c> to the byte-for-byte
    /// output of the reflection-based path, which is the whole contract that path has to honour.
    /// <para>
    /// Only the label crosses the theory-data boundary. xUnit cannot serialize an <see cref="IntPtr"/>,
    /// a <see cref="Type"/>, a <see cref="System.Reflection.MethodInfo"/> or a private enum, and test
    /// cases originating in Native AOT carry no serialization at all, so the values stay in
    /// <see cref="Cases"/> and the theory looks them up by name.
    /// </para>
    /// </remarks>
    public class ScalarFieldAotFixture
    {
        private static readonly Dictionary<string, (object? Value, string Expected)> Cases =
            new Dictionary<string, (object? Value, string Expected)>
            {
                ["string"] = ("abc", "\"abc\""),
                ["string escaped"] = ("a\"b\\cé", "\"a\\u0022b\\\\c\\u00E9\""),
                ["bool"] = (true, "true"),
                ["byte"] = ((byte)7, "7"),
                ["sbyte"] = ((sbyte)-7, "-7"),
                ["short"] = ((short)-300, "-300"),
                ["ushort"] = ((ushort)300, "300"),
                ["int"] = (42, "42"),
                ["uint"] = (42u, "42"),
                ["long"] = (-9007199254740993L, "-9007199254740993"),
                ["ulong"] = (ulong.MaxValue, "18446744073709551615"),
                ["char"] = ('x', "\"x\""),
                ["float"] = (1.5f, "1.5"),
                ["float precision"] = (0.1f, "0.1"),
                ["double"] = (3.14159265358979d, "3.14159265358979"),
                ["double rounding"] = (0.1d + 0.2d, "0.30000000000000004"),
                ["decimal"] = (1.500m, "1.500"),
                ["decimal max"] = (decimal.MaxValue, "79228162514264337593543950335"),
                ["DateTime utc"] = (new DateTime(2026, 9, 2, 13, 45, 30, DateTimeKind.Utc), "\"2026-09-02T13:45:30Z\""),
                ["DateTime unspecified"] = (new DateTime(2026, 9, 2, 13, 45, 30, DateTimeKind.Unspecified), "\"2026-09-02T13:45:30\""),
                ["DateTime fraction"] = (new DateTime(2026, 9, 2, 13, 45, 30, 123, DateTimeKind.Utc), "\"2026-09-02T13:45:30.123Z\""),
                ["TimeSpan"] = (TimeSpan.FromMinutes(5), "\"00:05:00\""),
                ["TimeSpan negative"] = (TimeSpan.FromTicks(-864000000001), "\"-1.00:00:00.0000001\""),
                ["TimeSpan days"] = (new TimeSpan(3, 4, 5, 6, 7), "\"3.04:05:06.0070000\""),
                ["TimeSpan max"] = (TimeSpan.MaxValue, "\"10675199.02:48:05.4775807\""),
                ["Guid"] = (Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), "\"0f8fad5b-d9cb-469f-a165-70867728950e\""),
                ["Uri"] = (new Uri("https://example.org/a b?x=1"), "\"https://example.org/a b?x=1\""),
                ["Uri relative"] = (new Uri("/a/b", UriKind.Relative), "\"/a/b\""),
                ["DateOnly"] = (new DateOnly(2026, 9, 2), "\"2026-09-02\""),
                ["TimeOnly"] = (new TimeOnly(13, 45, 30), "\"13:45:30\""),
                ["TimeOnly fraction"] = (new TimeOnly(1, 2, 3, 456), "\"01:02:03.4560000\""),
                ["enum int"] = (LogEventLevel.Warning, "3"),
                ["enum byte"] = (ByteEnum.Value, "200"),
                ["enum ulong"] = (UlongEnum.Max, "18446744073709551615"),
                ["enum long"] = (LongEnum.Negative, "-5"),
                ["enum undefined"] = ((ByteEnum)77, "77"),

                // System.Text.Json rejects these outright, so before the rework the event was dropped.
                ["IntPtr"] = ((IntPtr)123, "123"),
                ["UIntPtr"] = ((UIntPtr)123, "123"),
                ["Type"] = (typeof(GraylogSink), "\"Scarlet.Serilog.Sinks.Graylog.GraylogSink\""),
                ["MethodInfo"] = (typeof(string).GetMethod("Trim", Type.EmptyTypes)!, "\"System.String Trim()\""),

                // Not a scalar Serilog would produce, but an enricher can put anything in a ScalarValue.
                ["unknown type"] = (new Unknown(), "\"unknown!\"")
            };

        public static TheoryData<string> Labels
        {
            get
            {
                var labels = new TheoryData<string>();

                foreach (string label in Cases.Keys)
                {
                    labels.Add(label);
                }

                return labels;
            }
        }

        [Theory]
        [MemberData(nameof(Labels))]
        public void Field_IsWrittenAsExpected(string label)
        {
            (object? value, string expected) = Cases[label];

            Assert.Equal(expected, SinkHarness.FieldJson(value));
        }
    }
}
