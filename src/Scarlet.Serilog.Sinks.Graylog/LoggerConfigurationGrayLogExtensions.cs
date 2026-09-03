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

        GraylogSinkOptionsValidator.Validate(options);

        var sink = new GraylogSink(options);
        return options.Delivery.Batching is { } batchingOptions
            ? loggerSinkConfiguration.Sink(sink, batchingOptions, options.Delivery.MinimumLevel)
            : loggerSinkConfiguration.Sink((ILogEventSink)sink, options.Delivery.MinimumLevel);
    }

    internal static BatchingOptions? BuildBatchingOptions(
        bool? batched,
        int? batchSizeLimit,
        TimeSpan? bufferingTimeLimit,
        int? queueLimit,
        TimeSpan? retryTimeLimit,
        bool? eagerlyEmitFirstEvent)
    {
        bool hasSettings = batchSizeLimit.HasValue || bufferingTimeLimit.HasValue || queueLimit.HasValue || retryTimeLimit.HasValue || eagerlyEmitFirstEvent.HasValue;
        if (batched == false || (batched == null && !hasSettings))
            return null;

        return new BatchingOptions
        {
            BatchSizeLimit = batchSizeLimit ?? new BatchingOptions().BatchSizeLimit,
            BufferingTimeLimit = bufferingTimeLimit ?? new BatchingOptions().BufferingTimeLimit,
            QueueLimit = queueLimit is > 0 ? queueLimit : queueLimit.HasValue ? null : new BatchingOptions().QueueLimit,
            RetryTimeLimit = retryTimeLimit ?? new BatchingOptions().RetryTimeLimit,
            EagerlyEmitFirstEvent = eagerlyEmitFirstEvent ?? new BatchingOptions().EagerlyEmitFirstEvent
        };
    }
}
