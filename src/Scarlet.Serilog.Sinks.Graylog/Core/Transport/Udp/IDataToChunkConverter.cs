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
    /// <remarks>
    /// <see cref="UdpTransport"/> only calls this for a payload that needs splitting, so that the
    /// common case - one datagram - costs neither a list nor a copy.
    /// </remarks>
    /// <exception cref="ArgumentException">The payload needs more chunks than GELF allows.</exception>
    IList<byte[]> ConvertToChunks(ReadOnlyMemory<byte> message);
}
