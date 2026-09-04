using Serilog.Events;
using System.Text.Json;

namespace Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders
{
    /// <summary>
    /// Build json message for graylog
    /// </summary>
    public interface IMessageBuilder
    {
        /// <summary>
        /// Writes the GELF message for a log event.
        /// </summary>
        /// <param name="logEvent">The log event.</param>
        /// <param name="writer">The writer the payload is written to.</param>
        /// <remarks>
        /// The implementation writes one complete JSON object, opening and closing it itself, and
        /// leaves the writer otherwise as it found it. Flushing is the caller's business.
        /// </remarks>
        void Build(LogEvent logEvent, Utf8JsonWriter writer);
    }
}
