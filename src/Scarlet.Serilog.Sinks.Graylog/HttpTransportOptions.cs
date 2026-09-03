using System;
using System.Collections.Generic;

namespace Scarlet.Serilog.Sinks.Graylog;

/// <summary>Configures HTTP GELF delivery.</summary>
public sealed class HttpTransportOptions
{
    /// <summary>Gets or sets the absolute URI of the Graylog HTTP input; messages are posted to <c>gelf</c> beneath it. Use an <c>https</c> URI for TLS.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Gets or sets headers sent with every request. <c>Content-Type</c> cannot be overridden, and an <c>Authorization</c> header set here wins over <see cref="BasicAuthentication"/>.</summary>
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>Gets or sets HTTP basic authentication credentials; <c>null</c> sends no <c>Authorization</c> header.</summary>
    public HttpBasicAuthenticationOptions? BasicAuthentication { get; set; }

    /// <summary>Gets or sets client-certificate options for mutual TLS. Only the certificate members apply here - the server name comes from <see cref="Endpoint"/>.</summary>
    public TlsOptions? Tls { get; set; }

    /// <summary>
    /// Gets or sets how long a request may take before it is abandoned; <c>null</c> leaves
    /// <see cref="System.Net.Http.HttpClient.Timeout"/> at its 100-second default.
    /// </summary>
    /// <remarks>
    /// The default is deliberately shorter than <see cref="System.Net.Http.HttpClient"/>'s own: the
    /// other transports bound their waits too, and an unbatched send that hangs for a minute and a half
    /// holds a slot in <see cref="DeliveryOptions.ShutdownTimeout"/> and, under
    /// <see cref="DeliveryOptions.Batching"/>, stalls the batch behind it.
    /// </remarks>
    public TimeSpan? Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long a pooled connection is reused before it is replaced; <c>null</c> keeps
    /// connections for the life of the process.
    /// </summary>
    /// <remarks>
    /// This is what makes a long-running application notice that Graylog's address has changed. The
    /// connection pool resolves the host when it opens a connection and not again, so without a
    /// lifetime a process that has been up for weeks still posts to whatever address it resolved at
    /// startup. The UDP transport re-resolves on <see cref="UdpTransportOptions.DnsRefreshInterval"/>
    /// and the TCP one on every reconnect; this is the HTTP equivalent.
    /// <para>
    /// Honoured through <c>SocketsHttpHandler.PooledConnectionLifetime</c> on .NET, and through the
    /// endpoint's <c>ServicePoint.ConnectionLeaseTimeout</c> on .NET Framework. The net462 build uses
    /// <c>WinHttpHandler</c> when a client certificate is configured, which pools outside
    /// <c>ServicePointManager</c> and so ignores this.
    /// </para>
    /// </remarks>
    public TimeSpan? ConnectionLifetime { get; set; } = TimeSpan.FromMinutes(2);
}
