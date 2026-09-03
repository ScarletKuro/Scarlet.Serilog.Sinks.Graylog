using System;

namespace Scarlet.Serilog.Sinks.Graylog;

/// <summary>Configures TCP GELF delivery.</summary>
public sealed class TcpTransportOptions
{
    /// <summary>Gets or sets the host name or IP address of the Graylog GELF TCP input.</summary>
    public string? Host { get; set; }

    /// <summary>Gets or sets the port of the Graylog GELF TCP input.</summary>
    public int Port { get; set; } = 12201;

    /// <summary>Gets or sets TLS options; <c>null</c> connects in plaintext.</summary>
    public TlsOptions? Tls { get; set; }

    /// <summary>Gets or sets whether TCP keep-alive is enabled on the socket.</summary>
    public bool EnableKeepAlive { get; set; } = true;

    /// <summary>Gets or sets how long to wait for the connection; <c>null</c> waits indefinitely.</summary>
    public TimeSpan? ConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Gets or sets how long to wait for a write and its flush; <c>null</c> waits indefinitely.</summary>
    public TimeSpan? WriteTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
