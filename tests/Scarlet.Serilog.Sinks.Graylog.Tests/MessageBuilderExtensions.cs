using Scarlet.Serilog.Sinks.Graylog.Core;
using Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders;
using Serilog.Events;
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests
{
    /// <summary>
    /// Runs a message builder or converter and hands back what it wrote.
    /// </summary>
    /// <remarks>
    /// The builders write UTF-8 straight into a <see cref="Utf8JsonWriter"/>, so a fixture that wants
    /// to assert on fields has to read the payload back. Two shapes are offered because the two kinds
    /// of assertion need different things: <see cref="Build(IMessageBuilder, LogEvent)"/> parses the
    /// payload into a <see cref="JsonObject"/> for structural assertions, while
    /// <see cref="BuildPayload(IMessageBuilder, LogEvent)"/> hands back the bytes as text, for the
    /// fixtures that pin the wire format byte-for-byte. Going through <see cref="JsonNode"/> for those
    /// would re-serialize - and re-escape - the very output they are checking.
    /// </remarks>
    internal static class MessageBuilderExtensions
    {
        internal static JsonObject Build(this IMessageBuilder builder, LogEvent logEvent)
        {
            JsonNode? payload = JsonNode.Parse(builder.BuildPayload(logEvent));

            return Assert.IsType<JsonObject>(payload);
        }

        internal static string BuildPayload(this IMessageBuilder builder, LogEvent logEvent)
        {
            return Write(writer => builder.Build(logEvent, writer));
        }

        internal static JsonObject Convert(this IGelfConverter converter, LogEvent logEvent)
        {
            JsonNode? payload = JsonNode.Parse(Write(writer => converter.WriteGelfJson(logEvent, writer)));

            return Assert.IsType<JsonObject>(payload);
        }

        private static string Write(System.Action<Utf8JsonWriter> write)
        {
            var buffer = new ArrayBufferWriter<byte>();

            using (var writer = new Utf8JsonWriter(buffer))
            {
                write(writer);
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray());
        }
    }
}
