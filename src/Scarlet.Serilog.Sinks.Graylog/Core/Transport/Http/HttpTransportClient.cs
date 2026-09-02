using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Transport.Http
{
    public class HttpTransportClient : ITransportClient<string>
    {
        private const string _defaultHttpUriPath = "gelf";

        private readonly Lazy<HttpClient> _httpClient;

        private readonly GraylogSinkOptionsBase options;

        public HttpTransportClient(GraylogSinkOptionsBase options)
        {
            this.options = options;
            _httpClient = new Lazy<HttpClient>(CreateConfiguredHttpClient);
        }

        protected virtual HttpClient CreateHttpClient() => new();

        protected virtual void ConfigureHttpClient(HttpClient httpClient)
        {
            if (string.IsNullOrEmpty(options.HostnameOrAddress))
            {
                throw new InvalidOperationException("The HostnameOrAddress value must be set.");
            }

            var builder = new UriBuilder(options.HostnameOrAddress)
            {
                Port = options.Port.GetValueOrDefault(443)
            };

            if (options.UseSsl)
            {
                builder.Scheme = "https";
            }

            // A trailing slash makes a configured proxy path a base directory for the GELF endpoint.
            if (!builder.Path.EndsWith("/", StringComparison.Ordinal))
            {
                builder.Path += "/";
            }

            httpClient.BaseAddress = builder.Uri;

            httpClient.DefaultRequestHeaders.ExpectContinue = false;
            httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };

            var authenticationHeaderValue = new HttpBasicAuthenticationGenerator(options.UsernameInHttp, options.PasswordInHttp).Generate();

            if (authenticationHeaderValue != null)
            {
                httpClient.DefaultRequestHeaders.Authorization = authenticationHeaderValue;
            }
        }

        private HttpClient CreateConfiguredHttpClient()
        {
            HttpClient httpClient = CreateHttpClient();
            ConfigureHttpClient(httpClient);
            return httpClient;
        }

        public async Task Send(string message)
        {
            HttpClient httpClient = _httpClient.Value;

            var content = new StringContent(message, Encoding.UTF8, "application/json");

            HttpResponseMessage result = await httpClient.PostAsync(_defaultHttpUriPath, content).ConfigureAwait(false);

            // Throwing rather than swallowing is what lets Serilog's batching sink see the failure
            // and retry the batch; the unbatched path reports it through GraylogSink.Emit's
            // fault continuation.
            result.EnsureSuccessStatusCode();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

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
