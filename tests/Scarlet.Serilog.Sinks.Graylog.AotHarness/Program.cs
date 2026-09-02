using Scarlet.Serilog.Sinks.Graylog;
using Scarlet.Serilog.Sinks.Graylog.Core;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Serilog.Core;
using Serilog.Parsing;

namespace AotHarness
{
    /// <summary>
    /// Verifies that the sink produces the expected GELF payloads when published with Native AOT.
    /// </summary>
    /// <remarks>
    /// Kept separate from the xunit test project on purpose: that one references AutoFixture,
    /// Serilog.Settings.Configuration and reflection-driven fixtures, so publishing it would drown the
    /// sink's own warnings in noise from unrelated dependencies.
    /// <para>
    /// Published twice in CI - once as-is, and once with
    /// <c>JsonSerializerIsReflectionEnabledByDefault=false</c>. The second run is the meaningful one: it
    /// removes reflection-based serialization entirely, so the sink has to carry every value on its
    /// reflection-free path. Without it the harness could pass on reflection metadata that happened to
    /// survive trimming.
    /// </para>
    /// </remarks>
    internal static class Program
    {
        private static int Main()
        {
            var selfLog = new StringBuilder();
            SelfLog.Enable(message => selfLog.Append(message));

            Console.WriteLine("Scarlet.Serilog.Sinks.Graylog Native AOT harness");
            Console.WriteLine($"  reflection-based serialization: {JsonSerializer.IsReflectionEnabledByDefault}");
            Console.WriteLine();

            int failures = RunScalarCases();

            failures += RunCustomizationCases();

            failures += RunEndToEndCase();

            if (selfLog.Length > 0)
            {
                // The sink reports emit failures to SelfLog instead of throwing, so anything here means
                // events were silently dropped - exactly the symptom this harness exists to catch.
                Console.WriteLine();
                Console.WriteLine("FAIL: the sink wrote to SelfLog, so at least one event was dropped:");
                Console.WriteLine(selfLog.ToString());

                failures++;
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "PASS" : $"FAIL ({failures} problem(s))");

            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// One case per scalar type Serilog can hand a sink, with the JSON captured from the
        /// reflection-based implementation.
        /// </summary>
        private static int RunScalarCases()
        {
            var cases = new List<(string Label, object? Value, string Expected)>
            {
                ("string", "abc", "\"abc\""),
                ("string escaped", "a\"b\\cé", "\"a\\u0022b\\\\c\\u00E9\""),
                ("bool", true, "true"),
                ("byte", (byte)7, "7"),
                ("sbyte", (sbyte)-7, "-7"),
                ("short", (short)-300, "-300"),
                ("ushort", (ushort)300, "300"),
                ("int", 42, "42"),
                ("uint", 42u, "42"),
                ("long", -9007199254740993L, "-9007199254740993"),
                ("ulong", ulong.MaxValue, "18446744073709551615"),
                ("char", 'x', "\"x\""),
                ("float", 1.5f, "1.5"),
                ("float precision", 0.1f, "0.1"),
                ("double", 3.14159265358979d, "3.14159265358979"),
                ("double rounding", 0.1d + 0.2d, "0.30000000000000004"),
                ("decimal", 1.500m, "1.500"),
                ("decimal max", decimal.MaxValue, "79228162514264337593543950335"),
                ("DateTime utc", new DateTime(2026, 9, 2, 13, 45, 30, DateTimeKind.Utc), "\"2026-09-02T13:45:30Z\""),
                ("DateTime unspecified", new DateTime(2026, 9, 2, 13, 45, 30, DateTimeKind.Unspecified), "\"2026-09-02T13:45:30\""),
                ("DateTime fraction", new DateTime(2026, 9, 2, 13, 45, 30, 123, DateTimeKind.Utc), "\"2026-09-02T13:45:30.123Z\""),
                ("TimeSpan", TimeSpan.FromMinutes(5), "\"00:05:00\""),
                ("TimeSpan negative", TimeSpan.FromTicks(-864000000001), "\"-1.00:00:00.0000001\""),
                ("TimeSpan days", new TimeSpan(3, 4, 5, 6, 7), "\"3.04:05:06.0070000\""),
                ("TimeSpan max", TimeSpan.MaxValue, "\"10675199.02:48:05.4775807\""),
                ("Guid", Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), "\"0f8fad5b-d9cb-469f-a165-70867728950e\""),
                ("Uri", new Uri("https://example.org/a b?x=1"), "\"https://example.org/a b?x=1\""),
                ("Uri relative", new Uri("/a/b", UriKind.Relative), "\"/a/b\""),
                ("DateOnly", new DateOnly(2026, 9, 2), "\"2026-09-02\""),
                ("TimeOnly", new TimeOnly(13, 45, 30), "\"13:45:30\""),
                ("TimeOnly fraction", new TimeOnly(1, 2, 3, 456), "\"01:02:03.4560000\""),
                ("enum int", LogEventLevel.Warning, "3"),
                ("enum byte", ByteEnum.Value, "200"),
                ("enum ulong", UlongEnum.Max, "18446744073709551615"),
                ("enum long", LongEnum.Negative, "-5"),
                ("enum undefined", (ByteEnum)77, "77"),

                // System.Text.Json rejects these outright, so before the rework the event was dropped.
                ("IntPtr", (IntPtr)123, "123"),
                ("UIntPtr", (UIntPtr)123, "123"),
                ("Type", typeof(GraylogSink), "\"Scarlet.Serilog.Sinks.Graylog.GraylogSink\""),
                ("MethodInfo", typeof(string).GetMethod("Trim", Type.EmptyTypes)!, "\"System.String Trim()\""),

                // Not a scalar Serilog would produce, but an enricher can put anything in a ScalarValue.
                ("unknown type", new Unknown(), "\"unknown!\"")
            };

            int failures = 0;

            foreach ((string label, object? value, string expected) in cases)
            {
                string actual;

                try
                {
                    actual = FieldJson(value);
                } catch (Exception exception)
                {
                    actual = $"THREW {exception.GetType().Name}";
                }

                bool passed = string.Equals(actual, expected, StringComparison.Ordinal);

                if (!passed)
                {
                    failures++;
                }

                Console.WriteLine($"  {(passed ? "ok  " : "FAIL")} {label,-22} {actual}");

                if (!passed)
                {
                    Console.WriteLine($"       expected            {expected}");
                }
            }

            return failures;
        }

