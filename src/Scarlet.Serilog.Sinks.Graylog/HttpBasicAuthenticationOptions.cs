namespace Scarlet.Serilog.Sinks.Graylog;

/// <summary>Configures HTTP basic authentication. Both members must be set, or no credentials are sent.</summary>
public sealed class HttpBasicAuthenticationOptions
{
    /// <summary>Gets or sets the user name.</summary>
    public string? Username { get; set; }

    /// <summary>Gets or sets the password.</summary>
    public string? Password { get; set; }
}
