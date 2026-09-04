using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
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
    /// <seealso cref="ITransportClient" />
    public class HttpTransportClient : ITransportClient
    {
        private const string DefaultHttpUriPath = "gelf";

        private readonly Lazy<HttpClient> _httpClient;

        private readonly HttpTransportOptions _options;

        /// <summary>
        /// A client certificate this client loaded itself, and therefore has to dispose. Only ever
        /// assigned inside the <see cref="Lazy{T}"/> factory, so no synchronization is needed.
        /// </summary>
        private X509Certificate2? _ownedClientCertificate;

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
        /// <returns>A client over the handler from <see cref="CreateHttpMessageHandler"/>, which it owns.</returns>
        /// <remarks>
        /// The handler is built explicitly rather than left to <c>new HttpClient()</c>, because on .NET
        /// <c>SocketsHttpHandler.PooledConnectionLifetime</c> is the only way to make a long-lived
        /// client notice that Graylog's address has changed: the connection pool otherwise keeps a
        /// connection for the life of the process and never resolves the host again. The UDP and TCP
        /// transports re-resolve on their own schedule; without this, HTTP alone would keep posting to
        /// an address that had moved.
        /// </remarks>
        protected virtual HttpClient CreateHttpClient()
        {
            return new HttpClient(CreateHttpMessageHandler(), disposeHandler: true);
        }

        /// <summary>
        /// Creates the handler underneath the <see cref="HttpClient"/>.
        /// </summary>
        /// <returns>
        /// A handler that retires pooled connections after
        /// <see cref="HttpTransportOptions.ConnectionLifetime"/>, carrying the client certificate when
        /// <see cref="TlsOptions.ClientCertificate"/> or <see cref="TlsOptions.ClientCertificatePath"/>
        /// is set.
        /// </returns>
        /// <remarks>
        /// Override this to keep the sink's client configuration and supply a different pipeline - a
        /// proxy handler, or one that retries. <see cref="CreateHttpClient"/> disposes whatever this
        /// returns along with the client.
        /// </remarks>
        protected virtual HttpMessageHandler CreateHttpMessageHandler()
        {
            bool hasClientCertificate = TlsCertificateLoader.HasClientCertificate(_options.Tls);

#if NET
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = _options.ConnectionLifetime ?? Timeout.InfiniteTimeSpan
            };

            if (hasClientCertificate)
            {
                handler.SslOptions.ClientCertificates = new X509CertificateCollection { ResolveClientCertificate() };
            }

            return handler;
#elif NET462
            // WinHttpHandler is the one handler on net462 that can present a client certificate the
            // application chose rather than one the platform picked; it also pools outside
            // ServicePointManager, so ConnectionLifetime cannot reach it. Everything else stays on the
            // default handler, where ConfigureHttpClient applies the lifetime through the service point.
            if (hasClientCertificate)
            {
                var winHttpHandler = new WinHttpHandler { ClientCertificateOption = ClientCertificateOption.Manual };

                winHttpHandler.ClientCertificates.Add(ResolveClientCertificate());

                return winHttpHandler;
            }

            return new HttpClientHandler();
#else
            var handler = new HttpClientHandler();

            if (hasClientCertificate)
            {
                handler.ClientCertificates.Add(ResolveClientCertificate());
            }

            return handler;
#endif
        }

        /// <summary>
        /// Loads the configured client certificate, recording it for disposal when this client is the
        /// one that loaded it.
        /// </summary>
        /// <remarks>
        /// A handler holds the certificate for the life of the client but never disposes the
        /// collection's contents, so anything loaded here is this client's to release.
        /// </remarks>
        private X509Certificate2 ResolveClientCertificate()
        {
            (X509Certificate2 certificate, bool owned) = TlsCertificateLoader.ResolveClientCertificate(_options.Tls!);

            if (owned)
            {
                _ownedClientCertificate = certificate;
            }

            return certificate;
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

            if (_options.Timeout is { } timeout)
            {
                httpClient.Timeout = timeout;
            }

            ApplyConnectionLifetime(builder.Uri);

            httpClient.DefaultRequestHeaders.ExpectContinue = false;
            httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };

            var authenticationHeaderValue = new HttpBasicAuthenticationGenerator(_options.BasicAuthentication?.Username, _options.BasicAuthentication?.Password).Generate();

            if (authenticationHeaderValue != null)
            {
                httpClient.DefaultRequestHeaders.Authorization = authenticationHeaderValue;
            }

            ConfigureCustomHeaders(httpClient);
        }

        /// <summary>
        /// Retires pooled connections to the endpoint after
        /// <see cref="HttpTransportOptions.ConnectionLifetime"/>, on the frameworks where the handler
        /// itself offers no such setting.
        /// </summary>
        /// <remarks>
        /// <c>ConnectionLeaseTimeout</c> is the .NET Framework equivalent of
        /// <c>SocketsHttpHandler.PooledConnectionLifetime</c>: closing the connection once the
        /// lease expires is what forces the next request to resolve the host again. Only the service
        /// point for this endpoint is touched, never the process-wide defaults.
        /// </remarks>
        private void ApplyConnectionLifetime(Uri endpoint)
        {
#if !NET
            if (_options.ConnectionLifetime is not { } lifetime)
            {
                return;
            }

            double milliseconds = lifetime.TotalMilliseconds;

            ServicePointManager.FindServicePoint(endpoint).ConnectionLeaseTimeout =
                milliseconds >= int.MaxValue ? int.MaxValue : (int)milliseconds;
#endif
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
        public async Task Send(ReadOnlyMemory<byte> message)
        {
            HttpClient httpClient = _httpClient.Value;

            // The payload is already UTF-8, so it is posted as bytes over the buffer the sink filled.
            // StringContent would have transcoded a string the sink had transcoded from UTF-8 in the
            // first place, and allocated the whole message again to do it.
            ArraySegment<byte> segment = Helpers.PooledByteBuffer.AsArraySegment(message);

            using var content = new ByteArrayContent(segment.Array!, segment.Offset, segment.Count);

            content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
            {
                CharSet = Encoding.UTF8.WebName
            };

            // The response is buffered by the time PostAsync returns, so the connection is already back
            // in the pool - but the response still holds the buffered content, and leaving it to the
            // finalizer keeps one message's worth of it alive per event.
            using HttpResponseMessage result = await httpClient.PostAsync(DefaultHttpUriPath, content).ConfigureAwait(false);

            // A 413 has one cause and one fix, and neither is obvious from the bare status code.
            if (result.StatusCode == HttpStatusCode.RequestEntityTooLarge)
            {
                throw new HttpRequestException(
                    $"Graylog rejected a {message.Length}-byte GELF message as too large. "
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
        /// <remarks>
        /// A client certificate loaded from <see cref="TlsOptions.ClientCertificatePath"/> is released
        /// after the <see cref="HttpClient"/>, so nothing still in flight is using its key. One
        /// supplied through <see cref="TlsOptions.ClientCertificate"/> belongs to the caller and is
        /// left alone.
        /// </remarks>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_httpClient.IsValueCreated)
                {
                    _httpClient.Value.Dispose();
                }

                _ownedClientCertificate?.Dispose();
                _ownedClientCertificate = null;
            }
        }
    }
}