        /// <summary>
        /// Drives a real logger through the sink so the transport, GELF envelope and final
        /// <c>ToJsonString</c> are all exercised, not just the scalar writer.
        /// </summary>
        private static int RunEndToEndCase()
        {
            var transport = new RecordingTransport();

            using (Logger logger = new LoggerConfiguration()
                .WriteTo.Graylog(new GraylogSinkOptions
                {
                    Facility = "aot-harness",
                    HostnameOverride = "harness-host",
                    TransportType = TransportType.Custom,
                    TransportFactory = () => transport
                })
                .CreateLogger())
            {
                logger.Information("Ordered {Count} of {Sku} at {When}", 3, "ABC-123", new DateTime(2026, 9, 2, 13, 45, 30, DateTimeKind.Utc));
            }

            Console.WriteLine();

            if (transport.Payloads.Count != 1)
            {
                Console.WriteLine($"  FAIL end-to-end          expected 1 payload, got {transport.Payloads.Count}");

                return 1;
            }

            string payload = transport.Payloads[0];

            var expected = new[]
            {
                "\"host\":\"harness-host\"",
                "\"_facility\":\"aot-harness\"",
                "\"_Count\":3",
                "\"_Sku\":\"ABC-123\"",
                "\"_When\":\"2026-09-02T13:45:30Z\""
            };

            int failures = 0;

            foreach (string fragment in expected)
            {
                if (payload.IndexOf(fragment, StringComparison.Ordinal) < 0)
                {
                    Console.WriteLine($"  FAIL end-to-end          payload is missing {fragment}");

                    failures++;
                }
            }

            if (failures == 0)
            {
                Console.WriteLine("  ok   end-to-end          " + payload);
            } else
            {
                Console.WriteLine("       payload was          " + payload);
            }

            return failures;
        }

