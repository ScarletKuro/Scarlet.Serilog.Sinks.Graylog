using System.Security.Cryptography.X509Certificates;

namespace Scarlet.Serilog.Sinks.Graylog;

/// <summary>Configures TLS server naming and optional client authentication.</summary>
public sealed class TlsOptions
{
    /// <summary>Gets or sets the name the server certificate must match; defaults to the configured host. Used by the TCP transport only.</summary>
    public string? ServerName { get; set; }

    /// <summary>
    /// Gets or sets a client certificate presented for mutual TLS, as an alternative to
    /// <see cref="ClientCertificatePath"/>.
    /// </summary>
    /// <remarks>
    /// For a certificate already held in memory - one fetched from a secret store, or loaded from a
    /// certificate store - which would otherwise have to be written to disk just to be configured
    /// here. It must carry a private key, and it cannot be combined with
    /// <see cref="ClientCertificatePath"/>.
    /// <para>
    /// The certificate stays owned by the caller: the sink neither copies nor disposes it, so one
    /// instance can be shared between sinks and outlive them. Dispose it once every logger using it
    /// is closed.
    /// </para>
    /// <para>
    /// Settable in code only. <c>Serilog.Settings.Configuration</c> cannot bind a certificate from
    /// JSON, so configuration-driven setups use <see cref="ClientCertificatePath"/>.
    /// </para>
    /// </remarks>
    public X509Certificate2? ClientCertificate { get; set; }

    /// <summary>Gets or sets the path to a PFX client certificate presented for mutual TLS.</summary>
    public string? ClientCertificatePath { get; set; }

    /// <summary>Gets or sets the password protecting <see cref="ClientCertificatePath"/>.</summary>
    public string? ClientCertificatePassword { get; set; }
}
