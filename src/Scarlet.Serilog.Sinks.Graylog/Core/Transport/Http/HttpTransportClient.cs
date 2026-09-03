using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport.Http
{
    /// <summary>
    /// Posts GELF messages to a Graylog HTTP input.
    /// </summary>
    /// <remarks>
    /// The <see cref="HttpClient"/> is created and configured on the first send and reused afterwards,
    /// so changes to the options stop taking effect once a message has been sent. Override
    /// <see cref="CreateHttpClient"/> or <see cref="ConfigureHttpClient"/> to take over either half.
    /// </remarks>
    /// <seealso cref="ITransportClient{T}" />
    public class HttpTransportClient : ITransportClient<string>
    {
        private const string DefaultHttpUriPath = "gelf";

        private readonly Lazy<HttpClient> _httpClient;

        private readonly HttpTransportOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpTransportClient"/> class.
        /// </summary>
        /// <param name="options">The HTTP transport options.</param>
        public HttpTransportClient(HttpTransportOptions options)
        {
            _options = options;
            _httpClient = new Lazy<HttpClient>(CreateConfiguredHttpClient);
        }


        /// <summary>
        /// Creates the <see cref="HttpClient"/> used for every request.
        /// </summary>
        /// <returns>
        /// A plain client, or one over a handler carrying the client certificate when
        /// <see cref="TlsOptions.ClientCertificatePath"/> is set.
        /// </returns>
        protected virtual HttpClient CreateHttpClient()
        {
            if (string.IsNullOrWhiteSpace(_options.Tls?.ClientCertificatePath))
            {
                return new HttpClient();
            }

 #if NET462
            var handler = new WinHttpHandler();
            handler.ClientCertificateOption = ClientCertificateOption.Manual;
 #else
            var handler = new HttpClientHandler();
 #endif
            handler.ClientCertificates.Add(TlsCertificateLoader.LoadClientCertificate(_options.Tls!));
            return new HttpClient(handler, true);
        }

        /// <summary>
        /// Applies the base address, the default headers and the configured authentication.
        /// </summary>
        /// <param name="httpClient">The client to configure.</param>
        /// <exception cref="InvalidOperationException">
        /// <see cref="HttpTransportOptions.Endpoint"/> is missing or relative, or a header in
        /// <see cref="HttpTransportOptions.Headers"/> has an empty name, overrides <c>Content-Type</c>,
        /// or is otherwise rejected.
        /// </exception>
        protected virtual void ConfigureHttpClient(HttpClient httpClient)
        {
            if (_options.Endpoint == null || !_options.Endpoint.IsAbsoluteUri)
            {
                throw new InvalidOperationException("The HTTP endpoint must be an absolute URI.");
            }

            var builder = new UriBuilder(_options.Endpoint);

            // A trailing slash makes a configured proxy path a base directory for the GELF endpoint.
            if (!builder.Path.EndsWith("/", StringComparison.Ordinal))
            {
                builder.Path += "/";
            }

            httpClient.BaseAddress = builder.Uri;

            httpClient.DefaultRequestHeaders.ExpectContinue = false;
            httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };

            var authenticationHeaderValue = new HttpBasicAuthenticationGenerator(_options.BasicAuthentication?.Username, _options.BasicAuthentication?.Password).Generate();

            if (authenticationHeaderValue != null)
            {
                httpClient.DefaultRequestHeaders.Authorization = authenticationHeaderValue;
            }

            ConfigureCustomHeaders(httpClient);
        }

        private void ConfigureCustomHeaders(HttpClient httpClient)
        {
            if (_options.Headers == null)
            {
                return;
            }

            foreach (var header in _options.Headers)
            {
                if (string.IsNullOrWhiteSpace(header.Key))
                {
                    throw new InvalidOperationException("HTTP header names must not be empty.");
                }

                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("The HTTP transport always sends application/json content.");
                }

                httpClient.DefaultRequestHeaders.Remove(header.Key);
                if (!httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value))
                {
                    throw new InvalidOperationException($"The HTTP header '{header.Key}' could not be configured.");
                }
            }
        }

        private HttpClient CreateConfiguredHttpClient()
        {
            HttpClient httpClient = CreateHttpClient();
            ConfigureHttpClient(httpClient);
            return httpClient;
        }

        /// <inheritdoc />
        /// <exception cref="HttpRequestException">Graylog answered with a non-success status code.</exception>
        public async Task Send(string message)
        {
            HttpClient httpClient = _httpClient.Value;

            var content = new StringContent(message, Encoding.UTF8, "application/json");

            HttpResponseMessage result = await httpClient.PostAsync(DefaultHttpUriPath, content).ConfigureAwait(false);

            // A 413 has one cause and one fix, and neither is obvious from the bare status code.
            if (result.StatusCode == HttpStatusCode.RequestEntityTooLarge)
            {
                throw new HttpRequestException(
                    $"Graylog rejected a {Encoding.UTF8.GetByteCount(message)}-byte GELF message as too large. "
                    + "The HTTP input caps the decompressed message at its 'max_chunk_size', 65536 bytes by default, "
                    + "and GELF defines no chunking over HTTP - raise that setting on the input, shorten the event, "
                    + "or use the UDP or TCP transport, which do split large messages.");
            }

            // Throwing rather than swallowing is what lets Serilog's batching sink see the failure
            // and retry the batch; the unbatched path reports it through GraylogSink.Emit's
            // fault continuation.
            result.EnsureSuccessStatusCode();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the resources used by this client.
        /// </summary>
        /// <param name="disposing"><c>true</c> when called from <see cref="Dispose()"/>; the <see cref="HttpClient"/> is disposed with it, if one was ever created.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_httpClient.IsValueCreated)
                {
                    _httpClient.Value.Dispose();
                }
            }
        }
    }
}
