using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Scarlet.Serilog.Sinks.Graylog.Core;
using Scarlet.Serilog.Sinks.Graylog.Core.Extensions;
using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using System;

namespace Scarlet.Serilog.Sinks.Graylog
{
    public static class LoggerConfigurationGrayLogExtensions
    {
        /// <summary>
        /// Graylogs the specified options.
        /// </summary>
        /// <param name="loggerSinkConfiguration">The logger sink configuration.</param>
        /// <param name="options">The options.</param>
        /// <returns></returns>
        public static LoggerConfiguration Graylog(this LoggerSinkConfiguration loggerSinkConfiguration, GraylogSinkOptions options)
        {
            if (loggerSinkConfiguration == null)
            {
                throw new ArgumentNullException(nameof(loggerSinkConfiguration));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var sink = new GraylogSink(options);

            return options.Batching is { } batchingOptions
                ? loggerSinkConfiguration.Sink(sink, batchingOptions, options.MinimumLogEventLevel)
                : loggerSinkConfiguration.Sink((ILogEventSink)sink, options.MinimumLogEventLevel);
        }

        /// <summary>
        /// Graylogs the specified hostname or address.
        /// </summary>
        /// <param name="loggerSinkConfiguration">The logger sink configuration.</param>
        /// <param name="hostnameOrAddress">The hostname or address.</param>
        /// <param name="port">The port.</param>
        /// <param name="transportType">Type of the transport.</param>
        /// <param name="useSsl">Use SSL in Tcp and Http</param>
        /// <param name="minimumLogEventLevel">The minimum log event level.</param>
        /// <param name="messageIdGeneratorType">Type of the message identifier generator.</param>
        /// <param name="shortMessageMaxLength">Short length of the message maximum.</param>
        /// <param name="stackTraceDepth">The stack trace depth.</param>
        /// <param name="facility">The facility.</param>
        /// <param name="maxMessageSizeInUdp">the maxMessageSizeInUdp</param>
        /// <param name="host">The host property to use in GELF message. If null, DNS hostname will be used instead.</param>
        /// <param name="includeMessageTemplate">if set to <c>true</c> if include message template to graylog.</param>
        /// <param name="messageTemplateFieldName">Name of the message template field.</param>
        /// <param name="usernameInHttp">The usernameInHttp. Basic authentication property.</param>
        /// <param name="passwordInHttp">The passwordInHttp. Basic authentication property.</param>
        /// <param name="parseArrayValues">if set to <c>true</c> array values are parsed into separate fields.</param>
        /// <param name="useGzip">if set to <c>true</c> payloads are gzipped where the transport supports it.</param>
        /// <param name="batched">
        /// <c>true</c> to buffer events and deliver them in batches, <c>false</c> to write every event as it is
        /// emitted even if other batching arguments are supplied. When left <c>null</c> (the default) batching is
        /// enabled only if at least one of the other batching arguments is supplied. A batched logger must be
        /// disposed, or flushed with <c>Log.CloseAndFlush()</c>, or the tail of the buffer is lost at shutdown.
        /// </param>
        /// <param name="batchSizeLimit">Maximum number of events in a single batch. Serilog's default is 1000.</param>
        /// <param name="bufferingTimeLimit">Maximum time to wait before delivering a partial batch. Serilog's default is 2 seconds.</param>
        /// <param name="queueLimit">Maximum number of buffered events; events are dropped once it is reached. Serilog's default is 100000. Pass a non-positive value for an unbounded queue.</param>
        /// <param name="retryTimeLimit">How long to keep retrying a failing batch before dropping it. Serilog's default is 10 minutes.</param>
        /// <param name="eagerlyEmitFirstEvent">if set to <c>true</c> the first event is delivered without waiting for the buffering time limit. Serilog's default is <c>true</c>.</param>
        /// <returns></returns>
        public static LoggerConfiguration Graylog(this LoggerSinkConfiguration loggerSinkConfiguration,
                                                  string hostnameOrAddress,
                                                  int port,
                                                  TransportType transportType,
                                                  bool useSsl = false,
                                                  LogEventLevel minimumLogEventLevel = LevelAlias.Minimum,
                                                  MessageIdGeneratorType messageIdGeneratorType = GraylogSinkOptionsBase.DefaultMessageGeneratorType,
                                                  int shortMessageMaxLength = GraylogSinkOptionsBase.DefaultShortMessageMaxLength,
                                                  int stackTraceDepth = GraylogSinkOptionsBase.DefaultStackTraceDepth,
                                                  string? facility = GraylogSinkOptionsBase.DefaultFacility,
                                                  int maxMessageSizeInUdp = GraylogSinkOptionsBase.DefaultMaxMessageSizeInUdp,
                                                  string host = GraylogSinkOptionsBase.DefaultHost,
                                                  bool includeMessageTemplate = false,
                                                  string messageTemplateFieldName = GraylogSinkOptionsBase.DefaultMessageTemplateFieldName,
                                                  string? usernameInHttp = null,
                                                  string? passwordInHttp = null,
                                                  bool parseArrayValues = false,
                                                  bool useGzip = true,
                                                  bool? batched = null,
                                                  int? batchSizeLimit = null,
                                                  TimeSpan? bufferingTimeLimit = null,
                                                  int? queueLimit = null,
                                                  TimeSpan? retryTimeLimit = null,
                                                  bool? eagerlyEmitFirstEvent = null
                                                  )
        {
            // ReSharper disable once UseObjectOrCollectionInitializer
            var options = new GraylogSinkOptions
            {
                HostnameOrAddress = hostnameOrAddress.Expand(),
                Port = port,
                TransportType = transportType,
                UseSsl = useSsl,
                MinimumLogEventLevel = minimumLogEventLevel,
                MessageGeneratorType = messageIdGeneratorType,
                ShortMessageMaxLength = shortMessageMaxLength,
                StackTraceDepth = stackTraceDepth,
                Facility = facility?.Expand(),
                MaxMessageSizeInUdp = maxMessageSizeInUdp,
                HostnameOverride = host,
                IncludeMessageTemplate = includeMessageTemplate,
                MessageTemplateFieldName = messageTemplateFieldName,
                UsernameInHttp = usernameInHttp,
                PasswordInHttp = passwordInHttp,
                ParseArrayValues = parseArrayValues,
                UseGzip = useGzip,
                Batching = BuildBatchingOptions(batched, batchSizeLimit, bufferingTimeLimit, queueLimit, retryTimeLimit, eagerlyEmitFirstEvent)
            };

            return loggerSinkConfiguration.Graylog(options);
        }

        /// <summary>
        /// Turns the individual batching arguments into a <see cref="BatchingOptions"/>, or
        /// <c>null</c> when the sink should stay unbatched.
        /// </summary>
        /// <remarks>
        /// <paramref name="batched"/> is deliberately nullable so that all three intents can be
        /// expressed: <c>true</c> always batches, <c>false</c> never batches even when other
        /// batching arguments were supplied, and <c>null</c> (the default) batches only if at least
        /// one batching argument was supplied. Without the tri-state, passing
        /// <c>batchSizeLimit</c> on its own would silently do nothing.
        /// </remarks>
        internal static BatchingOptions? BuildBatchingOptions(bool? batched,
                                                              int? batchSizeLimit,
                                                              TimeSpan? bufferingTimeLimit,
                                                              int? queueLimit,
                                                              TimeSpan? retryTimeLimit,
                                                              bool? eagerlyEmitFirstEvent)
        {
            bool anySettingSupplied = batchSizeLimit.HasValue
                                      || bufferingTimeLimit.HasValue
                                      || queueLimit.HasValue
                                      || retryTimeLimit.HasValue
                                      || eagerlyEmitFirstEvent.HasValue;

            if (!(batched ?? anySettingSupplied))
            {
                return null;
            }

            var batching = new BatchingOptions();

            if (batchSizeLimit.HasValue)
            {
                batching.BatchSizeLimit = batchSizeLimit.Value;
            }

            if (bufferingTimeLimit.HasValue)
            {
                batching.BufferingTimeLimit = bufferingTimeLimit.Value;
            }

            if (queueLimit.HasValue)
            {
                // BatchingOptions.QueueLimit is int?, where null means unbounded. A non-positive
                // value is the only way to ask for that through this overload.
                batching.QueueLimit = queueLimit.Value > 0 ? queueLimit.Value : null;
            }

            if (retryTimeLimit.HasValue)
            {
                batching.RetryTimeLimit = retryTimeLimit.Value;
            }

            if (eagerlyEmitFirstEvent.HasValue)
            {
                batching.EagerlyEmitFirstEvent = eagerlyEmitFirstEvent.Value;
            }

            return batching;
        }
    }
}
