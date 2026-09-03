using Scarlet.Serilog.Sinks.Graylog.Core.Transport;

namespace Scarlet.Serilog.Sinks.Graylog
{
    /// <summary>Configures the Graylog sink. Transport sections other than the selected <see cref="TransportType"/> are ignored.</summary>
    public sealed class GraylogSinkOptions
    {
        /// <summary>Gets or sets the transport used to deliver GELF messages.</summary>
        public TransportType TransportType { get; set; } = TransportType.Udp;

        /// <summary>Gets or sets GELF payload options.</summary>
        public GelfOptions Message { get; set; } = new();

        /// <summary>Gets or sets delivery and batching options.</summary>
        public DeliveryOptions Delivery { get; set; } = new();

        /// <summary>Gets or sets UDP transport options.</summary>
        public UdpTransportOptions Udp { get; set; } = new();

        /// <summary>Gets or sets TCP transport options.</summary>
        public TcpTransportOptions Tcp { get; set; } = new();

        /// <summary>Gets or sets HTTP transport options.</summary>
        public HttpTransportOptions Http { get; set; } = new();

        /// <summary>Gets or sets custom transport options.</summary>
        public CustomTransportOptions Custom { get; set; } = new();
    }
}
