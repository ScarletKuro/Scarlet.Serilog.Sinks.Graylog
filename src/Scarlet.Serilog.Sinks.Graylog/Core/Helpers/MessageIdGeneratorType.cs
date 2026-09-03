namespace Scarlet.Serilog.Sinks.Graylog.Core.Helpers;

/// <summary>
/// Selects an <see cref="IMessageIdGenerator"/> for chunked UDP messages.
/// </summary>
public enum MessageIdGeneratorType
{
    /// <summary>Derive the identifier from the current UTC time.</summary>
    Timestamp,

    /// <summary>Derive the identifier from the content of the message.</summary>
    Md5
}
