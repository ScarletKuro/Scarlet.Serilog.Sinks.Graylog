using Scarlet.Serilog.Sinks.Graylog.Core.Transport;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Tcp;
using Scarlet.Serilog.Sinks.Graylog.Core.Transport.Udp;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using SinkTransportType = Scarlet.Serilog.Sinks.Graylog.Core.Transport.TransportType;

namespace Scarlet.Serilog.Sinks.Graylog.Compatibility.Tests
{
    /// <summary>
    /// Small runtime smoke tests for target frameworks the main MTP suite does not execute.
    /// </summary>
    public class CompatibilitySmokeFixture
    {
        [Fact]
        public void Sink_EmitsGelfPayloadThroughCustomTransport()
        {
            var transport = new RecordingTransport();
            var options = new GraylogSinkOptions
            {
                TransportType = SinkTransportType.Custom,
                Message =
                {
                    HostnameOverride = "compat-host",
                    Facility = "compat",
                    IncludeMessageTemplate = true
                },
                Custom = { Factory = () => transport }
            };

            using (var logger = new LoggerConfiguration()
                       .WriteTo.Graylog(options)
                       .CreateLogger())
            {
                logger.Information("Hello {UserId} from {Runtime}", 42, TargetName);
            }

            string payload = Assert.Single(transport.Payloads);

            Assert.Contains("\"version\":\"1.1\"", payload);
            Assert.Contains("\"host\":\"compat-host\"", payload);
            Assert.Contains("\"short_message\":\"Hello 42 from \\u0022" + TargetName + "\\u0022\"", payload);
            Assert.Contains("\"_facility\":\"compat\"", payload);
            Assert.Contains("\"_UserId\":42", payload);
            Assert.Contains("\"_Runtime\":\"" + TargetName + "\"", payload);
            Assert.Contains("\"_message_template\":\"Hello {UserId} from {Runtime}\"", payload);
            Assert.Equal(1, transport.DisposeCount);
        }

        [Fact]
        public void Sink_WritesExceptionFieldsThroughCustomTransport()
        {
            var transport = new RecordingTransport();
            var options = new GraylogSinkOptions
            {
                TransportType = SinkTransportType.Custom,
                Message = { HostnameOverride = "compat-host" },
                Custom = { Factory = () => transport }
            };

            using (var logger = new LoggerConfiguration()
                       .WriteTo.Graylog(options)
                       .CreateLogger())
            {
                logger.Error(CreateException(), "Failed on {Runtime}", TargetName);
            }

            string payload = Assert.Single(transport.Payloads);

            Assert.Contains("\"_ExceptionType\":\"System.InvalidOperationException\"", payload);
            Assert.Contains("\"_ExceptionMessage\":\"outer - inner\"", payload);
            Assert.Contains("\"_StackTrace\":", payload);
        }

        [Fact]
        public void LoggerConfigurationExtension_RejectsNullArguments()
        {
            Assert.Equal("loggerSinkConfiguration",
                Assert.Throws<ArgumentNullException>(() =>
                    LoggerConfigurationGrayLogExtensions.Graylog(null!, new GraylogSinkOptions())).ParamName);

            Assert.Equal("options",
                Assert.Throws<ArgumentNullException>(() =>
                    new LoggerConfiguration().WriteTo.Graylog(null!)).ParamName);
        }

        [Fact]
        public async Task UdpTransportClient_SendsPayloadToLoopback()
        {
            byte[] payload = Encoding.UTF8.GetBytes("xcompat-udp");
            byte[] expected = Encoding.UTF8.GetBytes("compat-udp");

            using (var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                var endpoint = (IPEndPoint)listener.Client.LocalEndPoint!;
                using var target = new UdpTransportClient(new UdpTransportOptions
                {
                    Host = IPAddress.Loopback.ToString(),
                    Port = endpoint.Port
                });

                Task<byte[]> receive = ReceiveUdp(listener);

                await target.Send(new ReadOnlyMemory<byte>(payload, 1, expected.Length));

                Assert.Equal(expected, await receive);
            }
        }

        [Fact]
        public async Task TcpTransportClient_SendsPayloadToLoopback()
        {
            byte[] payload = Encoding.UTF8.GetBytes("xcompat-tcp");
            byte[] expected = Encoding.UTF8.GetBytes("compat-tcp");

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                using (var target = new TcpTransportClient(new TcpTransportOptions
                       {
                           Host = IPAddress.Loopback.ToString(),
                           Port = ((IPEndPoint)listener.LocalEndpoint).Port
                       }))
                {
                    Task<byte[]> receive = ReceiveTcp(listener, expected.Length);

                    await target.Send(new ReadOnlyMemory<byte>(payload, 1, expected.Length));

                    Assert.Equal(expected, await receive);
                }
            }
            finally
            {
                listener.Stop();
            }
        }

#if NET462
        [Fact]
        public void HttpTransportClient_WithClientCertificate_UsesWinHttpHandler()
        {
            using var certificate = new X509Certificate2();
            using var target = new ProbeHttpTransportClient(new HttpTransportOptions
            {
                Endpoint = new Uri("https://127.0.0.1/gelf"),
                Tls = new TlsOptions { ClientCertificate = certificate }
            });

            using HttpMessageHandler handler = target.CreateHandler();

            Assert.Equal("System.Net.Http.WinHttpHandler", handler.GetType().FullName);
        }

#endif

        private static async Task<byte[]> ReceiveUdp(UdpClient listener)
        {
            UdpReceiveResult result = await WithTimeout(listener.ReceiveAsync());

            return result.Buffer;
        }

        private static async Task<byte[]> ReceiveTcp(TcpListener listener, int length)
        {
            using (TcpClient client = await WithTimeout(listener.AcceptTcpClientAsync()))
            using (NetworkStream stream = client.GetStream())
            {
                byte[] buffer = new byte[length];
                int offset = 0;
                while (offset < buffer.Length)
                {
                    int read = await WithTimeout(stream.ReadAsync(buffer, offset, buffer.Length - offset));
                    if (read == 0)
                    {
                        break;
                    }

                    offset += read;
                }

                Assert.Equal(length, offset);
                return buffer;
            }
        }

        private static async Task<T> WithTimeout<T>(Task<T> task)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));

            if (completed != task)
            {
                throw new TimeoutException("The loopback transport test timed out.");
            }

            return await task;
        }

        private static Exception CreateException()
        {
            try
            {
                try
                {
                    throw new InvalidOperationException("inner");
                }
                catch (Exception inner)
                {
                    throw new InvalidOperationException("outer", inner);
                }
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

#if NET462
        private const string TargetName = "net462";
#elif NET471
        private const string TargetName = "net471";
#else
        private const string TargetName = "netstandard2.0";
#endif

        private sealed class RecordingTransport : ITransport
        {
            private readonly List<string> _payloads = new List<string>();

            public IReadOnlyList<string> Payloads => _payloads;

            public int DisposeCount { get; private set; }

            public Task Send(ReadOnlyMemory<byte> message)
            {
                _payloads.Add(Encoding.UTF8.GetString(message.ToArray()));

                return Task.CompletedTask;
            }

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        private sealed class ProbeHttpTransportClient : Scarlet.Serilog.Sinks.Graylog.Core.Transport.Http.HttpTransportClient
        {
            public ProbeHttpTransportClient(HttpTransportOptions options)
                : base(options)
            {
            }

            public HttpMessageHandler CreateHandler()
            {
                return CreateHttpMessageHandler();
            }
        }
    }
}
