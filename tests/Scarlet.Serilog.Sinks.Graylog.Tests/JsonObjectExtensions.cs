using System.Text.Json.Nodes;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests
{
    /// <summary>
    /// Reads fields out of a GELF payload for assertions.
    /// </summary>
    /// <remarks>
    /// <see cref="JsonObject"/>'s indexer returns null both for a field that is absent and for one
    /// whose value is JSON null, so every read here asserts the node exists rather than suppressing
    /// the nullability with <c>!</c>: a field that went missing then fails the test as a missing
    /// field instead of as a NullReferenceException from somewhere further along.
    /// </remarks>
    internal static class JsonObjectExtensions
    {
        internal static string Text(this JsonObject payload, string field)
        {
            return payload.Value<string>(field);
        }

        internal static T Value<T>(this JsonObject payload, string field)
        {
            JsonNode? node = payload[field];

            Assert.NotNull(node);

            return node.GetValue<T>();
        }

        internal static JsonObject Object(this JsonObject payload, string field)
        {
            JsonNode? node = payload[field];

            Assert.NotNull(node);

            return node.AsObject();
        }

        internal static string Json(this JsonObject payload, string field)
        {
            JsonNode? node = payload[field];

            Assert.NotNull(node);

            return node.ToJsonString();
        }
    }
}
