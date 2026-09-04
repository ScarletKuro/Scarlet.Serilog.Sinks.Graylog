using System.Text.Json;
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
    /// Writes the GELF message for a log event.
    /// </summary>
    /// <param name="logEvent">The log event to convert.</param>
    /// <param name="writer">The writer the payload is written to.</param>
    /// <remarks>
    /// The implementation writes one complete JSON object, opening and closing it itself. The sink
    /// flushes the writer and owns the buffer underneath it, so nothing written here may be retained
    /// past the call.
    /// </remarks>
    void WriteGelfJson(LogEvent logEvent, Utf8JsonWriter writer);
}
