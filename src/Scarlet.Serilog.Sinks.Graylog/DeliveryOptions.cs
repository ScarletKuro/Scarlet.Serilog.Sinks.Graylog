using System;
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

    /// <summary>
    /// Gets or sets how long disposing the sink waits for sends that are already in flight;
    /// <c>null</c> does not wait at all.
    /// </summary>
    /// <remarks>
    /// Only the unbatched path has anything to wait for - <see cref="Batching"/> awaits every send
    /// before it returns. Without this, a process that exits shortly after logging loses whatever was
    /// still on the wire, silently: the transport is torn down underneath the send. The wait is bounded
    /// so an unreachable Graylog cannot hold up process exit. On net8.0 and later the sink also
    /// implements <c>IAsyncDisposable</c>, so <c>await Log.CloseAndFlushAsync()</c> drains without
    /// blocking a thread.
    /// </remarks>
    public TimeSpan? ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
