namespace Scarlet.Serilog.Sinks.Graylog.Core.Helpers;

/// <summary>
/// Maps a <see cref="MessageIdGeneratorType"/> to the generator that implements it.
/// </summary>
public interface IMessageIdGeneratorResolver
{
    /// <summary>
    /// Resolves the generator for a generator type.
    /// </summary>
    /// <param name="generatorType">The configured generator type.</param>
    /// <returns>The generator to use.</returns>
    IMessageIdGenerator Resolve(MessageIdGeneratorType generatorType);
}
