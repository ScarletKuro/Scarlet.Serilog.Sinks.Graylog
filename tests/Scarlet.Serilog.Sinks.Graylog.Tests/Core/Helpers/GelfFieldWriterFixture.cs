using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.Helpers
{
    public class GelfFieldWriterFixture
    {
        [Fact]
        public void Constructor_WithoutAWriter_Throws()
        {
            var scalars = new ScalarJsonWriter(new JsonSerializerOptions());

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new GelfFieldWriter(null!, scalars));

            Assert.Equal("writer", exception.ParamName);
        }

        [Fact]
        public void Constructor_WithoutAScalarWriter_Throws()
        {
            var buffer = new ByteBufferWriter();
            using var writer = new Utf8JsonWriter(buffer);

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new GelfFieldWriter(writer, null!));

            Assert.Equal("scalars", exception.ParamName);
        }

        [Fact]
        public void WriteField_WithNullName_Throws()
        {
            var buffer = new ByteBufferWriter();
            using var writer = new Utf8JsonWriter(buffer);
            var target = new GelfFieldWriter(writer, new ScalarJsonWriter(new JsonSerializerOptions()));

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => target.WriteField(null!, "value"));

            Assert.Equal("name", exception.ParamName);
        }

        [Fact]
        public void BeginField_WithNullName_Throws()
        {
            var buffer = new ByteBufferWriter();
            using var writer = new Utf8JsonWriter(buffer);
            var target = new GelfFieldWriter(writer, new ScalarJsonWriter(new JsonSerializerOptions()));

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => target.BeginField(null!));

            Assert.Equal("name", exception.ParamName);
        }

        [Fact]
        public void WriteField_WritesNullAndIgnoresALaterNormalizedDuplicate()
        {
            var buffer = new ByteBufferWriter();

            using (var writer = new Utf8JsonWriter(buffer))
            {
                var target = new GelfFieldWriter(writer, new ScalarJsonWriter(new JsonSerializerOptions()));

                writer.WriteStartObject();
                target.WriteField("a b", null);
                target.WriteField("a/b", "ignored");
                target.WriteField(string.Empty, "empty-name");
                writer.WriteEndObject();
            }

            using JsonDocument actual = JsonDocument.Parse(buffer.WrittenMemory);
            JsonElement root = actual.RootElement;

            Assert.Equal(JsonValueKind.Null, root.GetProperty("_a_b").ValueKind);
            Assert.Equal("empty-name", root.GetProperty("_").GetString());
            Assert.Equal(2, root.EnumerateObject().Count());
        }
    }
}
