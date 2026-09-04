using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Fakes
{
    /// <summary>
    /// An <see cref="ITransport"/> that records what was sent instead of touching the network.
    /// Injected through <c>CustomTransportOptions.Factory</c> with
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
        /// port are not required because <c>TransportType.Custom</c> never uses them.
        /// </summary>
        public GraylogSinkOptions SinkOptions(Action<GraylogSinkOptions>? configure = null)
        {
            var options = new GraylogSinkOptions
            {
                TransportType = TransportType.Custom,
                Custom = new CustomTransportOptions { Factory = () => this }
            };

            configure?.Invoke(options);

            return options;
        }

        /// <summary>
        /// Records the payload as text.
        /// </summary>
        public async Task Send(ReadOnlyMemory<byte> payload)
        {
            string message = Encoding.UTF8.GetString(payload.ToArray());

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
            }
            finally
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
