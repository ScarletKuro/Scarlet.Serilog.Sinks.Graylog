using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Scarlet.Serilog.Sinks.Graylog.Core;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
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
    /// The exception policy differs between the two paths, because their contracts do.
    /// <see cref="EmitBatchAsync"/> is asynchronous, so it lets everything propagate to Serilog's
    /// batching infrastructure, which owns diagnostics, back-off and retry. <see cref="Emit"/> is a
    /// synchronous <c>void</c>, so it can only propagate what fails synchronously - which it does,
    /// rather than swallowing it - and reports a failed asynchronous send to <see cref="SelfLog"/>,
    /// having nowhere else to put it. It must not block to do better than that; see the remarks on
    /// <see cref="Emit"/>.
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
        /// <remarks>
        /// The send is started and not waited on. Blocking here - <c>.Result</c>, <c>.Wait()</c> or
        /// <c>.GetAwaiter().GetResult()</c> - deadlocks any caller with a single-threaded
        /// synchronization context, such as a WinForms UI thread, because the continuation needs the
        /// very thread that is blocked. See serilog-contrib/serilog-sinks-graylog#102.
        /// <para>
        /// Building the GELF payload happens synchronously, so a bad event, unusable serializer options
        /// or a transport that cannot be constructed throws out of this method and reaches Serilog,
        /// which reports it against this sink or, for an <c>AuditTo</c> logger, surfaces it to the
        /// caller. Only a failure of the transport's asynchronous send is left, and a synchronous void
        /// method has nowhere to report that, so it goes to <see cref="SelfLog"/>. Use
        /// <see cref="GraylogSinkOptions.Batching"/> if delivery failures need to be observable, since
        /// that path propagates them to Serilog's batching infrastructure, which also retries.
        /// </para>
        /// </remarks>
        public void Emit(LogEvent logEvent)
        {
            SendAsync(logEvent).ContinueWith(
                static task => SelfLog.WriteLine("Could not send a log event to Graylog: {0}", task.Exception?.GetBaseException()),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                // Never the ambient scheduler: on a UI thread that would queue the diagnostic behind
                // whatever the application is doing.
                TaskScheduler.Default);
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
