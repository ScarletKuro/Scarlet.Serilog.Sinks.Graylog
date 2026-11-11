using BenchmarkDotNet.Attributes;
using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders;
using Serilog.Events;
using Serilog.Parsing;
using System.Text.Json;

namespace Scarlet.Serilog.Sinks.Graylog.Benchmarks;

[MemoryDiagnoser]
public class SmallOptimizationsBenchmarks
{
    private GelfMessageBuilder _after = null!;
    private BeforeSmallOptimizationsBuilder _before = null!;
    private LogEvent _event = null!;

    [Params("Literal", "LiteralExcluded", "EnrichedLiteralExcluded", "Parameterized", "ParameterizedExcluded", "MissingExcluded")]
    public string Scenario { get; set; } = "Literal";

    [GlobalSetup]
    public void Setup()
    {
        bool excluded = Scenario.EndsWith("Excluded", StringComparison.Ordinal);
        var options = new GelfOptions { ExcludeMessageTemplateProperties = excluded };
        _after = new GelfMessageBuilder("benchmark-host", options);
        _before = new BeforeSmallOptimizationsBuilder("benchmark-host", options);
        bool parameterized = Scenario.StartsWith("Parameterized", StringComparison.Ordinal);
        bool missing = Scenario == "MissingExcluded";
        var properties = new List<LogEventProperty>();
        if (parameterized || Scenario == "EnrichedLiteralExcluded")
        {
            properties.Add(new LogEventProperty("Name", new ScalarValue("Alice")));
            properties.Add(new LogEventProperty("RequestId", new ScalarValue(12345)));
        }
        _event = new LogEvent(DateTimeOffset.UnixEpoch, LogEventLevel.Information, null,
            new MessageTemplateParser().Parse(parameterized || missing
                ? "Processed request for {Name}"
                : "Representative benchmark event"), properties);
        if (!Payload(_before.Build).AsSpan().SequenceEqual(Payload(_after.Build)))
            throw new InvalidOperationException("Payloads differ.");
    }

    private byte[] Payload(Action<LogEvent, Utf8JsonWriter> build)
    {
        var buffer = new ByteBufferWriter();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            build(_event, writer);
        }

        return buffer.WrittenSpan.ToArray();
    }

    [Benchmark(Baseline = true)]
    public int BuildBefore()
    {
        var buffer = new ByteBufferWriter();
        using var writer = new Utf8JsonWriter(buffer);
        _before.Build(_event, writer);
        writer.Flush();
        return buffer.WrittenCount;
    }

    [Benchmark]
    public int BuildAfter()
    {
        var buffer = new ByteBufferWriter();
        using var writer = new Utf8JsonWriter(buffer);
        _after.Build(_event, writer);
        writer.Flush();
        return buffer.WrittenCount;
    }
}
