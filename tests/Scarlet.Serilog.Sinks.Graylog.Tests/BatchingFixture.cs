using Scarlet.Serilog.Sinks.Graylog.Tests.Fakes;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests
{
    public class BatchingFixture
    {
        private static LogEvent[] Events(int count)
        {
            var events = new LogEvent[count];

            for (int i = 0; i < count; i++)
            {
                events[i] = LogEventSource.GetSimpleLogEvent(DateTimeOffset.Now.AddSeconds(i));
            }

            return events;
        }

        [Fact]
        public async Task EmitBatchAsync_SendsOnePayloadPerEvent()
        {
            var transport = new RecordingTransport();
            using var sink = new GraylogSink(transport.SinkOptions());

            await sink.EmitBatchAsync(Events(3));

            Assert.Equal(3, transport.Payloads.Count);
        }

        [Fact]
        public async Task EmitBatchAsync_SendsEventsOneAtATime()
        {
            // Concurrent sends would interleave TCP GELF frames on the single shared stream.
            var transport = new RecordingTransport(_ => Task.Delay(20));
            using var sink = new GraylogSink(transport.SinkOptions());

            await sink.EmitBatchAsync(Events(5));

            Assert.Equal(1, transport.MaxObservedConcurrency);
        }

        [Fact]
        public async Task EmitBatchAsync_PropagatesTransportFailures()
        {
            // Serilog's batching infrastructure owns retry, so failures must not be swallowed.
            var transport = new RecordingTransport(_ => Task.FromException(new InvalidOperationException("boom")));
            using var sink = new GraylogSink(transport.SinkOptions());

            Task Act() => sink.EmitBatchAsync(Events(1));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(Act);
            Assert.Equal("boom", exception.Message);
        }

        [Fact]
        public void Emit_DoesNotPropagateTransportFailures()
        {
            // The unbatched path stays fire-and-forget and reports through SelfLog instead.
            var transport = new RecordingTransport(_ => Task.FromException(new InvalidOperationException("boom")));
            using var sink = new GraylogSink(transport.SinkOptions());

            var exception = Record.Exception(() => sink.Emit(LogEventSource.GetSimpleLogEvent(DateTimeOffset.Now)));
            Assert.Null(exception);
        }

        [Fact]
        public async Task EmitBatchAsync_HonoursJsonSerializerOptions()
        {
            // Regression guard: JsonNode.ToString() hard-codes Indented = true, so the sink must
            // serialize through ToJsonString(options) instead.
            var compact = new RecordingTransport();
            using (var sink = new GraylogSink(compact.SinkOptions()))
            {
                await sink.EmitBatchAsync(Events(1));
            }

            Assert.DoesNotContain("\n", Assert.Single(compact.Payloads));

            var indented = new RecordingTransport();
            using (var sink = new GraylogSink(indented.SinkOptions(o => o.Message.JsonSerializerOptions = new JsonSerializerOptions { WriteIndented = true })))
            {
                await sink.EmitBatchAsync(Events(1));
            }

            Assert.Contains("\n", Assert.Single(indented.Payloads));
        }

        [Fact]
        public async Task OnEmptyBatchAsync_Completes()
        {
            // Serilog only supplies a default implementation of this on net6.0+, so netstandard2.0
            // and .NET Framework need our own body; this is the canary for it.
            var transport = new RecordingTransport();
            using var sink = new GraylogSink(transport.SinkOptions());

            await sink.OnEmptyBatchAsync();

            Assert.Empty(transport.Payloads);
        }

        [Fact]
        public void Dispose_DoesNotCreateTheTransportWhenNothingWasEmitted()
        {
            int created = 0;
            var transport = new RecordingTransport();

            var sink = new GraylogSink(transport.SinkOptions(o => o.Custom.Factory = () =>
            {
                created++;
                return transport;
            }));

            sink.Dispose();

            Assert.Equal(0, created);
            Assert.Equal(0, transport.DisposeCount);
        }

        [Fact]
        public async Task Dispose_DisposesTheTransportOnceItExists()
        {
            var transport = new RecordingTransport();
            var sink = new GraylogSink(transport.SinkOptions());

            await sink.EmitBatchAsync(Events(1));

            sink.Dispose();
            sink.Dispose();

            Assert.Equal(1, transport.DisposeCount);
        }

        [Fact]
        public void GraylogSink_ImplementsBothSinkContracts()
        {
            var transport = new RecordingTransport();
            using var sink = new GraylogSink(transport.SinkOptions());

            Assert.IsAssignableFrom<ILogEventSink>(sink);
            Assert.IsAssignableFrom<IBatchedLogEventSink>(sink);
        }
    }
}
