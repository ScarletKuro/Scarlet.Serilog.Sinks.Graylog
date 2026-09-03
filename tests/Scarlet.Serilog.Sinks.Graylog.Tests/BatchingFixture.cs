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

        [Theory]
        // batched == null: batch only when some other batching value was supplied.
        [InlineData(null, null, null, false)]
        [InlineData(null, 500, null, true)]
        [InlineData(null, null, 42, true)]
        // batched == true: always batch.
        [InlineData(true, null, null, true)]
        [InlineData(true, 500, null, true)]
        // batched == false: never batch, even alongside other batching values.
        [InlineData(false, null, null, false)]
        [InlineData(false, 500, null, false)]
        public void BuildBatchingOptions_AppliesTheTriStateRule(bool? batched, int? batchSizeLimit, int? queueLimit, bool expectBatching)
        {
            object? result = InvokeBuildBatchingOptions(batched, batchSizeLimit, null, queueLimit, null, null);

            if (expectBatching)
            {
                Assert.NotNull(result);
            } else
            {
                Assert.Null(result);
            }
        }

        [Fact]
        public void BuildBatchingOptions_MapsSuppliedValues()
        {
            var result = (BatchingOptions?)InvokeBuildBatchingOptions(true, 500, TimeSpan.FromSeconds(7), 4242, TimeSpan.FromMinutes(3), false);

            Assert.NotNull(result);
            Assert.Equal(500, result.BatchSizeLimit);
            Assert.Equal(TimeSpan.FromSeconds(7), result.BufferingTimeLimit);
            Assert.Equal(4242, result.QueueLimit);
            Assert.Equal(TimeSpan.FromMinutes(3), result.RetryTimeLimit);
            Assert.False(result.EagerlyEmitFirstEvent);
        }

        [Fact]
        public void BuildBatchingOptions_LeavesUnsuppliedValuesAtSerilogDefaults()
        {
            var result = (BatchingOptions?)InvokeBuildBatchingOptions(true, null, null, null, null, null);
            var defaults = new BatchingOptions();

            Assert.NotNull(result);
            Assert.Equal(defaults.BatchSizeLimit, result.BatchSizeLimit);
            Assert.Equal(defaults.BufferingTimeLimit, result.BufferingTimeLimit);
            Assert.Equal(defaults.QueueLimit, result.QueueLimit);
            Assert.Equal(defaults.RetryTimeLimit, result.RetryTimeLimit);
            Assert.Equal(defaults.EagerlyEmitFirstEvent, result.EagerlyEmitFirstEvent);
        }

        [Fact]
        public void BuildBatchingOptions_TreatsANonPositiveQueueLimitAsUnbounded()
        {
            // BatchingOptions.QueueLimit is int? where null means unbounded, which a plain int?
            // parameter cannot otherwise express.
            var result = (BatchingOptions?)InvokeBuildBatchingOptions(null, null, null, 0, null, null);

            Assert.NotNull(result);
            Assert.Null(result.QueueLimit);
        }

        /// <summary>
        /// The sink assembly is strong-name signed and the test assemblies are not, so
        /// <c>InternalsVisibleTo</c> is not available; reach the internal helper by reflection
        /// rather than widening the public API for the sake of a test.
        /// </summary>
        private static object? InvokeBuildBatchingOptions(bool? batched,
                                                          int? batchSizeLimit,
                                                          TimeSpan? bufferingTimeLimit,
                                                          int? queueLimit,
                                                          TimeSpan? retryTimeLimit,
                                                          bool? eagerlyEmitFirstEvent)
        {
            var method = typeof(LoggerConfigurationGrayLogExtensions)
                .GetMethod("BuildBatchingOptions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            Assert.NotNull(method);

            return method.Invoke(null, new object?[]
            {
                batched, batchSizeLimit, bufferingTimeLimit, queueLimit, retryTimeLimit, eagerlyEmitFirstEvent
            });
        }
    }
}
