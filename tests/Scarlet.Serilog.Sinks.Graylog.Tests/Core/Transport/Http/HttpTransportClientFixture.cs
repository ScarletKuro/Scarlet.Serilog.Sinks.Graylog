using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.Transport.Http
{
    /// <summary>
    /// Tests for the URL, headers and failure handling of the HTTP transport.
    /// </summary>
    /// <remarks>
    /// The previous tests here posted to logs.aeroclub.int, which stopped existing years ago, and
    /// were <c>Skip</c>ped for that reason - one of them also asserted a <c>LoggingFailedException</c>
    /// that the client no longer raises. <see cref="HttpTransportClient.CreateHttpClient"/> and
    /// <see cref="HttpTransportClient.ConfigureHttpClient"/> are both <c>protected virtual</c>, so a
    /// subclass can supply a recording handler and the same behaviour can be asserted with no server.
    /// </remarks>
    public class HttpTransportClientFixture
    {
        [Theory]
        [InlineData("http://logs.example.org:12201")]
        [InlineData("http://logs.example.org:12201/")]
        public void ConfiguredClient_EndpointWithoutAPath_KeepsTheConfiguredPort(string endpoint)
        {
            using HttpClient client = Configure(OptionsFor(endpoint));

            Assert.NotNull(client.BaseAddress);
            Assert.Equal("http://logs.example.org:12201/", client.BaseAddress.ToString());
        }

        [Fact]
        public void ConfiguredClient_WithAnHttpsEndpoint_KeepsTheHttpsScheme()
        {
            using HttpClient client = Configure(OptionsFor("https://logs.example.org:12201"));

            Assert.NotNull(client.BaseAddress);
            Assert.Equal("https://logs.example.org:12201/", client.BaseAddress.ToString());
        }

        [Fact]
        public void ConfiguredClient_WithoutAnExplicitPort_UsesTheSchemeDefault()
        {
            using HttpClient client = Configure(OptionsFor("https://logs.example.org"));

            Assert.NotNull(client.BaseAddress);
            Assert.Equal("https://logs.example.org/", client.BaseAddress.ToString());
            Assert.Equal(443, client.BaseAddress.Port);
        }

        [Fact]
        public void ConfiguredClient_WithUsernameAndPassword_SendsBasicAuthentication()
        {
            using HttpClient client = Configure(OptionsFor("http://logs.example.org:12201", o =>
                o.BasicAuthentication = new HttpBasicAuthenticationOptions
                {
                    Username = "username",
                    Password = "password"
                }));

            AuthenticationHeaderValue? authorization = client.DefaultRequestHeaders.Authorization;

            Assert.NotNull(authorization);
            Assert.Equal("Basic", authorization.Scheme);
            Assert.NotNull(authorization.Parameter);
            Assert.Equal("username:password",
                Encoding.ASCII.GetString(Convert.FromBase64String(authorization.Parameter)));
        }

        [Theory]
        // Half a credential is not a credential; the generator reports it to SelfLog and gives up.
        [InlineData("username", null)]
        [InlineData(null, "password")]
        [InlineData(null, null)]
        public void ConfiguredClient_WithAnIncompleteCredential_SendsNoAuthorizationHeader(string? username, string? password)
        {
            using HttpClient client = Configure(OptionsFor("http://logs.example.org:12201", o =>
                o.BasicAuthentication = new HttpBasicAuthenticationOptions
                {
                    Username = username,
                    Password = password
                }));

            Assert.Null(client.DefaultRequestHeaders.Authorization);
        }

        [Fact]
        public void ConfiguredClient_AsksForNoCachingAndNoExpectContinue()
        {
            using HttpClient client = Configure(OptionsFor("http://logs.example.org:12201"));

            Assert.False(client.DefaultRequestHeaders.ExpectContinue);
            Assert.NotNull(client.DefaultRequestHeaders.CacheControl);
            Assert.True(client.DefaultRequestHeaders.CacheControl.NoCache);
        }

        [Fact]
        public void ConfiguredClient_WithoutAnEndpoint_Throws()
        {
            var exception = Assert.Throws<InvalidOperationException>(() => Configure(new HttpTransportOptions()));

            Assert.Equal("The HTTP endpoint must be an absolute URI.", exception.Message);
        }

        [Fact]
        public void ConfiguredClient_WithARelativeEndpoint_Throws()
        {
            var options = new HttpTransportOptions { Endpoint = new Uri("logs.example.org", UriKind.Relative) };

            var exception = Assert.Throws<InvalidOperationException>(() => Configure(options));

            Assert.Equal("The HTTP endpoint must be an absolute URI.", exception.Message);
        }

        [Fact]
        public async Task Send_PostsTheMessageAsJsonToTheGelfPath()
        {
            const string message = "{\"short_message\":\"hello\"}";

            using var target = new ProbeHttpTransportClient(OptionsFor("http://logs.example.org:12201"));

            await target.Send(message);

            HttpRequestMessage? request = target.Handler.Request;

            Assert.NotNull(request);
            Assert.NotNull(request.RequestUri);
            Assert.NotNull(request.Content);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://logs.example.org:12201/gelf", request.RequestUri.ToString());

            MediaTypeHeaderValue? contentType = request.Content.Headers.ContentType;

            Assert.NotNull(contentType);
            Assert.Equal("application/json", contentType.MediaType);
            Assert.Equal(Encoding.UTF8.WebName, contentType.CharSet);
            Assert.Equal(message, target.Handler.Body);
        }

        [Fact]
        public async Task Send_ConcurrentCalls_CreateOneSharedHttpClient()
        {
            using var target = new ProbeHttpTransportClient(OptionsFor("http://logs.example.org:12201"));

            await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => target.Send("{}")));

            Assert.Equal(1, target.CreateHttpClientCount);
        }

        [Fact]
        public async Task Send_WithCustomHeaders_AddsThemToTheRequestAndOverridesBasicAuthentication()
        {
            using var target = new ProbeHttpTransportClient(OptionsFor("http://logs.example.org:12201", o =>
            {
                o.BasicAuthentication = new HttpBasicAuthenticationOptions
                {
                    Username = "username",
                    Password = "password"
                };
                o.Headers = new Dictionary<string, string>
                {
                    ["X-Graylog-Tenant"] = "payments",
                    ["Authorization"] = "Bearer proxy-token"
                };
            }));

            await target.Send("{}");

            HttpRequestMessage? request = target.Handler.Request;

            Assert.NotNull(request);
            Assert.Equal("payments", Assert.Single(request.Headers.GetValues("X-Graylog-Tenant")));
            Assert.NotNull(request.Headers.Authorization);
            Assert.Equal("Bearer", request.Headers.Authorization.Scheme);
            Assert.Equal("proxy-token", request.Headers.Authorization.Parameter);
        }

        [Fact]
        public void ConfiguredClient_WithAContentTypeHeader_Throws()
        {
            HttpTransportOptions options = OptionsFor("http://logs.example.org:12201", o =>
                o.Headers = new Dictionary<string, string> { ["Content-Type"] = "text/plain" });

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Configure(options));

            Assert.Equal("The HTTP transport always sends application/json content.", exception.Message);
        }

        /// <summary>
        /// A path on <c>Endpoint</c> is retained so a reverse proxy can route GELF requests.
        /// </summary>
        [Fact]
        public async Task Send_WhenTheEndpointCarriesAPath_PostsBelowThatPath()
        {
            using var target = new ProbeHttpTransportClient(OptionsFor("http://logs.example.org:12201/testgelf"));

            await target.Send("{}");

            HttpRequestMessage? request = target.Handler.Request;

            Assert.NotNull(request);
            Assert.NotNull(request.RequestUri);
            Assert.Equal("http://logs.example.org:12201/testgelf/gelf", request.RequestUri.ToString());
        }

        /// <summary>
        /// A 413 means the GELF input's max_chunk_size was exceeded, and GELF has no chunking over HTTP,
        /// so the bare status code leaves the reader with nothing to act on.
        /// </summary>
        [Fact]
        public async Task Send_WhenTheMessageIsTooLarge_SaysWhichSettingCapsIt()
        {
            using var target = new ProbeHttpTransportClient(OptionsFor("http://logs.example.org:12201"),
                                                            HttpStatusCode.RequestEntityTooLarge);

            HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() => target.Send("{}"));

            Assert.Contains("max_chunk_size", exception.Message);
            Assert.Contains("2-byte", exception.Message);
        }

        [Fact]
        public async Task Send_WhenTheServerRejectsTheMessage_Throws()
        {
            // Failures must surface: the batching sink retries on them and Emit reports them to SelfLog.
            using var target = new ProbeHttpTransportClient(OptionsFor("http://logs.example.org:12201"),
                                                            HttpStatusCode.InternalServerError);

            await Assert.ThrowsAsync<HttpRequestException>(() => target.Send("{}"));
        }

        private static HttpTransportOptions OptionsFor(string endpoint, Action<HttpTransportOptions>? configure = null)
        {
            var options = new HttpTransportOptions
            {
                Endpoint = new Uri(endpoint)
            };

            configure?.Invoke(options);

            return options;
        }

        /// <summary>
        /// <c>ConfigureHttpClient</c> only runs on the first <c>Send</c>, so a test that wants to look
        /// at the configured client without sending has to invoke it itself.
        /// </summary>
        private static HttpClient Configure(HttpTransportOptions options)
        {
            using var probe = new ProbeHttpTransportClient(options);

            return probe.Configure();
        }

        private sealed class ProbeHttpTransportClient : HttpTransportClient
        {
            private int _createHttpClientCount;

            public ProbeHttpTransportClient(HttpTransportOptions options,
                                            HttpStatusCode status = HttpStatusCode.Accepted)
                : base(options)
            {
                Handler = new RecordingHandler(status);
            }

            public RecordingHandler Handler { get; }

            public int CreateHttpClientCount => Volatile.Read(ref _createHttpClientCount);

            public HttpClient Configure()
            {
                // Not the recording handler: this client is inspected, never sent through.
                var client = new HttpClient();

                ConfigureHttpClient(client);

                return client;
            }

            protected override HttpClient CreateHttpClient()
            {
                Interlocked.Increment(ref _createHttpClientCount);
                return new HttpClient(Handler);
            }
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;

            public RecordingHandler(HttpStatusCode status)
            {
                _status = status;
            }

            public HttpRequestMessage? Request { get; private set; }

            public string? Body { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                                        CancellationToken cancellationToken)
            {
                Request = request;

                if (request.Content != null)
                {
                    Body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                }

                return new HttpResponseMessage(_status);
            }
        }
    }
}
