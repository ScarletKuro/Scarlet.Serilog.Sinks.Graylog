using Scarlet.Serilog.Sinks.Graylog.Tests.Fakes;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;
using Scarlet.Serilog.Sinks.Graylog.Core;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests
{
    /// <summary>
    /// Shares a collection with the other classes that drive <c>SelfLog</c>: it is a single global
    /// handler, so two classes enabling it at once clobber each other's assertions.
    /// </summary>
    [Collection(SelfLogCollection.Name)]
    public class GraylogSinkFixture
    {
        [Fact]
        public void Emit_KeepsEachPayloadAliveUntilItsSendCompletes()
        {
            var transport = new DeferredReadingTransport();
            var options = new GraylogSinkOptions
            {
                TransportType = TransportType.Custom,
                Custom = new CustomTransportOptions { Factory = () => transport }
            };
            var sink = new GraylogSink(options);

            sink.Emit(LogEventSource.GetScalarEvent("Value", "first"));
            sink.Emit(LogEventSource.GetScalarEvent("Value", "second"));

            Assert.Equal("first", ReadValue(transport.Payloads[0]));
            Assert.Equal("second", ReadValue(transport.Payloads[1]));

            transport.CompleteAll();
            sink.Dispose();
        }

        [Fact]
        public void Constructor_SnapshotsSerializerOptionsBeforeTheLazyBuildersAreCreated()
        {
            var serializerOptions = new JsonSerializerOptions();
            using var transport = new RecordingTransport();
            GraylogSinkOptions options = transport.SinkOptions(
                sinkOptions => sinkOptions.Message.JsonSerializerOptions = serializerOptions);
            using var sink = new GraylogSink(options);

            // This remains legal because the sink copied the caller's instance, but it must not alter
            // scalar serialization after construction just because the builder is created lazily.
            serializerOptions.Converters.Add(new UpperCaseStringConverter());

            sink.Emit(LogEventSource.GetScalarEvent("Value", "first"));

            using JsonDocument payload = JsonDocument.Parse(transport.Payloads[0]);
            Assert.Equal("first", payload.RootElement.GetProperty("_Value").GetString());

            serializerOptions.WriteIndented = true;
            Assert.True(serializerOptions.WriteIndented);
        }

        [Fact]
        public void Emit_HonoursJsonSerializerOptionsMaxDepth()
        {
            using var transport = new RecordingTransport();
            GraylogSinkOptions options = transport.SinkOptions(sinkOptions =>
            {
                sinkOptions.Message.JsonSerializerOptions = new JsonSerializerOptions { MaxDepth = 1 };
                sinkOptions.Message.Converter = new NestedObjectGelfConverter();
            });
            using var sink = new GraylogSink(options);

            Assert.Throws<InvalidOperationException>(
                () => sink.Emit(LogEventSource.GetSimpleLogEvent(DateTimeOffset.UnixEpoch)));
            Assert.Empty(transport.Payloads);
        }

        [Fact]
        public void Emit_HonoursJsonSerializerOptionsDefaultMaxDepth()
        {
            using var transport = new RecordingTransport();
            GraylogSinkOptions options = transport.SinkOptions(
                sinkOptions => sinkOptions.Message.Converter = new NestedObjectGelfConverter(64));
            using var sink = new GraylogSink(options);

            Assert.Throws<InvalidOperationException>(
                () => sink.Emit(LogEventSource.GetSimpleLogEvent(DateTimeOffset.UnixEpoch)));
            Assert.Empty(transport.Payloads);
        }

#if NET9_0_OR_GREATER
        [Fact]
        public void Emit_HonoursJsonSerializerOptionsIndentationSettings()
        {
            var serializerOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                IndentCharacter = '\t',
                IndentSize = 1,
                NewLine = "\r\n"
            };
            using var transport = new RecordingTransport();
            using var sink = new GraylogSink(transport.SinkOptions(
                options => options.Message.JsonSerializerOptions = serializerOptions));

            sink.Emit(LogEventSource.GetSimpleLogEvent(DateTimeOffset.UnixEpoch));

            string payload = Assert.Single(transport.Payloads);
            Assert.Contains("\r\n\t\"version\"", payload);
            Assert.DoesNotContain("\n  \"version\"", payload);
        }
#endif

        /// <summary>
        /// Emit must never wait on the send. Blocking deadlocks a caller whose synchronization context
        /// is single-threaded, because the continuation needs the thread that is blocked.
        /// </summary>
        /// <remarks>
        /// Regression test for serilog-contrib/serilog-sinks-graylog#102, a WinForms application that
        /// froze after the first event. The context below accepts posted continuations and never runs
        /// them, which is what a blocked UI thread looks like, so anything that waits for the send to
        /// finish hangs here and trips the timeout.
        /// </remarks>
        [Fact]
        public void Emit_OnSingleThreadedSynchronizationContext_DoesNotBlock()
        {
            // Completes only when the context pumps, which it never does.
            var neverCompletes = new TaskCompletionSource<bool>();
            RecordingTransport transport = new(_ => neverCompletes.Task);
            GraylogSink target = new(transport.SinkOptions());
            var returned = new ManualResetEventSlim();

            var uiThread = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new NeverPumpedSynchronizationContext());

                target.Emit(LogEventSource.GetSimpleLogEvent(DateTimeOffset.UnixEpoch));

                returned.Set();
            });

            uiThread.Start();

            Assert.True(returned.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken),
                "Emit did not return - it is waiting on the send.");

            neverCompletes.SetResult(true);
            uiThread.Join();
        }

        /// <summary>
        /// Emit deliberately does not wait for the send - blocking there deadlocks a single-threaded
        /// synchronization context - but disposal has to, or a process that exits shortly after
        /// logging loses whatever was still on the wire, silently.
        /// </summary>
        /// <summary>
        /// Disposal must not return until a failed send has actually reached SelfLog. Waiting on the
        /// send alone left a window where the process exited between the send completing and its
        /// reporting continuation running, and the failure vanished without a trace.
        /// </summary>
        [Fact]
        public void Dispose_WhenASendFails_ReportsToSelfLogBeforeReturning()
        {
            const string failure = "graylog refused the batch";
            int reported = 0;
            RecordingTransport transport = new(async _ =>
            {
                await Task.Delay(150);
                throw new InvalidOperationException(failure);
            });

            var sink = new GraylogSink(transport.SinkOptions());

            // SelfLog is global and other classes run in parallel, so react only to this failure.
            SelfLog.Enable(message =>
            {
                if (message.Contains(failure))
                {
                    Interlocked.Increment(ref reported);
                }
            });

            try
            {
                sink.Emit(LogEventSource.GetSimpleLogEvent(DateTimeOffset.UnixEpoch));

                sink.Dispose();

                Assert.Equal(1, Volatile.Read(ref reported));
            }
            finally
            {
                SelfLog.Disable();
            }
        }

        [Fact]
        public void Dispose_WaitsForSendsAlreadyInFlight()
        {
            int completed = 0;
            RecordingTransport transport = new(async _ =>
            {
                await Task.Delay(200);
                Interlocked.Increment(ref completed);
            });

            var sink = new GraylogSink(transport.SinkOptions());
            sink.Emit(LogEventSource.GetSimpleLogEvent(DateTimeOffset.UnixEpoch));

            sink.Dispose();

            Assert.Equal(1, Volatile.Read(ref completed));
        }

        [Fact]
        public async Task DisposeAsync_WaitsForSendsAlreadyInFlight()
        {
            int completed = 0;
            RecordingTransport transport = new(async _ =>
            {
                await Task.Delay(200);
                Interlocked.Increment(ref completed);
            });

            var sink = new GraylogSink(transport.SinkOptions());
            sink.Emit(LogEventSource.GetSimpleLogEvent(DateTimeOffset.UnixEpoch));

            await sink.DisposeAsync();

            Assert.Equal(1, Volatile.Read(ref completed));
        }

        /// <summary>
        /// The wait is bounded, so an unreachable Graylog cannot hold up process exit.
        /// </summary>
        [Fact]
        public void Dispose_WhenASendNeverCompletes_GivesUpAfterTheTimeout()
        {
            var neverCompletes = new TaskCompletionSource<bool>();
            RecordingTransport transport = new(_ => neverCompletes.Task);

            var sink = new GraylogSink(transport.SinkOptions(
                o => o.Delivery.ShutdownTimeout = TimeSpan.FromMilliseconds(250)));
            sink.Emit(LogEventSource.GetSimpleLogEvent(DateTimeOffset.UnixEpoch));

            var elapsed = Stopwatch.StartNew();
            sink.Dispose();
            elapsed.Stop();

            Assert.InRange(elapsed.Elapsed, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10));

            neverCompletes.SetResult(true);
        }

        /// <summary>
        /// A null timeout opts out of waiting entirely, for anyone who would rather lose the tail
        /// than add anything at all to shutdown.
        /// </summary>
        [Fact]
        public void Dispose_WhenShutdownTimeoutIsNull_DoesNotWait()
        {
            int completed = 0;
            RecordingTransport transport = new(async _ =>
            {
                await Task.Delay(2000);
                Interlocked.Increment(ref completed);
            });

            var sink = new GraylogSink(transport.SinkOptions(o => o.Delivery.ShutdownTimeout = null));
            sink.Emit(LogEventSource.GetSimpleLogEvent(DateTimeOffset.UnixEpoch));

            var elapsed = Stopwatch.StartNew();
            sink.Dispose();
            elapsed.Stop();

            Assert.Equal(0, Volatile.Read(ref completed));
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1), $"Dispose took {elapsed.Elapsed}");
        }

        /// <summary>
        /// What fails synchronously must reach Serilog instead of being swallowed by the sink.
        /// </summary>
        /// <remarks>
        /// Serilog wraps a <c>WriteTo</c> sink in <c>SafeAggregateSink</c>, which reports the failure to
        /// <c>SelfLog</c> along with the sink that raised it, and an <c>AuditTo</c> sink in
        /// <c>AggregateSink</c>, which surfaces it to the caller. Neither can happen if the sink
        /// catches the exception itself.
        /// </remarks>
        [Fact]
        public void Emit_WhenTransportCannotBeCreated_Throws()
        {
            GraylogSink target = new(new GraylogSinkOptions
            {
                TransportType = TransportType.Custom,
                // A valid configuration whose transport still cannot be built: the factory is only
                // invoked on the first emit, so this is past the constructor's validation.
                Custom = new CustomTransportOptions { Factory = () => throw new InvalidOperationException("no transport") }
            });

            Assert.ThrowsAny<Exception>(
                () => target.Emit(LogEventSource.GetSimpleLogEvent(DateTimeOffset.UnixEpoch)));
        }

        /// <summary>
        /// Validation used to run only inside <c>WriteTo.Graylog(...)</c>, so constructing the sink
        /// directly skipped every check.
        /// </summary>
        [Fact]
        public void Constructor_WhenTheOptionsAreInvalid_Throws()
        {
            // Custom transport, no factory.
            var options = new GraylogSinkOptions { TransportType = TransportType.Custom };

            Assert.Throws<ArgumentException>(() => new GraylogSink(options));
        }

        /// <summary>
        /// A failed asynchronous send cannot be surfaced from a synchronous void method, so it has to
        /// be reported to SelfLog - but it must actually be reported, and it must not be an
        /// unobserved task exception.
        /// </summary>
        [Fact]
        public async Task Emit_WhenSendFails_ReportsToSelfLog()
        {
            const string failure = "graylog is down";

            var reported = new TaskCompletionSource<string>();
            RecordingTransport transport = new(_ => Task.FromException(new InvalidOperationException(failure)));
            GraylogSink target = new(transport.SinkOptions());

            // SelfLog is global and other test classes run in parallel, so only react to this failure.
            SelfLog.Enable(message =>
            {
                if (message.Contains(failure))
                {
                    reported.TrySetResult(message);
                }
            });

            try
            {
                target.Emit(LogEventSource.GetSimpleLogEvent(DateTimeOffset.UnixEpoch));

                Task completed = await Task.WhenAny(
                    reported.Task,
                    Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

                Assert.Same(reported.Task, completed);
                Assert.Contains("Could not send a log event to Graylog", await reported.Task);
            }
            finally
            {
                SelfLog.Disable();
            }
        }

        /// <summary>
        /// An incomplete custom transport is rejected before the sink is registered.
        /// </summary>
        [Fact]
        public void WriteTo_WhenCustomTransportHasNoFactory_Throws()
        {
            Assert.Throws<ArgumentException>(() => new LoggerConfiguration()
                .WriteTo.Graylog(new GraylogSinkOptions
                {
                    TransportType = TransportType.Custom
                }));
        }

        [Fact]
        public void Constructor_WithoutOptions_Throws()
        {
            Assert.Equal("options", Assert.Throws<ArgumentNullException>(() => new GraylogSink(null!)).ParamName);
        }

        /// <summary>
        /// Serilog calls whichever disposal the sink offers, and a container may well call both.
        /// </summary>
        [Fact]
        public async Task DisposeAsync_AfterDispose_DoesNothingMore()
        {
            using var transport = new RecordingTransport();
            var sink = new GraylogSink(transport.SinkOptions());
            sink.Emit(LogEventSource.GetSimpleLogEvent(DateTimeOffset.UnixEpoch));

            sink.Dispose();
            await sink.DisposeAsync();

            Assert.Equal(1, transport.DisposeCount);
        }

        /// <summary>
        /// Disposal has to finish even when reporting the failure fails: a SelfLog sink that throws
        /// would otherwise take the exception out of Dispose and into the shutdown path.
        /// </summary>
        [Fact]
        public void Dispose_WhenReportingAFailedSendThrows_StillCompletes()
        {
            const string failure = "graylog refused the batch while self log was broken";
            RecordingTransport transport = new(async _ =>
            {
                await Task.Delay(150);
                throw new InvalidOperationException(failure);
            });

            var sink = new GraylogSink(transport.SinkOptions());

            // SelfLog is global and other classes run in parallel, so break it only for this failure.
            SelfLog.Enable(message =>
            {
                if (message.Contains(failure))
                {
                    throw new InvalidOperationException("the self log sink is broken too");
                }
            });

            try
            {
                sink.Emit(LogEventSource.GetSimpleLogEvent(DateTimeOffset.UnixEpoch));

                sink.Dispose();
            }
            finally
            {
                SelfLog.Disable();
            }

            Assert.Equal(1, transport.DisposeCount);
        }

        /// <summary>
        /// Two threads disposing at once must not both get past the guard: the second one tore the
        /// transport down a second time, underneath whatever the first was still waiting for.
        /// </summary>
        [Fact]
        public void Dispose_CalledConcurrently_ReleasesTheTransportOnce()
        {
            using var transport = new RecordingTransport();
            var sink = new GraylogSink(transport.SinkOptions());

            sink.Emit(LogEventSource.GetSimpleLogEvent(DateTimeOffset.UnixEpoch));

            using var start = new ManualResetEventSlim();
            var threads = new Thread[8];

            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread(() =>
                {
                    start.Wait();
                    sink.Dispose();
                });
                threads[i].Start();
            }

            start.Set();

            foreach (Thread thread in threads)
            {
                thread.Join();
            }

            Assert.Equal(1, transport.DisposeCount);
        }

        [Fact]
        public async Task DisposeAsync_AfterDispose_DoesNotReleaseTheTransportAgain()
        {
            using var transport = new RecordingTransport();
            var sink = new GraylogSink(transport.SinkOptions());

            sink.Dispose();
            await sink.DisposeAsync();

            // Never materialised, so nothing to release - what matters is that neither call ran twice.
            Assert.Equal(0, transport.DisposeCount);
        }

        /// <summary>
        /// An event arriving after the sink was disposed is a shutdown ordering problem in the
        /// application. Reporting it beats both alternatives: sending it through a transport that has
        /// already been torn down, and taking the process down over a log line.
        /// </summary>
        [Fact]
        public void Emit_AfterDispose_ReportsTheDroppedEventInsteadOfSending()
        {
            using var transport = new RecordingTransport();
            var sink = new GraylogSink(transport.SinkOptions());

            sink.Dispose();

            var reported = new List<string>();
            SelfLog.Enable(reported.Add);

            try
            {
                sink.Emit(LogEventSource.GetSimpleLogEvent(DateTimeOffset.UnixEpoch));
            }
            finally
            {
                SelfLog.Disable();
            }

            Assert.Empty(transport.Payloads);
            Assert.Contains(reported, message => message.Contains("after it was disposed"));
        }

        /// <summary>
        /// Stands in for a busy UI thread: continuations are accepted and never run.
        /// </summary>
        private sealed class NeverPumpedSynchronizationContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state)
            {
            }

            public override void Send(SendOrPostCallback d, object? state)
            {
            }
        }

        private static string ReadValue(ReadOnlyMemory<byte> payload)
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            string? value = document.RootElement.GetProperty("_Value").GetString();

            Assert.NotNull(value);

            return value;
        }

        private sealed class DeferredReadingTransport : ITransport
        {
            private readonly List<TaskCompletionSource<bool>> _pending = new();

            public List<ReadOnlyMemory<byte>> Payloads { get; } = new();

            public Task Send(ReadOnlyMemory<byte> message)
            {
                Payloads.Add(message);

                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending.Add(completion);

                return completion.Task;
            }

            public void CompleteAll()
            {
                foreach (TaskCompletionSource<bool> completion in _pending)
                {
                    completion.SetResult(true);
                }
            }

            public void Dispose()
            {
            }
        }

        private sealed class UpperCaseStringConverter : JsonConverter<string>
        {
            public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                return reader.GetString();
            }

            public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.ToUpperInvariant());
            }
        }

        private sealed class NestedObjectGelfConverter : IGelfConverter
        {
            private readonly int _nestedDepth;

            public NestedObjectGelfConverter(int nestedDepth = 1)
            {
                _nestedDepth = nestedDepth;
            }

            public void WriteGelfJson(LogEvent logEvent, Utf8JsonWriter writer)
            {
                writer.WriteStartObject();

                for (int i = 0; i < _nestedDepth; i++)
                {
                    writer.WriteStartObject("nested");
                }

                for (int i = 0; i < _nestedDepth; i++)
                {
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }
        }
    }
}
