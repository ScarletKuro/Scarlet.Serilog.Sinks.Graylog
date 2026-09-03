using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Http;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Tcp;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp;
using System;
using System.Collections.Generic;
using System.Net;
using SinkTransportType = Scarlet.Serilog.Sinks.Graylog.Core.Transport.TransportType;

namespace Scarlet.Serilog.Sinks.Graylog.Core
{
    internal class SinkComponentsBuilder : ISinkComponentsBuilder
    {
        private readonly GraylogSinkOptions _options;
        private readonly Dictionary<BuilderType, Lazy<IMessageBuilder>> _builders;

        public SinkComponentsBuilder(GraylogSinkOptions options)
        {
            _options = options;

            _builders = new Dictionary<BuilderType, Lazy<IMessageBuilder>>
            {
                [BuilderType.Exception] = new(() =>
                {
                    string hostName = Dns.GetHostName();
                    return new ExceptionMessageBuilder(hostName, _options.Message);
                }),
                [BuilderType.Message] = new(() =>
                {
                    string hostName = Dns.GetHostName();
                    return new GelfMessageBuilder(hostName, _options.Message);
                })
            };
        }

        public ITransport MakeTransport()
        {
            switch (_options.TransportType)
            {
                case SinkTransportType.Udp:
                    var chunkSettings = new ChunkSettings(_options.Udp.MessageIdGeneratorType, _options.Udp.MaximumDatagramSize);
                    IDataToChunkConverter chunkConverter = new DataToChunkConverter(chunkSettings, new MessageIdGeneratorResolver());

                    var udpClient = new UdpTransportClient(_options.Udp, new DnsWrapper());
                    var udpTransport = new UdpTransport(udpClient, chunkConverter, _options.Udp);

                    return udpTransport;
                case SinkTransportType.Http:
                    var httpClient = new HttpTransportClient(_options.Http);

                    return new HttpTransport(httpClient);
                case SinkTransportType.Tcp:
                    var tcpClient = new TcpTransportClient(_options.Tcp, new DnsWrapper());

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

        public IGelfConverter MakeGelfConverter() => _options.Message.Converter ?? new GelfConverter(_builders);
    }
}
