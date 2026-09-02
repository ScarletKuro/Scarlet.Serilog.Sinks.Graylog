using Serilog.Events;
using System.Text.Json.Serialization;

namespace Scarlet.Serilog.Sinks.Graylog.Aot.Tests
{
    /// <summary>
    /// A source-generated contract for the one enum the customization cases exercise, standing in for
    /// what a consumer would declare to customize serialization under Native AOT.
    /// </summary>
    [JsonSourceGenerationOptions(UseStringEnumConverter = true)]
    [JsonSerializable(typeof(LogEventLevel))]
    internal sealed partial class HarnessJsonContext : JsonSerializerContext
    {
    }
}