        /// <summary>
        /// Establishes what the documented customization hooks actually do under Native AOT, where
        /// there is no reflection-based contract resolver to fall back on.
        /// </summary>
        private static int RunCustomizationCases()
        {
            Console.WriteLine();

            var cases = new List<(string Label, JsonSerializerOptions Options, string Expected)>
            {
                // A source-generated context can resolve a contract without reflection, so it applies.
                ("source-gen context", new JsonSerializerOptions { TypeInfoResolver = HarnessJsonContext.Default }, "\"Warning\""),

                // A converter alone cannot: applying it needs a contract, and building one without a
                // resolver requires reflection. Documented as needing a resolver too.
                ("converter, no resolver", OptionsWithStringEnums(), "3")
            };

            int failures = 0;

            foreach ((string label, JsonSerializerOptions options, string expected) in cases)
            {
                string actual;

                try
                {
                    actual = FieldJson(LogEventLevel.Warning, options);
                } catch (Exception exception)
                {
                    actual = $"THREW {exception.GetType().Name}";
                }

                bool passed = string.Equals(actual, expected, StringComparison.Ordinal);

                if (!passed)
                {
                    failures++;
                }

                Console.WriteLine($"  {(passed ? "ok  " : "FAIL")} {label,-22} {actual}");

                if (!passed)
                {
                    Console.WriteLine($"       expected            {expected}");
                }
            }

            return failures;
        }

        private static JsonSerializerOptions OptionsWithStringEnums()
        {
            var options = new JsonSerializerOptions();

            // The generic form: the non-generic JsonStringEnumConverter is itself RequiresDynamicCode.
            options.Converters.Add(new JsonStringEnumConverter<LogEventLevel>());

            return options;
        }

        /// <summary>
        /// The JSON the sink writes for a single additional field, taken from a real emitted payload.
        /// </summary>
        private static string FieldJson(object? value) => FieldJson(value, new JsonSerializerOptions());

        private static string FieldJson(object? value, JsonSerializerOptions serializerOptions)
        {
            var transport = new RecordingTransport();

            var options = new GraylogSinkOptions
            {
                Facility = "aot-harness",
                TransportType = TransportType.Custom,
                TransportFactory = () => transport,
                JsonSerializerOptions = serializerOptions
            };

            using (var sink = new GraylogSink(options))
            {
                var logEvent = new LogEvent(DateTimeOffset.UnixEpoch, LogEventLevel.Information, null,
                    new MessageTemplate("", Array.Empty<MessageTemplateToken>()),
                    new[] { new LogEventProperty("Val", new ScalarValue(value)) });

                // EmitBatchAsync rather than Emit: it propagates failures instead of routing them to
                // SelfLog, so a broken case shows up as an exception here rather than a missing payload.
                sink.EmitBatchAsync(new[] { logEvent }).GetAwaiter().GetResult();
            }

            if (transport.Payloads.Count != 1)
            {
                throw new InvalidOperationException($"expected 1 payload, got {transport.Payloads.Count}");
            }

            using JsonDocument document = JsonDocument.Parse(transport.Payloads[0]);

            if (!document.RootElement.TryGetProperty("_Val", out JsonElement field))
            {
                throw new InvalidOperationException("payload has no _Val field");
            }

            return field.GetRawText();
        }

        private sealed class RecordingTransport : ITransport
        {
            public List<string> Payloads { get; } = new List<string>();

            public Task Send(string message)
            {
                Payloads.Add(message);

                return Task.CompletedTask;
            }

            public void Dispose()
            {
            }
        }

        private sealed class Unknown
        {
            public override string ToString() => "unknown!";
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

namespace AotHarness
{
    /// <summary>
    /// A source-generated contract for the one enum the customization cases exercise, standing in for
    /// what a consumer would declare to customize serialization under Native AOT.
    /// </summary>
    [System.Text.Json.Serialization.JsonSourceGenerationOptions(UseStringEnumConverter = true)]
    [System.Text.Json.Serialization.JsonSerializable(typeof(Serilog.Events.LogEventLevel))]
    internal sealed partial class HarnessJsonContext : System.Text.Json.Serialization.JsonSerializerContext
    {
    }
}
