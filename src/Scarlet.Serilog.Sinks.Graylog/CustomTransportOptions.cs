using System;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport;

namespace Scarlet.Serilog.Sinks.Graylog;

/// <summary>Configures a code-only custom transport.</summary>
public sealed class CustomTransportOptions
{
    /// <summary>Gets or sets the factory invoked to create the transport when <see cref="GraylogSinkOptions.TransportType"/> is <see cref="TransportType.Custom"/>.</summary>
    public Func<ITransport>? Factory { get; set; }
}
