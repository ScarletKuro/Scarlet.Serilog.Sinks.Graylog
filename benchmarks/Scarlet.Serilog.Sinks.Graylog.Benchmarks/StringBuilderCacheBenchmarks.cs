using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders;
using Serilog.Events;
using Serilog.Parsing;

namespace Scarlet.Serilog.Sinks.Graylog.Benchmarks;

/// <summary>
/// Isolates the two StringBuilderCache call sites - <see cref="ExceptionMessageBuilder"/>'s message and
/// stack-trace flattening, and <see cref="GelfMessageBuilder"/>'s rendering of sequence and dictionary
/// property values - from the rest of message building.
/// </summary>
[MemoryDiagnoser]
public class StringBuilderCacheBenchmarks
{
    private ExceptionMessageBuilder _exceptionBuilder = null!;
    private GelfMessageBuilder _arrayBuilder = null!;
    private LogEvent _exceptionEvent = null!;
    private LogEvent _arrayEvent = null!;

    [GlobalSetup]
    public void Setup()
    {
        _exceptionBuilder = new ExceptionMessageBuilder("benchmark-host", new GelfOptions { StackTraceDepth = 10 });
        _exceptionEvent = new LogEvent(
            DateTimeOffset.UnixEpoch,
            LogEventLevel.Error,
            NestedException(4),
            new MessageTemplate("Representative exception event", [new TextToken("Representative exception event")]),
            []);

        _arrayBuilder = new GelfMessageBuilder("benchmark-host", new GelfOptions { ParseArrayValues = true });

        var elements = new List<LogEventPropertyValue>();
        for (int i = 0; i < 20; i++)
        {
            elements.Add(new ScalarValue($"element-{i}"));
        }

        var properties = new List<LogEventProperty>
        {
            new("Sequence", new SequenceValue(elements))
        };

        _arrayEvent = new LogEvent(
            DateTimeOffset.UnixEpoch,
            LogEventLevel.Information,
            null,
            new MessageTemplate("Representative array event", [new TextToken("Representative array event")]),
            properties);
    }

    [Benchmark(Baseline = true, Description = "Exception message: flatten message and stack trace")]
    public int ExceptionMessage()
    {
        var buffer = new ByteBufferWriter();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            _exceptionBuilder.Build(_exceptionEvent, writer);
            writer.Flush();
        }

        return buffer.WrittenCount;
    }

    [Benchmark(Description = "Sequence property: render each element back to text")]
    public int SequenceProperty()
    {
        var buffer = new ByteBufferWriter();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            _arrayBuilder.Build(_arrayEvent, writer);
            writer.Flush();
        }

        return buffer.WrittenCount;
    }

    private static Exception NestedException(int depth)
    {
        Exception? inner = null;

        for (int level = 1; level <= depth; level++)
        {
            try
            {
                if (inner == null)
                {
                    throw new InvalidOperationException($"Level {level} exception");
                }

                throw new InvalidOperationException($"Level {level} exception", inner);
            }
            catch (Exception thrown)
            {
                inner = thrown;
            }
        }

        return inner!;
    }
}
