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
}
