using Scarlet.Serilog.Sinks.Graylog.Core.Transport;

namespace Scarlet.Serilog.Sinks.Graylog.Core;

internal interface ISinkComponentsBuilder
{
    ITransport MakeTransport();
    IGelfConverter MakeGelfConverter();
}
