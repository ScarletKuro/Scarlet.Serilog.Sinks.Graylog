using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Fakes
{
    /// <summary>
    /// An <see cref="ITransport"/> that records what was sent instead of touching the network.
    /// Injected through <c>GraylogSinkOptionsBase.TransportFactory</c> with
    /// <c>TransportType.Custom</c>.
    /// </summary>
    internal sealed class RecordingTransport : ITransport
    {
        private readonly Func<string, Task>? _onSend;
        private readonly object _gate = new();

        private int _concurrentSends;

        public RecordingTransport(Func<string, Task>? onSend = null)
        {
            _onSend = onSend;
        }

        public List<string> Payloads { get; } = new();

        public int DisposeCount { get; private set; }

        /// <summary>
        /// The highest number of <see cref="Send"/> calls that were ever in flight at once.
        /// </summary>
        public int MaxObservedConcurrency { get; private set; }

        /// <summary>
        /// Sink options that route a logger to this transport instead of the network. The host and
        /// port are still required by the sink even though <c>TransportType.Custom</c> never uses them.
        /// </summary>
        public GraylogSinkOptions SinkOptions(Action<GraylogSinkOptions>? configure = null)
        {
            var options = new GraylogSinkOptions
            {
                HostnameOrAddress = "localhost",
                Port = 12201,
                TransportType = TransportType.Custom,
                TransportFactory = () => this
            };

            configure?.Invoke(options);

            return options;
        }

        public async Task Send(string message)
        {
            int concurrent = Interlocked.Increment(ref _concurrentSends);

            lock (_gate)
            {
                Payloads.Add(message);

                if (concurrent > MaxObservedConcurrency)
                {
                    MaxObservedConcurrency = concurrent;
                }
            }

            try
            {
                if (_onSend != null)
                {
                    await _onSend(message).ConfigureAwait(false);
                }
            } finally
            {
                Interlocked.Decrement(ref _concurrentSends);
            }
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
