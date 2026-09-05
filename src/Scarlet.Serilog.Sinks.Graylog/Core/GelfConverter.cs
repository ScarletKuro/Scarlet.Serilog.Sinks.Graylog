using Serilog.Events;
using Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders;
using System;
using System.Text.Json;

namespace Scarlet.Serilog.Sinks.Graylog.Core
{
    /// <summary>
    /// The default <see cref="IGelfConverter"/>, which dispatches to a message builder per event kind.
    /// </summary>
    internal sealed class GelfConverter : IGelfConverter
    {
        private readonly Lazy<GelfMessageBuilder> _messageBuilder;
        private readonly Lazy<ExceptionMessageBuilder> _exceptionBuilder;

        internal GelfConverter(
            string hostName,
            GelfOptions options,
            JsonSerializerOptions serializerOptions)
        {
            _messageBuilder = new Lazy<GelfMessageBuilder>(
                () => new GelfMessageBuilder(hostName, options, serializerOptions));
            _exceptionBuilder = new Lazy<ExceptionMessageBuilder>(
                () => new ExceptionMessageBuilder(hostName, options, serializerOptions));
        }

        /// <inheritdoc />
        /// <remarks>
        /// An event carrying an exception goes to the exception builder, and everything else to the
        /// ordinary message builder.
        /// </remarks>
        public void WriteGelfJson(LogEvent logEvent, Utf8JsonWriter writer)
        {
            if (logEvent.Exception != null)
            {
                _exceptionBuilder.Value.Build(logEvent, writer);
            }
            else
            {
                _messageBuilder.Value.Build(logEvent, writer);
            }
        }
    }
}
