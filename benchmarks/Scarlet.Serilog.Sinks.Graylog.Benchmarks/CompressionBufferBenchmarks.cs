using BenchmarkDotNet.Attributes;
using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;

namespace Scarlet.Serilog.Sinks.Graylog.Benchmarks;

[MemoryDiagnoser]
public class CompressionBufferBenchmarks
{
    private const int MaximumDatagramSize = 8192;

    private byte[] _payload = null!;

    [Params(4096, 65536)]
    public int PayloadSize { get; set; }

    [Params(true, false)]
    public bool Compressible { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _payload = new byte[PayloadSize];

        if (Compressible)
        {
            Array.Fill(_payload, (byte)'x');
        }
        else
        {
            new Random(42).NextBytes(_payload);
        }
    }

    [Benchmark(Baseline = true, Description = "Input-sized compression buffer")]
    public int InputSizedBuffer()
    {
        var buffer = new ByteBufferWriter(_payload.Length);

        GzipCompressor.Compress(_payload, buffer);

        return buffer.WrittenCount;
    }

    [Benchmark(Description = "Datagram-sized initial compression buffer")]
    public int DatagramSizedInitialBuffer()
    {
        var buffer = new ByteBufferWriter(Math.Min(_payload.Length, MaximumDatagramSize));

        GzipCompressor.Compress(_payload, buffer);

        return buffer.WrittenCount;
    }
}
