using BenchmarkDotNet.Attributes;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp;

namespace Scarlet.Serilog.Sinks.Graylog.Benchmarks;

[MemoryDiagnoser]
public class UdpDispatchBenchmarks
{
    private readonly ReadOnlyMemory<byte> _payload = new byte[256];
    private readonly YieldingTransportClient _client = new();
    private UdpTransport _transport = null!;

    [GlobalSetup]
    public void Setup()
    {
        _transport = new UdpTransport(
            _client,
            new UnusedChunkConverter(),
            new UdpTransportOptions { Compression = UdpCompression.None });
    }

    [Benchmark(Baseline = true, Description = "Async wrapper around one datagram")]
    public Task AsyncWrapper()
    {
        return SendWithWrapper();
    }

    [Benchmark(Description = "Return the client task directly")]
    public Task DirectTask()
    {
        return _transport.Send(_payload);
    }

    private async Task SendWithWrapper()
    {
        await _client.Send(_payload).ConfigureAwait(false);
    }

    private sealed class YieldingTransportClient : ITransportClient
    {
        public async Task Send(ReadOnlyMemory<byte> payload)
        {
            await Task.Yield();
        }

        public void Dispose()
        {
        }
    }

    private sealed class UnusedChunkConverter : IDataToChunkConverter
    {
        public IReadOnlyList<byte[]> ConvertToChunks(ReadOnlyMemory<byte> message)
        {
            throw new InvalidOperationException("The benchmark payload should fit in one datagram.");
        }
    }
}
