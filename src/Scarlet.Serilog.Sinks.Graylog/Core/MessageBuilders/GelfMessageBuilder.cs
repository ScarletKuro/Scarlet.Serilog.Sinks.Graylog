using Serilog.Events;
using Serilog.Parsing;
using Scarlet.Serilog.Sinks.Graylog.Core.Extensions;
using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
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

        /// <summary>Gets the GELF payload options this builder was created with.</summary>
        protected GelfOptions Options { get; }

        private readonly string _hostName;

        /// <summary>
        /// Built on first use and rebuilt if <see cref="GelfOptions.JsonSerializerOptions"/> is
        /// swapped for a different instance, since the writer caches contracts per options instance.
        /// </summary>
        private ScalarJsonWriter? _scalarJsonWriter;

        /// <summary>
        /// Initializes a new instance of the <see cref="GelfMessageBuilder"/> class.
        /// </summary>
        /// <param name="hostName">Name of the host.</param>
        /// <param name="options">The options.</param>
        public GelfMessageBuilder(string hostName, GelfOptions options)
        {
            _hostName = hostName;
            Options = options;
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

            // Collected once. Re-running the token query inside the loop made this quadratic in
            // properties times template tokens, and allocated two enumerators and a closure per property.
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

                AddAdditionalField(jsonObject, property);
            }

            if (Options.IncludeMessageTemplate)
            {
                string messageTemplate = logEvent.MessageTemplate.Text;

                AddGelfField(jsonObject, Options.MessageTemplateFieldName, messageTemplate);
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

        /// <summary>
        /// Writes one GELF additional field, applying the naming rules the format requires.
        /// </summary>
        /// <param name="target">The GELF message being built.</param>
        /// <param name="name">The field name, without the leading underscore.</param>
        /// <param name="value">The value; <c>null</c> is written as a JSON null.</param>
        /// <remarks>
        /// Graylog only promotes underscore-prefixed fields to additional fields, reserves <c>_id</c>,
        /// and accepts only <c>^[\w\.\-]*$</c> in a field name - anything else is replaced with an
        /// underscore. Two names that collide after that are written last-wins rather than throwing,
        /// because <c>JsonObject.Add</c> would take the whole event down with an
        /// <see cref="ArgumentException"/>.
        /// </remarks>
        protected static void AddGelfField(JsonObject target, string name, JsonNode? value)
        {
            target[ToGelfFieldName(name)] = value;
        }

        private static string ToGelfFieldName(string name)
        {
            // GELF reserves _id, so a property called 'id' has to move out of the way.
            if (name.Equals("id", StringComparison.OrdinalIgnoreCase))
            {
                name = "id_";
            }

            string sanitized = SanitizeFieldName(name);

            return sanitized.StartsWith("_", StringComparison.Ordinal)
                ? sanitized
                : $"_{sanitized}";
        }

        /// <summary>
        /// Replaces every character GELF does not allow in a field name with an underscore.
        /// </summary>
        /// <remarks>
        /// Written as a scan rather than a <c>Regex</c> so the sink keeps no regular-expression
        /// dependency on the Native AOT path, and so the overwhelmingly common case - a name that is
        /// already valid - allocates nothing.
        /// </remarks>
        private static string SanitizeFieldName(string name)
        {
            int firstInvalid = -1;

            for (int i = 0; i < name.Length; i++)
            {
                if (!IsAllowedInFieldName(name[i]))
                {
                    firstInvalid = i;
                    break;
                }
            }

            if (firstInvalid < 0)
            {
                return name;
            }

            char[] characters = name.ToCharArray();

            for (int i = firstInvalid; i < characters.Length; i++)
            {
                if (!IsAllowedInFieldName(characters[i]))
                {
                    characters[i] = '_';
                }
            }

            return new string(characters);
        }

        // Graylog verifies field names with ^[\w\.\-]*$, and its \w is ASCII-only.
        private static bool IsAllowedInFieldName(char character)
        {
            return (character >= 'a' && character <= 'z')
                || (character >= 'A' && character <= 'Z')
                || (character >= '0' && character <= '9')
                || character == '_'
                || character == '.'
                || character == '-';
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
                    AddGelfField(jObject, key, scalarValue.Value == null
                        ? null
                        : ScalarJsonWriter.ToJsonNode(scalarValue.Value));

                    break;
                case SequenceValue sequenceValue:
                    AddGelfField(jObject, key, RenderPropertyValue(sequenceValue));

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

                        AddGelfField(jObject, key, nested);
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
