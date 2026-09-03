using System.Text.Json.Nodes;
using Serilog.Events;

namespace Scarlet.Serilog.Sinks.Graylog.Core;

/// <summary>
/// Turns a log event into the GELF message that is sent to Graylog.
/// </summary>
/// <remarks>
/// Assign an implementation to <see cref="GelfOptions.Converter"/> to take over GELF payload
/// construction entirely. The conversion runs on the thread that emitted the event.
/// </remarks>
public interface IGelfConverter
{
    /// <summary>
    /// Builds the GELF message for a log event.
    /// </summary>
    /// <param name="logEvent">The log event to convert.</param>
    /// <returns>The GELF message, serialized to JSON by the caller.</returns>
    JsonObject GetGelfJson(LogEvent logEvent);
}
