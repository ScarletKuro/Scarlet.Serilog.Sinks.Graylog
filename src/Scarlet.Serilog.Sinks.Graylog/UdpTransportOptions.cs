using System;

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

    /// <summary>
    /// Gets or sets how long a resolved <see cref="Host"/> is reused before it is looked up again;
    /// <c>null</c> resolves once and never again.
    /// </summary>
    /// <remarks>
    /// UDP has no connection to fail, so without this a host that moves - a rotated Kubernetes
    /// Service, a DNS failover - would be written to at its old address for the life of the sink. The
    /// default matches .NET's own <c>ServicePointManager.DnsRefreshTimeout</c>. Re-resolving is cheap:
    /// <c>System.Net.Dns</c> caches nothing itself, so the answer comes from the operating system
    /// resolver cache, which honours the record's TTL. Ignored when <see cref="Host"/> is an IP
    /// literal, which is never resolved in the first place.
    /// </remarks>
    public TimeSpan? DnsRefreshInterval { get; set; } = TimeSpan.FromMinutes(2);
}
