using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Http;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Tcp;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp;
using System;
using System.Net;
using System.Text.Json;
using SinkTransportType = Scarlet.Serilog.Sinks.Graylog.Core.Transport.TransportType;

namespace Scarlet.Serilog.Sinks.Graylog.Core
{
    internal class SinkComponentsBuilder
    {
        private readonly GraylogSinkOptions _options;

        /// <summary>The serializer configuration captured when the sink was constructed.</summary>
        internal JsonSerializerOptions JsonSerializerOptions { get; }

        public SinkComponentsBuilder(GraylogSinkOptions options)
        {
            _options = options;
            JsonSerializerOptions = new JsonSerializerOptions(options.Message.JsonSerializerOptions);
        }

        public ITransport MakeTransport()
        {
            switch (_options.TransportType)
            {
                case SinkTransportType.Udp:
                    var udpClient = new UdpTransportClient(_options.Udp);
                    var udpTransport = new UdpTransport(udpClient, _options.Udp);

                    return udpTransport;
                case SinkTransportType.Http:
                    var httpClient = new HttpTransportClient(_options.Http);

                    return new HttpTransport(httpClient);
                case SinkTransportType.Tcp:
                    var tcpClient = new TcpTransportClient(_options.Tcp);

                    return new TcpTransport(tcpClient);
                case SinkTransportType.Custom:
                    if (_options.Custom.Factory == null)
                    {
                        throw new InvalidOperationException("The TransportFactory value must have a value.");
                    }

                    return _options.Custom.Factory();
                default:
                    throw new ArgumentOutOfRangeException(nameof(_options), _options.TransportType, null);
            }
        }

        public IGelfConverter MakeGelfConverter() => _options.Message.Converter ?? new GelfConverter(Dns.GetHostName(), _options.Message, JsonSerializerOptions);
    }
}
