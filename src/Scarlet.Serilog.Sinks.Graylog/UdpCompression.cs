namespace Scarlet.Serilog.Sinks.Graylog;

/// <summary>Specifies UDP GELF compression.</summary>
public enum UdpCompression
{
    /// <summary>Send the GELF payload uncompressed.</summary>
    None,

    /// <summary>Compress the GELF payload with gzip.</summary>
    Gzip
}
