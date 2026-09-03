using System;
using System.Collections.Generic;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp;

/// <summary>
/// Splits a GELF payload into the datagrams sent over UDP.
/// </summary>
public interface IDataToChunkConverter
{
    /// <summary>
    /// Converts to chunks.
    /// </summary>
    /// <param name="message">The complete GELF payload.</param>
    /// <returns>
    /// The datagrams to send, in order. A payload that already fits is returned as a single
    /// unmodified chunk, without a GELF chunk header.
    /// </returns>
    /// <exception cref="ArgumentException">The payload needs more chunks than GELF allows.</exception>
    IList<byte[]> ConvertToChunks(byte[] message);
}
