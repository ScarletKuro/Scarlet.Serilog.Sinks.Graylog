namespace Scarlet.Serilog.Sinks.Graylog.Core.Helpers;

/// <summary>
/// Generates the message id from eight cryptographically random bytes.
/// </summary>
/// <seealso cref="IMessageIdGenerator" />
internal sealed class RandomMessageIdGenerator : IMessageIdGenerator
{
    /// <inheritdoc />
    public byte[] GenerateMessageId(byte[] message)
    {
        return SecureRandom.NextBytes(8);
    }
}
