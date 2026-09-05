using Serilog.Events;
using Serilog.Parsing;
using Scarlet.Serilog.Sinks.Graylog.Core.Extensions;
using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders
{
    /// <summary>Writes a log event as a GELF JSON object.</summary>
    public class GelfMessageBuilder : IMessageBuilder
    {
        private const string DefaultGelfVersion = "1.1";
        private const string StringLevel = "_stringLevel";
        private const string Facility = "_facility";

        private readonly string _hostName;

        /// <summary>
        /// Writes scalar field values, built once from the serializer options this builder was created
        /// with.
        /// </summary>
        /// <remarks>
        /// A sink supplies the serializer-options snapshot it captured at construction, so its writer
        /// configuration and both of its lazy message builders always agree. A builder constructed
        /// directly takes its own snapshot here instead.
        /// </remarks>
        private readonly ScalarJsonWriter _scalarJsonWriter;

        /// <summary>Gets the GELF payload options this builder was created with.</summary>
        protected GelfOptions Options { get; }

        /// <summary>Initializes a GELF message builder.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
        public GelfMessageBuilder(string hostName, GelfOptions options)
            : this(hostName, options, GetSerializerOptions(options))
        {
        }

        internal GelfMessageBuilder(
            string hostName,
            GelfOptions options,
            JsonSerializerOptions serializerOptions)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (serializerOptions == null)
            {
                throw new ArgumentNullException(nameof(serializerOptions));
            }

            _hostName = hostName;
            Options = options;
            _scalarJsonWriter = new ScalarJsonWriter(serializerOptions);
        }

        private static JsonSerializerOptions GetSerializerOptions(GelfOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return options.JsonSerializerOptions;
        }

        /// <inheritdoc />
        public virtual void Build(LogEvent logEvent, Utf8JsonWriter writer)
        {
            if (logEvent == null)
            {
                throw new ArgumentNullException(nameof(logEvent));
            }

            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            var fields = new GelfFieldWriter(writer, _scalarJsonWriter);

            writer.WriteStartObject();
            WriteCoreFields(logEvent, fields);
            WriteExtraFields(logEvent, fields);
            WriteProperties(logEvent, fields);
            writer.WriteEndObject();
        }

        /// <summary>Adds fields supplied by a specialized builder.</summary>
        protected virtual void WriteExtraFields(LogEvent logEvent, GelfFieldWriter fields)
        {
        }

        private void WriteCoreFields(LogEvent logEvent, GelfFieldWriter fields)
        {
            Utf8JsonWriter writer = fields.Writer;
            string rendered = logEvent.RenderMessage();
            int shortLength = Math.Min(rendered.Length, Options.ShortMessageMaxLength);

            writer.WriteString("version", DefaultGelfVersion);
            writer.WriteString("host", Options.HostnameOverride ?? _hostName);
            writer.WriteString("short_message", rendered.AsSpan(0, shortLength));
            writer.WriteNumber("timestamp", logEvent.Timestamp.ConvertToNix());
            writer.WriteNumber("level", LogLevelMapper.GetMappedLevel(logEvent.Level));

            if (fields.BeginField(StringLevel))
            {
                writer.WriteStringValue(LogLevelMapper.GetLevelName(logEvent.Level));
            }

            if (Options.Facility != null && fields.BeginField(Facility))
            {
                writer.WriteStringValue(Options.Facility);
            }

            if (rendered.Length > Options.ShortMessageMaxLength)
            {
                writer.WriteString("full_message", rendered);
            }
        }

        private void WriteProperties(LogEvent logEvent, GelfFieldWriter fields)
        {
            HashSet<string>? templateProperties = null;

            if (Options.ExcludeMessageTemplateProperties)
            {
                templateProperties = new HashSet<string>(StringComparer.Ordinal);

                foreach (MessageTemplateToken token in logEvent.MessageTemplate.Tokens)
                {
                    if (token is PropertyToken propertyToken)
                    {
                        templateProperties.Add(propertyToken.PropertyName);
                    }
                }
            }

            foreach (KeyValuePair<string, LogEventPropertyValue> property in logEvent.Properties)
            {
                if (templateProperties?.Contains(property.Key) == true)
                {
                    continue;
                }

                WriteAdditionalField(fields, property.Key, property.Value);
            }

            if (Options.IncludeMessageTemplate)
            {
                fields.WriteField(Options.MessageTemplateFieldName, logEvent.MessageTemplate.Text);
            }
        }

        private void WriteAdditionalField(
            GelfFieldWriter fields,
            string name,
            LogEventPropertyValue value,
            string memberPath = "")
        {
            string key = memberPath.Length == 0 ? name : $"{memberPath}.{name}";

            switch (value)
            {
                case ScalarValue scalarValue:
                    if (fields.BeginField(key))
                    {
                        if (scalarValue.Value == null)
                        {
                            fields.Writer.WriteNullValue();
                        }
                        else
                        {
                            fields.Scalars.WriteValue(fields.Writer, scalarValue.Value);
                        }
                    }

                    break;
                case SequenceValue sequenceValue:
                    fields.WriteField(key, RenderPropertyValue(sequenceValue));

                    if (Options.ParseArrayValues)
                    {
                        int index = 0;

                        foreach (LogEventPropertyValue element in sequenceValue.Elements)
                        {
                            WriteAdditionalField(fields, index.ToString(CultureInfo.InvariantCulture), element, key);
                            index++;
                        }
                    }

                    break;
                case StructureValue structureValue:
                    foreach (LogEventProperty property in structureValue.Properties)
                    {
                        WriteAdditionalField(fields, property.Name, property.Value, key);
                    }

                    break;
                case DictionaryValue dictionaryValue:
                    WriteDictionary(fields, key, dictionaryValue);

                    break;
            }
        }

        private void WriteDictionary(GelfFieldWriter fields, string key, DictionaryValue dictionaryValue)
        {
            if (Options.ParseArrayValues)
            {
                foreach (KeyValuePair<ScalarValue, LogEventPropertyValue> element in dictionaryValue.Elements)
                {
                    WriteAdditionalField(fields, RenderPropertyValue(element.Key), element.Value, key);
                }

                return;
            }

            if (!fields.BeginField(key))
            {
                return;
            }

            fields.Writer.WriteStartObject();

            foreach (KeyValuePair<ScalarValue, LogEventPropertyValue> element in dictionaryValue.Elements)
            {
                fields.Writer.WriteString(RenderPropertyValue(element.Key), RenderPropertyValue(element.Value));
            }

            fields.Writer.WriteEndObject();
        }

        private static string RenderPropertyValue(LogEventPropertyValue value)
        {
            using TextWriter writer = new StringWriter();

            value.Render(writer);

            return writer.ToString()!.Trim('"');
        }
    }
}
