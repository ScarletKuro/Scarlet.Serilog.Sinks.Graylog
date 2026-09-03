using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;

namespace Scarlet.Serilog.Sinks.Graylog;

/// <summary>Configures UDP GELF delivery.</summary>
public sealed class UdpTransportOptions
{
    /// <summary>Gets or sets the host name or IP address of the Graylog GELF UDP input.</summary>
    public string? Host { get; set; }

    /// <summary>Gets or sets the port of the Graylog GELF UDP input.</summary>
    public int Port { get; set; } = 12201;

    /// <summary>Gets or sets the compression applied to the GELF payload before it is chunked.</summary>
    public UdpCompression Compression { get; set; } = UdpCompression.Gzip;

    /// <summary>Gets or sets the largest datagram sent; a longer payload is split into GELF chunks.</summary>
    public int MaximumDatagramSize { get; set; } = 8192;

    /// <summary>Gets or sets how the message identifier shared by the chunks of one payload is generated.</summary>
    public MessageIdGeneratorType MessageIdGeneratorType { get; set; } = MessageIdGeneratorType.Timestamp;
}
