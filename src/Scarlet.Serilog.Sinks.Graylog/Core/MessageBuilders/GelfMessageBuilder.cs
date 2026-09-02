using Serilog.Events;
using Serilog.Parsing;
using Scarlet.Serilog.Sinks.Graylog.Core.Extensions;
using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Scarlet.Serilog.Sinks.Graylog.Core.MessageBuilders
{
    /// <summary>
    /// Message builder
    /// </summary>
    /// <seealso cref="IMessageBuilder" />
    public class GelfMessageBuilder : IMessageBuilder
    {
        private const string DefaultGelfVersion = "1.1";

        protected GraylogSinkOptionsBase Options => _options;

        private readonly string _hostName;
        private readonly GraylogSinkOptionsBase _options;

        /// <summary>
        /// Built on first use and rebuilt if <see cref="GraylogSinkOptionsBase.JsonSerializerOptions"/> is
        /// swapped for a different instance, since the writer caches contracts per options instance.
        /// </summary>
        private ScalarJsonWriter? _scalarJsonWriter;

        /// <summary>
        /// Initializes a new instance of the <see cref="GelfMessageBuilder"/> class.
        /// </summary>
        /// <param name="hostName">Name of the host.</param>
        /// <param name="options">The options.</param>
        public GelfMessageBuilder(string hostName, GraylogSinkOptionsBase options)
        {
            _hostName = hostName;
            _options = options;
        }

        /// <summary>
        /// Builds the specified log event.
        /// </summary>
        /// <param name="logEvent">The log event.</param>
        /// <returns></returns>
        public virtual JsonObject Build(LogEvent logEvent)
        {
            string message = logEvent.RenderMessage();
            string shortMessage = message.Truncate(Options.ShortMessageMaxLength);

            var jsonObject = new JsonObject
            {
                ["version"] = DefaultGelfVersion,
                ["host"] = Options.HostnameOverride ?? _hostName,
                ["short_message"] = shortMessage,
                ["timestamp"] = logEvent.Timestamp.ConvertToNix(),
                ["level"] = LogLevelMapper.GetMappedLevel(logEvent.Level),
                ["_stringLevel"] = logEvent.Level.ToString(),
                ["_facility"] = Options.Facility
            };

            if (message.Length > Options.ShortMessageMaxLength)
            {
                jsonObject.Add("full_message", message);
            }

            foreach (KeyValuePair<string, LogEventPropertyValue> property in logEvent.Properties)
            {
                if (Options.ExcludeMessageTemplateProperties)
                {
                    var propertyTokens = logEvent.MessageTemplate.Tokens.OfType<PropertyToken>();

                    if (propertyTokens.Any(x => x.PropertyName == property.Key))
                    {
                        continue;
                    }
                }

                AddAdditionalField(jsonObject, property);
            }

            if (Options.IncludeMessageTemplate)
            {
                string messageTemplate = logEvent.MessageTemplate.Text;

                jsonObject.Add($"_{Options.MessageTemplateFieldName}", messageTemplate);
            }

            return jsonObject;
        }

        private ScalarJsonWriter ScalarJsonWriter
        {
            get
            {
                JsonSerializerOptions options = Options.JsonSerializerOptions;
                ScalarJsonWriter? writer = _scalarJsonWriter;

                if (writer == null || !ReferenceEquals(writer.Options, options))
                {
                    writer = new ScalarJsonWriter(options);
                    _scalarJsonWriter = writer;
                }

                return writer;
            }
        }

        private void AddAdditionalField(JsonObject jObject,
                                        KeyValuePair<string, LogEventPropertyValue> property,
                                        string memberPath = "")
        {
            string key = string.IsNullOrEmpty(memberPath)
                ? property.Key
                : $"{memberPath}.{property.Key}";

            switch (property.Value)
            {
                case ScalarValue scalarValue:
                    if (key.Equals("id", StringComparison.OrdinalIgnoreCase))
                    {
                        key = "id_";
                    }

                    if (!key.StartsWith("_", StringComparison.OrdinalIgnoreCase))
                    {
                        key = $"_{key}";
                    }

                    if (scalarValue.Value == null)
                    {
                        jObject.Add(key, null);

                        break;
                    }

                    var node = ScalarJsonWriter.ToJsonNode(scalarValue.Value);

                    jObject.Add(key, node);

                    break;
                case SequenceValue sequenceValue:
                    var sequenceValueString = RenderPropertyValue(sequenceValue);

                    jObject.Add(key, sequenceValueString);

                    if (Options.ParseArrayValues)
                    {
                        int counter = 0;

                        foreach (var sequenceElement in sequenceValue.Elements)
                        {
                            AddAdditionalField(jObject, new KeyValuePair<string, LogEventPropertyValue>(counter.ToString(), sequenceElement), key);

                            counter++;
                        }
                    }

                    break;
                case StructureValue structureValue:
                    foreach (LogEventProperty logEventProperty in structureValue.Properties)
                    {
                        AddAdditionalField(jObject,
                                           new KeyValuePair<string, LogEventPropertyValue>(logEventProperty.Name, logEventProperty.Value),
                                           key);
                    }

                    break;
                case DictionaryValue dictionaryValue:
                    if (Options.ParseArrayValues)
                    {
                        foreach (KeyValuePair<ScalarValue, LogEventPropertyValue> dictionaryValueElement in dictionaryValue.Elements)
                        {
                            var renderedKey = RenderPropertyValue(dictionaryValueElement.Key);

                            AddAdditionalField(jObject, new KeyValuePair<string, LogEventPropertyValue>(renderedKey, dictionaryValueElement.Value), key);
                        }
                    } else
                    {
                        // Built directly rather than serialized from a Dictionary<object, string>: the object
                        // key made that an untyped serialization call, which Native AOT cannot support.
                        // Rendering the keys is also what makes the result a well-formed JSON object, since
                        // Serilog permits primitives, the built-in scalars and enums as dictionary keys.
                        var nested = new JsonObject();

                        foreach (KeyValuePair<ScalarValue, LogEventPropertyValue> dictionaryValueElement in dictionaryValue.Elements)
                        {
                            nested[RenderPropertyValue(dictionaryValueElement.Key)] = RenderPropertyValue(dictionaryValueElement.Value);
                        }

                        jObject.Add(key, nested);
                    }

                    break;
            }
        }

        private static string RenderPropertyValue(LogEventPropertyValue propertyValue)
        {
            using TextWriter tw = new StringWriter();

            propertyValue.Render(tw);

            string result = tw.ToString()!;
            result = result.Trim('"');

            return result;
        }
    }
}
