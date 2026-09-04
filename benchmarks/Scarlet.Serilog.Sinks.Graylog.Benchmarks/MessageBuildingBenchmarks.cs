using BenchmarkDotNet.Attributes;
using Serilog.Events;
using Serilog.Parsing;
using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Scarlet.Serilog.Sinks.Graylog.Benchmarks;

[MemoryDiagnoser]
public class MessageBuildingBenchmarks
{
    private readonly JsonSerializerOptions _options = new();
    private GelfMessageBuilder _builder = null!;
    private LogEvent _event = null!;

    [Params(0, 10, 50)]
    public int PropertyCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _builder = new GelfMessageBuilder("benchmark-host", new GelfOptions { Facility = "benchmark" });

        var properties = new List<LogEventProperty>(PropertyCount);

        for (int i = 0; i < PropertyCount; i++)
        {
            object value = (i % 3) switch
            {
                0 => i,
                1 => $"value-{i}",
                _ => Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e")
            };

            properties.Add(new LogEventProperty($"Property{i}", new ScalarValue(value)));
        }

        _event = new LogEvent(
            DateTimeOffset.UnixEpoch,
            LogEventLevel.Information,
            null,
            new MessageTemplate("Representative benchmark event", [new TextToken("Representative benchmark event")]),
            properties);
    }

    [Benchmark(Baseline = true, Description = "Legacy JsonNode tree and UTF-16 payload")]
    public int LegacyJsonTree()
    {
        var json = new JsonObject
        {
            ["version"] = "1.1",
            ["host"] = "benchmark-host",
            ["short_message"] = _event.RenderMessage(),
            ["timestamp"] = _event.Timestamp.ToUnixTimeMilliseconds() / 1000d,
            ["level"] = 6,
            ["_stringLevel"] = "Information",
            ["_facility"] = "benchmark"
        };

        foreach (KeyValuePair<string, LogEventPropertyValue> property in _event.Properties)
        {
            var scalar = (ScalarValue)property.Value;
            JsonNode? value = scalar.Value == null
                ? null
                : JsonSerializer.SerializeToNode(scalar.Value, scalar.Value.GetType(), _options);

            json["_" + property.Key] = value;
        }

        return json.ToJsonString(_options).Length;
    }

    [Benchmark(Description = "Streaming Utf8JsonWriter with the production buffer")]
    public int StreamingUtf8()
    {
        var buffer = new ByteBufferWriter();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            _builder.Build(_event, writer);
            writer.Flush();
        }

        return buffer.WrittenCount;
    }
}
