using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Helpers
{
    internal static class TlsCertificateLoader
    {
        public static X509Certificate2 LoadClientCertificate(TlsOptions options)
        {
            string path = options.ClientCertificatePath ?? throw new InvalidOperationException("A TLS client certificate path is required.");
            if (!File.Exists(path))
                throw new FileNotFoundException($"The TLS client certificate file '{path}' was not found.", path);

            try
            {
#pragma warning disable SYSLIB0057 // The supported loader API is unavailable on the legacy targets.
                return new X509Certificate2(path, options.ClientCertificatePassword);
#pragma warning restore SYSLIB0057
            }
            catch (Exception exception) when (exception is CryptographicException || exception is IOException || exception is UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"The TLS client certificate file '{path}' could not be loaded. Ensure it is an accessible PFX file and that its password is correct.", exception);
            }
        }
    }
}
