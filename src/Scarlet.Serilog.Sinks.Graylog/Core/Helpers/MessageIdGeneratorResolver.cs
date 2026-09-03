using System;
using System.Collections.Generic;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Helpers
{
    /// <summary>
    /// The default <see cref="IMessageIdGeneratorResolver"/>. Each generator is created on first use
    /// and reused afterwards.
    /// </summary>
    internal sealed class MessageIdGeneratorResolver : IMessageIdGeneratorResolver
    {
        private readonly Dictionary<MessageIdGeneratorType, Lazy<IMessageIdGenerator>> _messageGenerators = new()
        {
            [MessageIdGeneratorType.Timestamp] = new Lazy<IMessageIdGenerator>(() => new TimestampMessageIdGenerator()),
            [MessageIdGeneratorType.Md5] = new Lazy<IMessageIdGenerator>(() => new Md5MessageIdGenerator())
        };

        /// <inheritdoc />
        /// <exception cref="KeyNotFoundException"><paramref name="generatorType"/> is not one of the mapped values.</exception>
        public IMessageIdGenerator Resolve(MessageIdGeneratorType generatorType)
        {
            return _messageGenerators[generatorType].Value;
        }
    }
}
