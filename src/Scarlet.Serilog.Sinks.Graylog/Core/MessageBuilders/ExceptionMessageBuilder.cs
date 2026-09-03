using Serilog.Events;
using System;
using System.Text;
using System.Text.Json.Nodes;

namespace Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders
{
    /// <summary>
    /// Exception builder
    /// </summary>
    /// <seealso cref="GelfMessageBuilder" />
    public class ExceptionMessageBuilder : GelfMessageBuilder
    {
        private const string DefaultExceptionDelimiter = " - ";
        private const string DefaultStackTraceDelimiter = "--- Inner exception stack trace ---";

        /// <summary>
        /// Initializes a new instance of the <see cref="ExceptionMessageBuilder"/> class.
        /// </summary>
        /// <param name="hostName">Name of the host.</param>
        /// <param name="options">The options.</param>
        public ExceptionMessageBuilder(string hostName, GelfOptions options) : base(hostName, options)
        {
        }


        /// <inheritdoc />
        /// <remarks>
        /// Adds <c>_ExceptionSource</c>, <c>_ExceptionType</c>, <c>_ExceptionMessage</c> and
        /// <c>_StackTrace</c> to the GELF message - flattened across inner exceptions, up to
        /// <see cref="GelfOptions.StackTraceDepth"/> levels.
        /// <para>
        /// These are written onto the built message rather than added to the log event as properties.
        /// Serilog hands the same <see cref="LogEvent"/> instance to every sink in the pipeline, so
        /// adding properties to it leaked these fields into the console, file and any other sink - and
        /// under batching it did so from the batching thread, while those sinks could be reading the
        /// event.
        /// </para>
        /// </remarks>
        public override JsonObject Build(LogEvent logEvent)
        {
            JsonObject payload = base.Build(logEvent);

            // GelfConverter only routes to this builder when logEvent.Exception is non-null.
            Exception exception = logEvent.Exception!;

            Tuple<string, string?> excMessageTuple = GetExceptionMessages(exception);
            string exceptionDetail = excMessageTuple.Item1;
            string? stackTrace = excMessageTuple.Item2;
            string? source = exception.Source;
            string type = exception.GetType().FullName!;

            AddGelfField(payload, "ExceptionSource", source);
            AddGelfField(payload, "ExceptionType", type);
            AddGelfField(payload, "ExceptionMessage", exceptionDetail);
            AddGelfField(payload, "StackTrace", stackTrace);

            return payload;
        }

        /// <summary>
        /// Get the message details from all nested exceptions, up to 10 in depth.
        /// </summary>
        /// <param name="ex">Exception to get details for</param>
        private Tuple<string, string?> GetExceptionMessages(Exception ex)
        {
            var exceptionSb = new StringBuilder();
            var stackSb = new StringBuilder();
            Exception? nestedException = ex;
            string? stackDetail = null;

            var counter = 0;
            do
            {
                exceptionSb.Append(nestedException.Message).Append(DefaultExceptionDelimiter);
                if (nestedException.StackTrace != null)
                {
                    stackSb.AppendLine(nestedException.StackTrace).AppendLine(DefaultStackTraceDelimiter);
                }
                nestedException = nestedException.InnerException;
                counter++;
            }
            while (nestedException != null && counter < Options.StackTraceDepth);

            string exceptionDetail = exceptionSb.ToString().Substring(0, exceptionSb.Length - DefaultExceptionDelimiter.Length).Trim();

            if (stackSb.Length > 0)
            {
                stackDetail = stackSb.ToString().Substring(0, stackSb.Length - DefaultStackTraceDelimiter.Length - 2).Trim();
            }

            return new Tuple<string, string?>(exceptionDetail, stackDetail);
        }
    }
}
