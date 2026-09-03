using System.Text.Json;
using Scarlet.Serilog.Sinks.Graylog.Core;

namespace Scarlet.Serilog.Sinks.Graylog;

/// <summary>Configures how a log event is turned into a GELF message.</summary>
public sealed class GelfOptions
{
    /// <summary>Gets or sets the GELF host field.</summary>
    public string? HostnameOverride { get; set; }

    /// <summary>Gets or sets the GELF facility field.</summary>
    public string? Facility { get; set; }

    /// <summary>Gets or sets the maximum short-message length.</summary>
    public int ShortMessageMaxLength { get; set; } = 500;

    /// <summary>Gets or sets the maximum exception inner-stack depth.</summary>
    public int StackTraceDepth { get; set; } = 10;

    /// <summary>Gets or sets whether the message template is included.</summary>
    public bool IncludeMessageTemplate { get; set; }

    /// <summary>Gets or sets whether template properties are excluded.</summary>
    public bool ExcludeMessageTemplateProperties { get; set; }

    /// <summary>Gets or sets the GELF field name for a message template.</summary>
    public string MessageTemplateFieldName { get; set; } = "message_template";

    /// <summary>Gets or sets whether array values are parsed into GELF fields.</summary>
    public bool ParseArrayValues { get; set; }

    /// <summary>Gets or sets JSON serializer behavior for GELF values.</summary>
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = new() { WriteIndented = false };

    /// <summary>Gets or sets an optional code-configured GELF converter.</summary>
    public IGelfConverter? Converter { get; set; }
}
