using Serilog.Configuration;
using Serilog.Events;

namespace Scarlet.Serilog.Sinks.Graylog;

/// <summary>Configures event filtering and optional batching.</summary>
public sealed class DeliveryOptions
{
    /// <summary>Gets or sets the minimum level delivered to Graylog.</summary>
    public LogEventLevel MinimumLevel { get; set; } = LevelAlias.Minimum;

    /// <summary>Gets or sets optional Serilog batching behavior.</summary>
    public BatchingOptions? Batching { get; set; }
}
