namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport
{
    /// <summary>
    /// Specifies which transport delivers GELF messages to Graylog.
    /// </summary>
    public enum TransportType
    {
        /// <summary>GELF over UDP, chunked when the payload exceeds the datagram size. Does not support TLS.</summary>
        Udp,

        /// <summary>GELF over HTTP; TLS comes from using an <c>https</c> endpoint.</summary>
        Http,

        /// <summary>GELF over TCP, as null-terminated frames on a persistent connection. Supports TLS.</summary>
        Tcp,

        /// <summary>A transport supplied in code through <see cref="CustomTransportOptions.Factory"/>.</summary>
        Custom
    }
}
