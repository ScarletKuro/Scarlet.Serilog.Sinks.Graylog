using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Helpers
{
    internal static class TlsCertificateLoader
    {
        /// <summary>
        /// Reports whether a client certificate is configured at all, by either route.
        /// </summary>
        public static bool HasClientCertificate(TlsOptions? options)
        {
            return options != null
                   && (options.ClientCertificate != null || !string.IsNullOrWhiteSpace(options.ClientCertificatePath));
        }

        /// <summary>
        /// Resolves the configured client certificate, and reports whether the caller owns what it
        /// gets back and therefore has to dispose it.
        /// </summary>
        /// <returns>
        /// The certificate, and <c>true</c> when it was loaded here - a certificate supplied through
        /// <see cref="TlsOptions.ClientCertificate"/> stays owned by the caller who supplied it, who
        /// may share the instance between sinks or keep using it after one is disposed.
        /// </returns>
        public static (X509Certificate2 Certificate, bool Owned) ResolveClientCertificate(TlsOptions options)
        {
            if (options.ClientCertificate is { } configured)
            {
                return (configured, false);
            }

            string path = options.ClientCertificatePath ?? throw new InvalidOperationException("A TLS client certificate path is required.");
            if (!File.Exists(path))
                throw new FileNotFoundException($"The TLS client certificate file '{path}' was not found.", path);

            try
            {
#pragma warning disable SYSLIB0057 // The supported loader API is unavailable on the legacy targets.
                return (new X509Certificate2(path, options.ClientCertificatePassword), true);
#pragma warning restore SYSLIB0057
            }
            catch (Exception exception) when (exception is CryptographicException || exception is IOException || exception is UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"The TLS client certificate file '{path}' could not be loaded. Ensure it is an accessible PFX file and that its password is correct.", exception);
            }
        }
    }
}
