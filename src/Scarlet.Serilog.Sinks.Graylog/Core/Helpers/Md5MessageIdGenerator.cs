using System.Linq;
using System.Security.Cryptography;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Helpers;

/// <summary>
/// Generates the message id from the first 8 bytes of the MD5 hash of the message.
/// </summary>
/// <seealso cref="IMessageIdGenerator" />
internal sealed class Md5MessageIdGenerator : IMessageIdGenerator
{
    /// <inheritdoc />
    public byte[] GenerateMessageId(byte[] message)
    {
#if !NET
            using MD5 md5 = MD5.Create();

            byte[] messageHash = md5.ComputeHash(message);
#else
        byte[] messageHash = MD5.HashData(message);
#endif

        return messageHash.Take(8).ToArray();
    }
}
