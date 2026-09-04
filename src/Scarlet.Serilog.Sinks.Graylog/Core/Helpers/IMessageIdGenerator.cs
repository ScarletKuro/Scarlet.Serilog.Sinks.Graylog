using System;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Helpers;

/// <summary>
/// Produces the 8-byte identifier that ties the chunks of one GELF message together.
/// </summary>
public interface IMessageIdGenerator
{
    /// <summary>
    /// Generates the message identifier.
    /// </summary>
    /// <param name="message">The complete GELF payload the chunks were split from.</param>
    /// <returns>Eight bytes, unique enough that two messages in flight at once do not collide.</returns>
    byte[] GenerateMessageId(ReadOnlyMemory<byte> message);
}
