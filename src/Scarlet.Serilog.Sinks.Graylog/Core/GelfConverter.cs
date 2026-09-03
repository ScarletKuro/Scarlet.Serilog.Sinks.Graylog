using Serilog.Events;
using Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Scarlet.Serilog.Sinks.Graylog.Core
{
    /// <summary>
    /// The default <see cref="IGelfConverter"/>, which dispatches to a message builder per event kind.
    /// </summary>
    public class GelfConverter : IGelfConverter
    {
        private readonly IDictionary<BuilderType, Lazy<IMessageBuilder>> _messageBuilders;

        /// <summary>
        /// Initializes a new instance of the <see cref="GelfConverter"/> class.
        /// </summary>
        /// <param name="messageBuilders">
        /// The builder to use per <see cref="BuilderType"/>. Both <see cref="BuilderType.Exception"/>
        /// and <see cref="BuilderType.Message"/> must be present; each is constructed on first use.
        /// </param>
        public GelfConverter(IDictionary<BuilderType, Lazy<IMessageBuilder>> messageBuilders)
        {
            _messageBuilders = messageBuilders;
        }

        /// <inheritdoc />
        /// <remarks>
        /// An event carrying an exception goes to the <see cref="BuilderType.Exception"/> builder, and
        /// everything else to the <see cref="BuilderType.Message"/> one.
        /// </remarks>
        public JsonObject GetGelfJson(LogEvent logEvent)
        {
            IMessageBuilder builder = logEvent.Exception != null
                ? _messageBuilders[BuilderType.Exception].Value
                : _messageBuilders[BuilderType.Message].Value;

            return builder.Build(logEvent);
        }
    }
}
