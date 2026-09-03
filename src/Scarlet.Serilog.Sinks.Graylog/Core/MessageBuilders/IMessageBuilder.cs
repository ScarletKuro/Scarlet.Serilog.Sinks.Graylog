using Serilog.Events;
using System.Text.Json.Nodes;

namespace Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders
{
    /// <summary>
    /// Build json message for graylog
    /// </summary>
    public interface IMessageBuilder
    {
        /// <summary>
        /// Builds the specified log event.
        /// </summary>
        /// <param name="logEvent">The log event.</param>
        /// <returns>The GELF message for the event.</returns>
        JsonObject Build(LogEvent logEvent);
    }
}
