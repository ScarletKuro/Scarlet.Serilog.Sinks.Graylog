using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Serilog.Events;
using Serilog.Parsing;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Aot.Tests
{
    /// <summary>
    /// Emits a single scalar through the real sink and hands back the JSON it wrote for that field.
    /// </summary>
    internal static class SinkHarness
    {
        /// <summary>
        /// The JSON the sink writes for a single additional field, taken from a real emitted payload.
        /// </summary>
        public static string FieldJson(object? value) => FieldJson(value, new JsonSerializerOptions());

        public static string FieldJson(object? value, JsonSerializerOptions serializerOptions)
        {
            var transport = new RecordingTransport();

            var options = new GraylogSinkOptions
            {
                TransportType = TransportType.Custom,
                Custom = new CustomTransportOptions { Factory = () => transport },
                Message = new GelfOptions { Facility = "aot-harness", JsonSerializerOptions = serializerOptions }
            };

            using (var sink = new GraylogSink(options))
            {
                var logEvent = new LogEvent(DateTimeOffset.UnixEpoch, LogEventLevel.Information, null,
                    new MessageTemplate("", Array.Empty<MessageTemplateToken>()),
                    new[] { new LogEventProperty("Val", new ScalarValue(value)) });

                // EmitBatchAsync rather than Emit: it propagates failures instead of routing them to
                // SelfLog, so a broken case fails the test with an exception rather than a missing payload.
                sink.EmitBatchAsync(new[] { logEvent }).GetAwaiter().GetResult();
            }

            if (transport.Payloads.Count != 1)
            {
                throw new InvalidOperationException($"expected 1 payload, got {transport.Payloads.Count}");
            }

            using JsonDocument document = JsonDocument.Parse(transport.Payloads[0]);

            if (!document.RootElement.TryGetProperty("_Val", out JsonElement field))
            {
                throw new InvalidOperationException("payload has no _Val field");
            }

            return field.GetRawText();
        }
    }

    /// <summary>
    /// An <see cref="ITransport"/> that records what was sent instead of touching the network.
    /// </summary>
    /// <remarks>
    /// Deliberately not the richer <c>Fakes.RecordingTransport</c> from the main test project. The value
    /// of this project is a publish graph small enough that an IL warning can only have come from the
    /// sink, so nothing is linked in that the tests here do not actually need.
    /// </remarks>
    internal sealed class RecordingTransport : ITransport
    {
        public List<string> Payloads { get; } = new List<string>();

        public Task Send(string message)
        {
            Payloads.Add(message);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Not a scalar Serilog would produce, but an enricher can put anything in a ScalarValue.
    /// </summary>
    internal sealed class Unknown
    {
        public override string ToString() => "unknown!";
    }

    internal enum ByteEnum : byte
    {
        Value = 200
    }

    internal enum UlongEnum : ulong
    {
        Max = ulong.MaxValue
    }

    internal enum LongEnum : long
    {
        Negative = -5
    }
}
