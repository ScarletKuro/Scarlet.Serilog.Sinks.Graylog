using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Fakes
{
    /// <summary>
    /// Drives a real Graylog over its REST API: creates the GELF inputs the integration tests send to,
    /// and reads back the messages that arrived.
    /// </summary>
    /// <remarks>
    /// The server is the one in <c>tests/integration/docker-compose.yml</c>; <see cref="TryDiscover"/>
    /// returns <c>null</c> when it is not running, which is what lets the integration tests skip rather
    /// than fail on a machine without it. Credentials and ports are fixed by that compose file.
    /// </remarks>
    internal sealed class GraylogServer : IDisposable
    {
        public const string UdpInputType = "org.graylog2.inputs.gelf.udp.GELFUDPInput";
        public const string TcpInputType = "org.graylog2.inputs.gelf.tcp.GELFTCPInput";
        public const string HttpInputType = "org.graylog2.inputs.gelf.http.GELFHttpInput";

        private const string DefaultApiUri = "http://127.0.0.1:9000/";
        private const string DefaultUsername = "admin";
        private const string DefaultPassword = "admin";

        private readonly HttpClient _client;

        private GraylogServer(HttpClient client)
        {
            _client = client;
        }

        /// <summary>The host the transports send GELF to.</summary>
        public string Host { get; private set; } = "127.0.0.1";

        /// <summary>
        /// Connects to the Graylog named by <c>GRAYLOG_API_URI</c>, or the compose file's own address,
        /// and waits for it to report itself alive.
        /// </summary>
        /// <returns>The server, or <c>null</c> when nothing answered within <paramref name="timeout"/>.</returns>
        public static async Task<GraylogServer?> TryDiscover(TimeSpan timeout, CancellationToken cancellationToken)
        {
            string apiUri = Environment.GetEnvironmentVariable("GRAYLOG_API_URI") ?? DefaultApiUri;

            if (!Uri.TryCreate(apiUri, UriKind.Absolute, out Uri? baseAddress))
            {
                return null;
            }

            var client = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(30) };

            string credentials = Environment.GetEnvironmentVariable("GRAYLOG_USERNAME") ?? DefaultUsername;
            string password = Environment.GetEnvironmentVariable("GRAYLOG_PASSWORD") ?? DefaultPassword;

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials}:{password}")));
            // Graylog rejects a state-changing API call without this header, as CSRF protection.
            client.DefaultRequestHeaders.Add("X-Requested-By", "scarlet-serilog-sinks-graylog-tests");

            var server = new GraylogServer(client) { Host = baseAddress.Host };

            if (await server.WaitUntilAlive(timeout, cancellationToken).ConfigureAwait(false))
            {
                return server;
            }

            server.Dispose();

            return null;
        }

        private async Task<bool> WaitUntilAlive(TimeSpan timeout, CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using HttpResponseMessage response = await _client.GetAsync("api/system/lbstatus", cancellationToken)
                                                                      .ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                }
                catch (HttpRequestException)
                {
                    // Not up yet.
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Request timeout rather than the caller giving up.
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }

            return false;
        }

        /// <summary>
        /// Makes sure a global input of <paramref name="type"/> is listening on <paramref name="port"/>,
        /// creating it if this is the first run against a fresh server, and waits for it to start.
        /// </summary>
        /// <remarks>
        /// Graylog creates no inputs of its own, and an input that exists is not yet an input that has
        /// bound its port - a message sent in between is simply lost, which would show up as a flaky
        /// test rather than as the setup race it is.
        /// </remarks>
        public Task EnsureInput(string type, int port, CancellationToken cancellationToken)
        {
            return EnsureInput(type, port, configure: null, cancellationToken);
        }

        /// <summary>
        /// As above, with <paramref name="configure"/> adding to the input configuration - TLS, for the
        /// input the TLS test sends to.
        /// </summary>
        public async Task EnsureInput(string type, int port, Action<JsonObject>? configure, CancellationToken cancellationToken)
        {
            // Two runners against one fresh server both find nothing and both create an input on the
            // same port; only one binds it, and the loser's tests then skip on a server that is in
            // fact working. Whoever loses drops the input it created and adopts the one that started.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                string? existing = await FindInput(type, port, cancellationToken).ConfigureAwait(false);

                if (existing != null)
                {
                    if (await TryWaitUntilInputIsRunning(existing, TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false))
                    {
                        return;
                    }

                    // Somebody else's input holds the port and is not starting. Nothing to adopt and
                    // nothing to clean up, so this is fatal.
                    throw new TimeoutException($"The Graylog {type} input on port {port} did not start.");
                }

                string created = await CreateInput(type, port, configure, cancellationToken).ConfigureAwait(false);

                if (await TryWaitUntilInputIsRunning(created, TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                // Lost the race: the port belongs to a competing input. Remove the duplicate so the
                // server is not left with two inputs fighting over one port, then look again.
                await DeleteInput(created, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException($"No Graylog {type} input could be started on port {port}.");
        }

        private async Task DeleteInput(string inputId, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await _client.DeleteAsync($"api/system/inputs/{inputId}", cancellationToken)
                                                              .ConfigureAwait(false);

            // Best effort: the input is already useless, and failing the run over the cleanup of a
            // duplicate would replace a recoverable race with an unrecoverable one.
            _ = response;
        }

        private async Task<string?> FindInput(string type, int port, CancellationToken cancellationToken)
        {
            JsonObject inputs = await GetJson("api/system/inputs", cancellationToken).ConfigureAwait(false);

            foreach (JsonNode? input in inputs["inputs"]?.AsArray() ?? new JsonArray())
            {
                if (input is not JsonObject candidate)
                {
                    continue;
                }

                if (candidate["type"]?.GetValue<string>() == type &&
                    candidate["attributes"]?["port"]?.GetValue<int>() == port)
                {
                    return candidate["id"]?.GetValue<string>();
                }
            }

            return null;
        }

        private async Task<string> CreateInput(string type, int port, Action<JsonObject>? configure, CancellationToken cancellationToken)
        {
            var configuration = new JsonObject
            {
                ["bind_address"] = "0.0.0.0",
                ["port"] = port,
                ["recv_buffer_size"] = 262144,
                ["number_worker_threads"] = 2,
                ["decompress_size_limit"] = 8388608,
                ["charset_name"] = "UTF-8"
            };

            if (type == TcpInputType)
            {
                // The sink frames TCP messages with a trailing null byte, which is what this input
                // setting expects; without it Graylog waits for a newline that never comes.
                configuration["use_null_delimiter"] = true;
                configuration["max_message_size"] = 2097152;
                configuration["tls_enable"] = false;
                configuration["tcp_keepalive"] = false;
            }

            if (type == HttpInputType)
            {
                configuration["max_chunk_size"] = 65536;
                configuration["idle_writer_timeout"] = 60;
                configuration["enable_cors"] = true;
                configuration["tls_enable"] = false;
            }
            // Last, so a caller can turn on what the defaults above turned off - TLS, in particular.
            configure?.Invoke(configuration);

            var request = new JsonObject
            {
                ["title"] = $"integration-{port}",
                ["type"] = type,
                ["global"] = true,
                ["configuration"] = configuration
            };

            JsonObject created = await PostJson("api/system/inputs", request, cancellationToken).ConfigureAwait(false);

            return created["id"]?.GetValue<string>()
                   ?? throw new InvalidOperationException($"Graylog created an input of type {type} without reporting its id.");
        }

        /// <summary>
        /// Waits for an input to reach RUNNING, reporting whether it got there rather than throwing:
        /// an input that never starts is how a lost create race looks, and that is recoverable.
        /// </summary>
        private async Task<bool> TryWaitUntilInputIsRunning(string inputId, TimeSpan timeout, CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                JsonObject states = await GetJson("api/system/inputstates", cancellationToken).ConfigureAwait(false);

                foreach (JsonNode? state in states["states"]?.AsArray() ?? new JsonArray())
                {
                    if (state is JsonObject candidate &&
                        candidate["id"]?.GetValue<string>() == inputId &&
                        candidate["state"]?.GetValue<string>() == "RUNNING")
                    {
                        return true;
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }

            return false;
        }

        /// <summary>
        /// Polls Graylog until a message matching <paramref name="query"/> has been indexed.
        /// </summary>
        /// <param name="query">A Graylog search query, e.g. <c>correlation:abc123</c>.</param>
        /// <param name="timeout">How long to keep looking before giving up.</param>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>The stored message's fields.</returns>
        /// <remarks>
        /// Delivery is asynchronous end to end - the sink does not wait for the send, and Graylog
        /// buffers before it indexes - so the only sound assertion is that the message turns up
        /// eventually. The compose file shrinks the output buffer to keep "eventually" short.
        /// </remarks>
        public async Task<JsonObject> WaitForMessage(string query, TimeSpan timeout, CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                JsonObject? message = await Search(query, cancellationToken).ConfigureAwait(false);

                if (message != null)
                {
                    return message;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException($"No Graylog message matched '{query}' within {timeout}.");
        }

        /// <summary>
        /// Runs one search through the views API, which is the search surface Graylog 5 and 6 share -
        /// the legacy <c>/api/search/universal</c> endpoints are deprecated.
        /// </summary>
        private async Task<JsonObject?> Search(string query, CancellationToken cancellationToken)
        {
            var request = new JsonObject
            {
                ["queries"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "q",
                        ["query"] = new JsonObject
                        {
                            ["type"] = "elasticsearch",
                            ["query_string"] = query
                        },
                        ["timerange"] = new JsonObject
                        {
                            ["type"] = "relative",
                            ["range"] = 300
                        },
                        ["search_types"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["id"] = "messages",
                                ["type"] = "messages",
                                ["limit"] = 10
                            }
                        }
                    }
                }
            };

            JsonObject created = await PostJson("api/views/search", request, cancellationToken).ConfigureAwait(false);
            string searchId = created["id"]?.GetValue<string>()
                              ?? throw new InvalidOperationException("Graylog accepted a search without reporting its id.");

            JsonObject job = await PostJson($"api/views/search/{searchId}/execute", new JsonObject(), cancellationToken)
                .ConfigureAwait(false);

            JsonObject executed = await AwaitSearchJob(job, cancellationToken).ConfigureAwait(false);

            JsonNode? messages = executed["results"]?["q"]?["search_types"]?["messages"]?["messages"];

            if (messages is not JsonArray results || results.Count == 0)
            {
                return null;
            }

            return results[0]?["message"]?.AsObject();
        }

        /// <summary>
        /// Waits for a search job to finish and returns its final state.
        /// </summary>
        /// <remarks>
        /// Executing a search starts a job rather than returning results: the response carries
        /// <c>execution.done</c> and, until that is true, an empty <c>results</c>. Reading it straight
        /// out of the execute response finds nothing every time, no matter what is in the index.
        /// </remarks>
        private async Task<JsonObject> AwaitSearchJob(JsonObject job, CancellationToken cancellationToken)
        {
            string jobId = job["id"]?.GetValue<string>()
                           ?? throw new InvalidOperationException("Graylog started a search job without reporting its id.");

            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);

            while (job["execution"]?["done"]?.GetValue<bool>() != true)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException($"The Graylog search job {jobId} did not finish.");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);

                job = await GetJson($"api/views/search/status/{jobId}", cancellationToken).ConfigureAwait(false);
            }

            return job;
        }

        private async Task<JsonObject> GetJson(string path, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await _client.GetAsync(path, cancellationToken).ConfigureAwait(false);

            return await ReadJson(response, path, cancellationToken).ConfigureAwait(false);
        }

        private async Task<JsonObject> PostJson(string path, JsonObject body, CancellationToken cancellationToken)
        {
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _client.PostAsync(path, content, cancellationToken).ConfigureAwait(false);

            return await ReadJson(response, path, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads a JSON body, reporting a failed call with the body Graylog sent - which is where it
        /// says which configuration field it did not like.
        /// </summary>
        private static async Task<JsonObject> ReadJson(HttpResponseMessage response, string path, CancellationToken cancellationToken)
        {
#if NET
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    string.Format(CultureInfo.InvariantCulture, "Graylog answered {0} for {1}: {2}", (int)response.StatusCode, path, body));
            }

            return JsonNode.Parse(body) as JsonObject
                   ?? throw new InvalidOperationException($"Graylog answered {path} with something other than a JSON object: {body}");
        }

        /// <summary>
        /// A value that identifies one test's message among everything else in the index.
        /// </summary>
        public static string NewCorrelationId()
        {
            return "scarlet" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
