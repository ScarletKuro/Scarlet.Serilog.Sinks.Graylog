using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using Serilog.Events;
using System;
using System.Text;
using System.Text.Json;

namespace Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders
{
    /// <summary>
    /// Exception builder
    /// </summary>
    /// <seealso cref="GelfMessageBuilder" />
    public sealed class ExceptionMessageBuilder : GelfMessageBuilder
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

        internal ExceptionMessageBuilder(
            string hostName,
            GelfOptions options,
            JsonSerializerOptions serializerOptions)
            : base(hostName, options, serializerOptions)
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
        protected override void WriteExtraFields(LogEvent logEvent, GelfFieldWriter fields)
        {
            // GelfConverter only routes to this builder when logEvent.Exception is non-null.
            Exception exception = logEvent.Exception!;

            fields.WriteField("ExceptionSource", exception.Source);
            fields.WriteField("ExceptionType", exception.GetType().FullName);

            var messages = new StringBuilder();
            var stackTraces = new StringBuilder();

            Flatten(exception, messages, stackTraces);

            fields.WriteField("ExceptionMessage", messages.ToString().Trim());

            if (stackTraces.Length > 0)
            {
                fields.WriteField("StackTrace", stackTraces.ToString().Trim());
            }
            else
            {
                fields.WriteField("StackTrace", null);
            }
        }

        /// <summary>
        /// Joins the messages of the exception chain, and the stack trace of every link that has one,
        /// up to <see cref="GelfOptions.StackTraceDepth"/> levels deep.
        /// </summary>
        private void Flatten(Exception exception, StringBuilder messages, StringBuilder stackTraces)
        {
            Exception? nested = exception;
            int counter = 0;

            do
            {
                if (counter > 0)
                {
                    messages.Append(DefaultExceptionDelimiter);
                }

                messages.Append(nested.Message);

                // Read once. Exception.StackTrace formats the trace from scratch on every access, so
                // testing it and then writing it walked and rebuilt the whole thing twice - on the
                // largest field of the largest payload the sink produces.
                string? stackTrace = nested.StackTrace;

                if (stackTrace != null)
                {
                    if (stackTraces.Length > 0)
                    {
                        stackTraces.AppendLine();
                        stackTraces.AppendLine(DefaultStackTraceDelimiter);
                    }

                    stackTraces.Append(stackTrace);
                }

                nested = nested.InnerException;
                counter++;
            }
            while (nested != null && counter < Options.StackTraceDepth);
        }
    }
}
