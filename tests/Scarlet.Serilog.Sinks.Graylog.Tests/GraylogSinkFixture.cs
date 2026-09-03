using NSubstitute;
using Scarlet.Serilog.Sinks.Graylog.Tests.Fakes;
using Serilog;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Parsing;
using Scarlet.Serilog.Sinks.Graylog.Core;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests
{
    public class GraylogSinkFixture
    {
        [Fact(Skip = "This test not work anymore because IMessageBuilder gets from internal dictionary")]
        public void WhenEmit_ThenSendData()
        {
            var gelfConverter = Substitute.For<IGelfConverter>();
            var transport = Substitute.For<ITransport>();

            var options = new GraylogSinkOptions
            {
                Message = new GelfOptions { Converter = gelfConverter },
                TransportType = TransportType.Udp,
                Udp = new UdpTransportOptions { Host = "localhost" }
            };

            GraylogSink target = new(options);

            var logEvent = new LogEvent(DateTimeOffset.Now, LogEventLevel.Fatal, null,
                new MessageTemplate("O_o", new List<MessageTemplateToken>()), new List<LogEventProperty>());

            transport.Send(JsonSerializer.Serialize(new { })).Returns(Task.CompletedTask);


            //gelfConverter.GetGelfJson(logEvent).Returns(jObject);

            target.Emit(logEvent);

            transport.Received().Send(Arg.Any<string>());
        }

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
            } finally
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
    }
}
