using System;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Helpers;

/// <summary>
/// Generates the message id from the current UTC tick count.
/// </summary>
/// <seealso cref="IMessageIdGenerator" />
internal sealed class TimestampMessageIdGenerator : IMessageIdGenerator
{
    /// <inheritdoc />
    public byte[] GenerateMessageId(byte[] message)
    {
        return BitConverter.GetBytes(DateTime.UtcNow.Ticks);
    }
}
