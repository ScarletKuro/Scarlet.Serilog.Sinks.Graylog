using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp;
using System;
using System.Linq;

namespace Scarlet.Serilog.Sinks.Graylog;

internal static class GraylogSinkOptionsValidator
{
    // The largest payload an IPv4 UDP datagram can carry: 65535 less the 20-byte IP and 8-byte UDP
    // headers. Anything above this can never leave the machine.
    private const int MaximumUdpPayload = 65507;

    public static void Validate(GraylogSinkOptions options)
    {
        Require(options.Message, nameof(options.Message));
        Require(options.Delivery, nameof(options.Delivery));
        ValidateTimeout(options.Delivery.ShutdownTimeout, nameof(options.Delivery.ShutdownTimeout));

        switch (options.TransportType)
        {
            case TransportType.Udp:
                Require(options.Udp, nameof(options.Udp));
                ValidateHostAndPort(options.Udp.Host, options.Udp.Port, "UDP", nameof(options.Udp.Host), nameof(options.Udp.Port));
                if (options.Udp.MaximumDatagramSize <= ChunkSettings.PrefixSize)
                    throw new ArgumentOutOfRangeException(nameof(options.Udp.MaximumDatagramSize), $"The UDP maximum datagram size must exceed the GELF chunk header size of {ChunkSettings.PrefixSize} bytes.");
                if (options.Udp.MaximumDatagramSize > MaximumUdpPayload)
                    throw new ArgumentOutOfRangeException(nameof(options.Udp.MaximumDatagramSize), $"The UDP maximum datagram size cannot exceed {MaximumUdpPayload} bytes.");
                ValidateTimeout(options.Udp.DnsRefreshInterval, nameof(options.Udp.DnsRefreshInterval));
                break;

            case TransportType.Tcp:
                Require(options.Tcp, nameof(options.Tcp));
                ValidateHostAndPort(options.Tcp.Host, options.Tcp.Port, "TCP", nameof(options.Tcp.Host), nameof(options.Tcp.Port));
                ValidateTimeout(options.Tcp.ConnectTimeout, nameof(options.Tcp.ConnectTimeout));
                ValidateTimeout(options.Tcp.WriteTimeout, nameof(options.Tcp.WriteTimeout));
                ValidateTls(options.Tcp.Tls, "TCP");
                break;

            case TransportType.Http:
                Require(options.Http, nameof(options.Http));
                if (options.Http.Endpoint == null || !options.Http.Endpoint.IsAbsoluteUri ||
                    (options.Http.Endpoint.Scheme != Uri.UriSchemeHttp && options.Http.Endpoint.Scheme != Uri.UriSchemeHttps))
                    throw new ArgumentException("The HTTP endpoint must be an absolute HTTP or HTTPS URI.", nameof(options.Http.Endpoint));

                if (options.Http.Headers?.Keys.Any(string.IsNullOrWhiteSpace) == true)
                    throw new ArgumentException("HTTP header names cannot be empty.", nameof(options.Http.Headers));
                if (options.Http.Headers?.Keys.Any(key => string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase)) == true)
                    throw new ArgumentException("HTTP headers cannot override the GELF content type.", nameof(options.Http.Headers));

                var authentication = options.Http.BasicAuthentication;
                if (authentication != null && (string.IsNullOrWhiteSpace(authentication.Username) || authentication.Password == null))
                    throw new ArgumentException("HTTP basic authentication requires both a username and password.", nameof(options.Http.BasicAuthentication));

                if (options.Http.Tls != null && options.Http.Endpoint.Scheme != Uri.UriSchemeHttps)
                    throw new ArgumentException("HTTP TLS options require an HTTPS endpoint.", nameof(options.Http.Tls));
                // The HTTP transport takes the name to validate from the endpoint URI, so a server name
                // set here would be silently ignored rather than honoured.
                if (options.Http.Tls?.ServerName != null)
                    throw new ArgumentException("The HTTP transport takes the TLS server name from the endpoint; set it on the endpoint host instead.", nameof(options.Http.Tls));
                ValidateTls(options.Http.Tls, "HTTP");
                ValidateTimeout(options.Http.Timeout, nameof(options.Http.Timeout));
                ValidateTimeout(options.Http.ConnectionLifetime, nameof(options.Http.ConnectionLifetime));
                break;

            case TransportType.Custom:
                Require(options.Custom, nameof(options.Custom));
                if (options.Custom.Factory == null)
                    throw new ArgumentException("A custom transport factory must be configured for the custom transport.", nameof(options.Custom.Factory));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(options.TransportType), options.TransportType, "The selected transport is not supported.");
        }
    }

    private static void ValidateHostAndPort(string? host, int port, string transport, string hostName, string portName)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException($"The {transport} host must be configured.", hostName);
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(portName, $"The {transport} port must be between 1 and 65535.");
    }

    private static void Require<T>(T? value, string name) where T : class
    {
        if (value == null)
            throw new ArgumentNullException(name);
    }

    private static void ValidateTimeout(TimeSpan? timeout, string name)
    {
        if (timeout is { } value && value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(name, "A timeout must be greater than zero or null to disable it.");
    }

    private static void ValidateTls(TlsOptions? tls, string transport)
    {
        if (tls == null)
            return;

        if (string.IsNullOrWhiteSpace(tls.ServerName) && tls.ServerName != null)
            throw new ArgumentException($"The {transport} TLS server name cannot be empty.", nameof(tls.ServerName));

        if (tls.ClientCertificate != null)
        {
            if (tls.ClientCertificatePath != null)
                throw new ArgumentException($"The {transport} TLS options cannot set both a client certificate and a client certificate path.", nameof(tls.ClientCertificate));
            // Meaningless for an already-loaded certificate, and quietly ignoring it would leave the
            // impression that a password was applied.
            if (tls.ClientCertificatePassword != null)
                throw new ArgumentException($"The {transport} TLS client certificate password applies to a certificate file; a client certificate supplied in memory needs none.", nameof(tls.ClientCertificatePassword));
            // Without one there is no way to answer the server's certificate request, and the
            // handshake fails well away from the configuration that caused it.
            if (!tls.ClientCertificate.HasPrivateKey)
                throw new ArgumentException($"The {transport} TLS client certificate must have a private key.", nameof(tls.ClientCertificate));

            return;
        }

        if (string.IsNullOrWhiteSpace(tls.ClientCertificatePath) && tls.ClientCertificatePassword != null)
            throw new ArgumentException($"The {transport} TLS client certificate password requires a certificate path.", nameof(tls.ClientCertificatePassword));
    }
}
