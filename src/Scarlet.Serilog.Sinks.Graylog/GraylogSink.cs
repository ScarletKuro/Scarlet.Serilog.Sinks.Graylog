using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Scarlet.Serilog.Sinks.Graylog.Core;
using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using System;
using System.Collections.Concurrent;
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
    /// <see cref="DeliveryOptions.Batching"/>.
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
#if NET
        , IAsyncDisposable
#endif
    {
        private const int DefaultJsonSerializerMaxDepth = 64;

        private readonly Lazy<IGelfConverter> _converter;
        private readonly Lazy<ITransport> _transport;
        private readonly TimeSpan? _shutdownTimeout;

        private readonly JsonWriterOptions _writerOptions;

        /// <summary>
        /// Sends started by <see cref="Emit"/> that have not finished yet, so disposal can wait for
        /// them instead of tearing the transport down underneath them.
        /// </summary>
        private readonly ConcurrentDictionary<Task, byte> _inFlight = new ConcurrentDictionary<Task, byte>();

        /// <summary>
        /// Non-zero once disposal has started. An <see cref="int"/> driven through
        /// <see cref="Interlocked"/> rather than a <see cref="bool"/>: a plain field let two threads
        /// both read <c>false</c> and dispose the transport twice, and left <see cref="Emit"/> free to
        /// read a stale <c>false</c> indefinitely.
        /// </summary>
        private int _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="GraylogSink"/> class.
        /// </summary>
        /// <param name="options">The sink options. A copy of <see cref="GelfOptions.JsonSerializerOptions"/> is captured here; the transport and converter are built on first use.</param>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
        public GraylogSink(GraylogSinkOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            GraylogSinkOptionsValidator.Validate(options);

            var sinkComponentsBuilder = new SinkComponentsBuilder(options);

            _writerOptions = CreateWriterOptions(sinkComponentsBuilder.JsonSerializerOptions);

            _shutdownTimeout = options.Delivery.ShutdownTimeout;
            _transport = new Lazy<ITransport>(sinkComponentsBuilder.MakeTransport);
            _converter = new Lazy<IGelfConverter>(sinkComponentsBuilder.MakeGelfConverter);
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
        /// <see cref="DeliveryOptions.Batching"/> if delivery failures need to be observable, since
        /// that path propagates them to Serilog's batching infrastructure, which also retries.
        /// </para>
        /// </remarks>
        public void Emit(LogEvent logEvent)
        {
            // Reported rather than thrown: an event arriving after Log.CloseAndFlush() is a shutdown
            // ordering problem in the application, and taking it down over a log line would be worse
            // than losing the line. The check narrows the window rather than closing it - a send that
            // starts just before disposal and finds the transport gone underneath it faults, and its
            // continuation reports that to SelfLog the same way.
            if (Volatile.Read(ref _disposed) != 0)
            {
                SelfLog.WriteLine("A log event reached the Graylog sink after it was disposed and was dropped.");

                return;
            }

            Task send = SendAsync(logEvent);

            if (send.IsCompleted)
            {
                Report(send);

                return;
            }

            // What Dispose waits on is the reporting continuation, not the send. Waiting on the send
            // alone let the process exit between the send completing and the diagnostic reaching
            // SelfLog, so a failed delivery could disappear without a trace.
            Task reported = send.ContinueWith(
                static (task, _) => Report(task),
                null,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                // Never the ambient scheduler: on a UI thread that would queue the diagnostic behind
                // whatever the application is doing.
                TaskScheduler.Default);

            // Registered before the remover is attached, so the remover cannot run first and leave a
            // completed entry behind for the lifetime of the sink.
            _inFlight[reported] = 0;

            reported.ContinueWith(
                static (task, state) => ((GraylogSink)state!)._inFlight.TryRemove(task, out _),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        /// <summary>
        /// Reports a failed send to <see cref="SelfLog"/>, and observes the exception either way so it
        /// never surfaces as an unobserved task exception.
        /// </summary>
        private static void Report(Task send)
        {
            if (send.IsFaulted)
            {
                SelfLog.WriteLine("Could not send a log event to Graylog: {0}", send.Exception?.GetBaseException());
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

        /// <summary>
        /// Writes the GELF payload for an event and starts its send.
        /// </summary>
        /// <remarks>
        /// The payload buffer belongs to the sink and is handed back to the pool once the send has
        /// finished with it - not when this method returns, since <see cref="Emit"/> does not wait for
        /// the send. That is also why <see cref="ITransport.Send"/> documents the memory as valid only
        /// until its task completes: a transport that squirrels the payload away would find it
        /// overwritten by the next event. That holds for a send that failed as much as one that
        /// succeeded - a task in any terminal state means the transport is done with the memory, which
        /// is the contract every <c>Memory&lt;T&gt;</c>-taking asynchronous API works to - so the buffer
        /// goes back to the pool either way rather than being thrown away on the failure path.
        /// </remarks>
        private Task SendAsync(LogEvent logEvent)
        {
            var payload = new PooledByteBuffer();

            try
            {
                // A block rather than a using declaration on purpose: the writer has to be flushed and
                // done with the buffer before the send starts, not at the end of this method.
                using (var writer = new Utf8JsonWriter(payload, _writerOptions))
                {
                    _converter.Value.WriteGelfJson(logEvent, writer);
                    writer.Flush();
                }

                Task send = _transport.Value.Send(payload.WrittenMemory);

                if (send.IsCompleted)
                {
                    payload.Dispose();

                    return send;
                }

                return ReleaseWhenSent(send, payload);
            }
            catch
            {
                payload.Dispose();

                throw;
            }
        }

        private static async Task ReleaseWhenSent(Task send, PooledByteBuffer payload)
        {
            try
            {
                await send.ConfigureAwait(false);
            }
            finally
            {
                payload.Dispose();
            }
        }

        /// <summary>
        /// Derives the writer configuration from the serializer options the payload options carry.
        /// </summary>
        /// <remarks>
        /// Settings that have a corresponding <see cref="JsonWriterOptions"/> property are carried
        /// across so direct writing preserves the behavior of serializing with the configured
        /// <see cref="JsonSerializerOptions"/>. Validation is deliberately left on - it costs a few
        /// nanoseconds of bookkeeping per event and is what turns a custom <see cref="IGelfConverter"/>
        /// that writes unbalanced JSON into an exception rather than a payload Graylog silently
        /// rejects.
        /// </remarks>
        private static JsonWriterOptions CreateWriterOptions(JsonSerializerOptions serializerOptions)
        {
            return new JsonWriterOptions
            {
                Encoder = serializerOptions.Encoder,
                Indented = serializerOptions.WriteIndented,
                // Zero means 64 to JsonSerializerOptions but 1000 to JsonWriterOptions.
                MaxDepth = serializerOptions.MaxDepth == 0
                    ? DefaultJsonSerializerMaxDepth
                    : serializerOptions.MaxDepth,
#if NET9_0_OR_GREATER
                IndentCharacter = serializerOptions.IndentCharacter,
                IndentSize = serializerOptions.IndentSize,
                NewLine = serializerOptions.NewLine
#endif
            };
        }

        /// <inheritdoc />
        /// <remarks>
        /// Idempotent, and does not materialise the transport just to tear it down.
        /// </remarks>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Task[] pending = TakePending();

            if (pending.Length > 0 && _shutdownTimeout is { } timeout)
            {
                try
                {
                    // Safe to block: every await in the send path uses ConfigureAwait(false), so no
                    // continuation is waiting on the caller's synchronization context.
                    Task.WaitAll(pending, timeout);
                }
                catch (AggregateException)
                {
                    // Each send already reported itself to SelfLog through its own continuation.
                }
            }

            DisposeTransport();
        }

#if NET
        /// <summary>
        /// Waits for sends that are already in flight, then releases the transport.
        /// </summary>
        /// <remarks>
        /// Serilog prefers this over <see cref="Dispose()"/> when it is available, so
        /// <c>await Log.CloseAndFlushAsync()</c> drains the sink without blocking a thread.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Task[] pending = TakePending();

            if (pending.Length > 0 && _shutdownTimeout is { } timeout)
            {
                Task all = Task.WhenAll(pending);

                await Task.WhenAny(all, Task.Delay(timeout)).ConfigureAwait(false);

                // Observed so a faulted batch cannot resurface as an unobserved task exception; the
                // individual failures already reached SelfLog.
                _ = all.Exception;
            }

            DisposeTransport();
        }
#endif

        private Task[] TakePending()
        {
            var pending = new List<Task>(_inFlight.Count);

            foreach (Task send in _inFlight.Keys)
            {
                pending.Add(send);
            }

            return pending.ToArray();
        }

        private void DisposeTransport()
        {
            // Don't materialise the transport just to tear it down.
            if (_transport.IsValueCreated)
            {
                _transport.Value.Dispose();
            }
        }
    }
}
