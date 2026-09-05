using BenchmarkDotNet.Attributes;
using System.Collections.Concurrent;

namespace Scarlet.Serilog.Sinks.Graylog.Benchmarks;

[MemoryDiagnoser]
public class PendingSendBenchmarks
{
    private readonly DictionaryTracker _dictionary = new();
    private readonly CounterTracker _counter = new();

    [Benchmark(Baseline = true, Description = "Dictionary and removal continuation")]
    public Task DictionaryAndTwoContinuations()
    {
        var completion = new TaskCompletionSource<object?>();
        Task tracked = _dictionary.Track(completion.Task);

        completion.SetResult(null);

        return tracked;
    }

    [Benchmark(Description = "Counter and one continuation")]
    public Task CounterAndOneContinuation()
    {
        var completion = new TaskCompletionSource<object?>();
        Task tracked = _counter.Track(completion.Task);

        completion.SetResult(null);

        return tracked;
    }

    private sealed class DictionaryTracker
    {
        private readonly ConcurrentDictionary<Task, byte> _inFlight = new();

        public Task Track(Task send)
        {
            Task reported = send.ContinueWith(
                static (_, _) => { },
                null,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            _inFlight[reported] = 0;

            return reported.ContinueWith(
                static (task, state) => ((DictionaryTracker)state!)._inFlight.TryRemove(task, out _),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private sealed class CounterTracker
    {
        private int _inFlightCount;

        public Task Track(Task send)
        {
            Interlocked.Increment(ref _inFlightCount);

            return send.ContinueWith(
                static (_, state) => Interlocked.Decrement(ref ((CounterTracker)state!)._inFlightCount),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
