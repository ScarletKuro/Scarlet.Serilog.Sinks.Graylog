#nullable enable

using FluentAssertions;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
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
        private static GraylogSinkOptions OptionsFor(ITransport transport, Action<GraylogSinkOptions>? configure = null)
        {
            var options = new GraylogSinkOptions
            {
                HostnameOrAddress = "localhost",
                Port = 12201,
                TransportType = TransportType.Custom,
                TransportFactory = () => transport
            };

            configure?.Invoke(options);

            return options;
        }

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
            using var sink = new GraylogSink(OptionsFor(transport));

            await sink.EmitBatchAsync(Events(3));

            transport.Payloads.Should().HaveCount(3);
        }

        [Fact]
        public async Task EmitBatchAsync_SendsEventsOneAtATime()
        {
            // Concurrent sends would interleave TCP GELF frames on the single shared stream.
            var transport = new RecordingTransport(_ => Task.Delay(20));
            using var sink = new GraylogSink(OptionsFor(transport));

            await sink.EmitBatchAsync(Events(5));

            transport.MaxObservedConcurrency.Should().Be(1);
        }

        [Fact]
        public async Task EmitBatchAsync_PropagatesTransportFailures()
        {
            // Serilog's batching infrastructure owns retry, so failures must not be swallowed.
            var transport = new RecordingTransport(_ => Task.FromException(new InvalidOperationException("boom")));
            using var sink = new GraylogSink(OptionsFor(transport));

            Func<Task> act = () => sink.EmitBatchAsync(Events(1));

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        }

        [Fact]
        public void Emit_DoesNotPropagateTransportFailures()
        {
            // The unbatched path stays fire-and-forget and reports through SelfLog instead.
            var transport = new RecordingTransport(_ => Task.FromException(new InvalidOperationException("boom")));
            using var sink = new GraylogSink(OptionsFor(transport));

            sink.Invoking(s => s.Emit(LogEventSource.GetSimpleLogEvent(DateTimeOffset.Now)))
                .Should().NotThrow();
        }

        [Fact]
        public async Task EmitBatchAsync_HonoursJsonSerializerOptions()
        {
            // Regression guard: JsonNode.ToString() hard-codes Indented = true, so the sink must
            // serialize through ToJsonString(options) instead.
            var compact = new RecordingTransport();
            using (var sink = new GraylogSink(OptionsFor(compact)))
            {
                await sink.EmitBatchAsync(Events(1));
            }

            compact.Payloads.Should().ContainSingle().Which.Should().NotContain("\n");

            var indented = new RecordingTransport();
            using (var sink = new GraylogSink(OptionsFor(indented, o => o.JsonSerializerOptions = new JsonSerializerOptions { WriteIndented = true })))
            {
                await sink.EmitBatchAsync(Events(1));
            }

            indented.Payloads.Should().ContainSingle().Which.Should().Contain("\n");
        }

        [Fact]
        public async Task OnEmptyBatchAsync_Completes()
        {
            // Serilog only supplies a default implementation of this on net6.0+, so netstandard2.0
            // needs our own body; this is the canary for it.
            var transport = new RecordingTransport();
            using var sink = new GraylogSink(OptionsFor(transport));

            await sink.OnEmptyBatchAsync();

            transport.Payloads.Should().BeEmpty();
        }

        [Fact]
        public void Dispose_DoesNotCreateTheTransportWhenNothingWasEmitted()
        {
            int created = 0;
            var transport = new RecordingTransport();

            var sink = new GraylogSink(OptionsFor(transport, o => o.TransportFactory = () =>
            {
                created++;
                return transport;
            }));

            sink.Dispose();

            created.Should().Be(0);
            transport.DisposeCount.Should().Be(0);
        }

        [Fact]
        public async Task Dispose_DisposesTheTransportOnceItExists()
        {
            var transport = new RecordingTransport();
            var sink = new GraylogSink(OptionsFor(transport));

            await sink.EmitBatchAsync(Events(1));

            sink.Dispose();
            sink.Dispose();

            transport.DisposeCount.Should().Be(1);
        }

        [Fact]
        public void GraylogSink_ImplementsBothSinkContracts()
        {
            var transport = new RecordingTransport();
            using var sink = new GraylogSink(OptionsFor(transport));

            sink.Should().BeAssignableTo<ILogEventSink>();
            sink.Should().BeAssignableTo<IBatchedLogEventSink>();
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
                result.Should().NotBeNull();
            } else
            {
                result.Should().BeNull();
            }
        }

        [Fact]
        public void BuildBatchingOptions_MapsSuppliedValues()
        {
            var result = (BatchingOptions?)InvokeBuildBatchingOptions(true, 500, TimeSpan.FromSeconds(7), 4242, TimeSpan.FromMinutes(3), false);

            result.Should().NotBeNull();
            result!.BatchSizeLimit.Should().Be(500);
            result.BufferingTimeLimit.Should().Be(TimeSpan.FromSeconds(7));
            result.QueueLimit.Should().Be(4242);
            result.RetryTimeLimit.Should().Be(TimeSpan.FromMinutes(3));
            result.EagerlyEmitFirstEvent.Should().BeFalse();
        }

        [Fact]
        public void BuildBatchingOptions_LeavesUnsuppliedValuesAtSerilogDefaults()
        {
            var result = (BatchingOptions?)InvokeBuildBatchingOptions(true, null, null, null, null, null);
            var defaults = new BatchingOptions();

            result.Should().NotBeNull();
            result!.BatchSizeLimit.Should().Be(defaults.BatchSizeLimit);
            result.BufferingTimeLimit.Should().Be(defaults.BufferingTimeLimit);
            result.QueueLimit.Should().Be(defaults.QueueLimit);
            result.RetryTimeLimit.Should().Be(defaults.RetryTimeLimit);
            result.EagerlyEmitFirstEvent.Should().Be(defaults.EagerlyEmitFirstEvent);
        }

        [Fact]
        public void BuildBatchingOptions_TreatsANonPositiveQueueLimitAsUnbounded()
        {
            // BatchingOptions.QueueLimit is int? where null means unbounded, which a plain int?
            // parameter cannot otherwise express.
            var result = (BatchingOptions?)InvokeBuildBatchingOptions(null, null, null, 0, null, null);

            result.Should().NotBeNull();
            result!.QueueLimit.Should().BeNull();
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

            method.Should().NotBeNull("LoggerConfigurationGrayLogExtensions.BuildBatchingOptions should exist");

            return method!.Invoke(null, new object?[]
            {
                batched, batchSizeLimit, bufferingTimeLimit, queueLimit, retryTimeLimit, eagerlyEmitFirstEvent
            });
        }
    }
}
