using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Scarlet.Serilog.Sinks.Graylog.Core;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog
{
    /// <summary>
    /// Writes GELF messages to Graylog.
    /// </summary>
    /// <remarks>
    /// A single instance serves both registration paths, but a given logger only ever drives one of
    /// them: <see cref="LoggerConfigurationGrayLogExtensions"/> hands the sink either to
    /// <c>LoggerSinkConfiguration.Sink(ILogEventSink, ...)</c> or to
    /// <c>LoggerSinkConfiguration.Sink(IBatchedLogEventSink, BatchingOptions, ...)</c>, depending on
    /// <see cref="GraylogSinkOptions.Batching"/>.
    /// <para>
    /// The exception policy differs between the two paths, deliberately: <see cref="Emit"/> is
    /// fire-and-forget and reports failures to <see cref="SelfLog"/>, while
    /// <see cref="EmitBatchAsync"/> lets exceptions propagate because Serilog's batching
    /// infrastructure owns diagnostics, back-off and retry.
    /// </para>
    /// </remarks>
    public sealed class GraylogSink : ILogEventSink, IBatchedLogEventSink, IDisposable
    {
        private readonly Lazy<IGelfConverter> _converter;
        private readonly Lazy<ITransport> _transport;
        private readonly JsonSerializerOptions _options;
        private bool _disposed;

        public GraylogSink(GraylogSinkOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            ISinkComponentsBuilder sinkComponentsBuilder = new SinkComponentsBuilder(options);

            var jsonSerializerOptions = options.JsonSerializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.General);
            _options = new JsonSerializerOptions(jsonSerializerOptions);

            _transport = new Lazy<ITransport>(sinkComponentsBuilder.MakeTransport);
            _converter = new Lazy<IGelfConverter>(() => sinkComponentsBuilder.MakeGelfConverter());
        }

        /// <inheritdoc />
        public void Emit(LogEvent logEvent)
        {
            try
            {
                SendAsync(logEvent).ContinueWith(
                    task =>
                    {
                        SelfLog.WriteLine("Oops something going wrong {0}", task.Exception);
                    },
                    TaskContinuationOptions.OnlyOnFaulted);
            } catch (Exception exc)
            {
                // Materialising the lazy transport or converter can fail synchronously, for example
                // TransportType.Custom without a TransportFactory.
                SelfLog.WriteLine("Oops something going wrong {0}", exc);
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// Exceptions are intentionally not caught: Serilog's batching sink reports them and decides
        /// whether to retry or drop the batch.
        /// <para>
        /// Events are sent one at a time on purpose. <see cref="Core.Transport.Tcp.TcpTransportClient"/>
        /// writes null-terminated GELF frames into a single shared stream, so concurrent sends would
        /// interleave frames; the HTTP and UDP clients initialise their state lazily and are likewise
        /// not safe to drive concurrently.
        /// </para>
        /// </remarks>
        public async Task EmitBatchAsync(IReadOnlyCollection<LogEvent> batch)
        {
            foreach (LogEvent logEvent in batch)
            {
                await SendAsync(logEvent).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// Serilog only supplies a default implementation of this member on net6.0 and later, so the
        /// netstandard2.0 and .NET Framework builds require an explicit body.
        /// </remarks>
        public Task OnEmptyBatchAsync()
        {
            return Task.CompletedTask;
        }

        private Task SendAsync(LogEvent logEvent)
        {
            var json = _converter.Value.GetGelfJson(logEvent);
            var payload = json.ToJsonString(_options);

            return _transport.Value.Send(payload);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Don't materialise the transport just to tear it down.
            if (_transport.IsValueCreated)
            {
                _transport.Value.Dispose();
            }
        }
    }
}
