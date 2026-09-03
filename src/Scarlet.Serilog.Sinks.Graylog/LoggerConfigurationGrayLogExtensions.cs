using Serilog.Configuration;
using Serilog.Core;
using Serilog;
using Scarlet.Serilog.Sinks.Graylog;
using System;

namespace Scarlet.Serilog.Sinks.Graylog;

/// <summary>
/// Extension methods for configuring the Graylog sink.
/// </summary>
public static class LoggerConfigurationGrayLogExtensions
{
    /// <summary>
    /// Adds a Graylog GELF sink.
    /// </summary>
    /// <param name="loggerSinkConfiguration">The logger sink configuration.</param>
    /// <param name="options">The Graylog sink options.</param>
    /// <returns>The logger configuration.</returns>
    public static LoggerConfiguration Graylog(
        this LoggerSinkConfiguration loggerSinkConfiguration,
        GraylogSinkOptions options)
    {
        if (loggerSinkConfiguration == null)
            throw new ArgumentNullException(nameof(loggerSinkConfiguration));

        if (options == null)
            throw new ArgumentNullException(nameof(options));

        var sink = new GraylogSink(options);
        return options.Delivery.Batching is { } batchingOptions
            ? loggerSinkConfiguration.Sink(sink, batchingOptions, options.Delivery.MinimumLevel)
            : loggerSinkConfiguration.Sink((ILogEventSink)sink, options.Delivery.MinimumLevel);
    }
}
