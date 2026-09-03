namespace Scarlet.Serilog.Sinks.Graylog;

/// <summary>Configures TLS server naming and optional PFX client authentication.</summary>
public sealed class TlsOptions
{
    /// <summary>Gets or sets the name the server certificate must match; defaults to the configured host. Used by the TCP transport only.</summary>
    public string? ServerName { get; set; }

    /// <summary>Gets or sets the path to a PFX client certificate presented for mutual TLS.</summary>
    public string? ClientCertificatePath { get; set; }

    /// <summary>Gets or sets the password protecting <see cref="ClientCertificatePath"/>.</summary>
    public string? ClientCertificatePassword { get; set; }
}
