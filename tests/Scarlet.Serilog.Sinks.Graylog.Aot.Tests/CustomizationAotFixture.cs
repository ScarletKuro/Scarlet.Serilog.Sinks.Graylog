using Serilog.Events;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Aot.Tests
{
    /// <summary>
    /// Establishes what the documented customization hooks actually do under Native AOT, where there is
    /// no reflection-based contract resolver to fall back on.
    /// </summary>
    public class CustomizationAotFixture
    {
        /// <summary>
        /// A source-generated context can resolve a contract without reflection, so it applies.
        /// </summary>
        [Fact]
        public void SourceGeneratedContext_IsApplied()
        {
            var options = new JsonSerializerOptions { TypeInfoResolver = HarnessJsonContext.Default };

            Assert.Equal("\"Warning\"", SinkHarness.FieldJson(LogEventLevel.Warning, options));
        }

        /// <summary>
        /// A converter alone cannot: applying it needs a contract, and building one without a resolver
        /// requires reflection. Documented as needing a resolver too.
        /// </summary>
        [Fact]
        public void Converter_WithoutResolver_IsNotApplied()
        {
            var options = new JsonSerializerOptions();

            // The generic form: the non-generic JsonStringEnumConverter is itself RequiresDynamicCode.
            options.Converters.Add(new JsonStringEnumConverter<LogEventLevel>());

            Assert.Equal("3", SinkHarness.FieldJson(LogEventLevel.Warning, options));
        }
    }
}
