using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Helpers
{
    /// <summary>
    /// Writes GELF additional fields, applying the required prefix, reserved-name and character rules.
    /// </summary>
    public sealed class GelfFieldWriter
    {
        private readonly HashSet<string> _written = new HashSet<string>(StringComparer.Ordinal);

        internal GelfFieldWriter(Utf8JsonWriter writer, ScalarJsonWriter scalars)
        {
            Writer = writer ?? throw new ArgumentNullException(nameof(writer));
            Scalars = scalars ?? throw new ArgumentNullException(nameof(scalars));
        }

        /// <summary>The writer for the complete GELF object.</summary>
        public Utf8JsonWriter Writer { get; }

        internal ScalarJsonWriter Scalars { get; }

        /// <summary>Writes a text-valued additional field, or a JSON null.</summary>
        public void WriteField(string name, string? value)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (!BeginField(name))
            {
                return;
            }

            if (value == null)
            {
                Writer.WriteNullValue();
            }
            else
            {
                Writer.WriteStringValue(value);
            }
        }

        /// <summary>
        /// Writes the normalized property name and reports whether the caller should write its value.
        /// </summary>
        public bool BeginField(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            return BeginNormalizedField(ToGelfFieldName(name));
        }

        private bool BeginNormalizedField(string fieldName)
        {
            if (!_written.Add(fieldName))
            {
                return false;
            }

            Writer.WritePropertyName(fieldName);

            return true;
        }

        private static string ToGelfFieldName(string name)
        {
            bool reserved = IsReserved(name.AsSpan());
            bool prefixed = name.Length > 0 && (name[0] == '_' || !IsAllowedInFieldName(name[0]));
            int prefixLength = prefixed ? 0 : 1;
            int suffixLength = reserved ? 1 : 0;

            int length = prefixLength + name.Length + suffixLength;
#if NET
            return string.Create(
                length,
                (Name: name, Prefixed: prefixed, Reserved: reserved),
                static (characters, state) =>
                {
                    int offset = 0;

                    if (!state.Prefixed)
                    {
                        characters[offset++] = '_';
                    }

                    foreach (char character in state.Name)
                    {
                        characters[offset++] = IsAllowedInFieldName(character) ? character : '_';
                    }

                    if (state.Reserved)
                    {
                        characters[offset] = '_';
                    }
                });
#else
            var characters = new char[length];
            int offset = 0;

            if (!prefixed)
            {
                characters[offset++] = '_';
            }

            foreach (char character in name)
            {
                characters[offset++] = IsAllowedInFieldName(character) ? character : '_';
            }

            if (reserved)
            {
                characters[offset] = '_';
            }

            return new string(characters);
#endif
        }

        private static bool IsReserved(ReadOnlySpan<char> name)
        {
            if (name.Equals("id".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return name.SequenceEqual("message".AsSpan())
                || name.SequenceEqual("source".AsSpan())
                || name.SequenceEqual("timestamp".AsSpan())
                || name.SequenceEqual("level".AsSpan())
                || name.SequenceEqual("host".AsSpan())
                || name.SequenceEqual("full_message".AsSpan())
                || name.StartsWith("gl2_".AsSpan(), StringComparison.Ordinal);
        }

        private static bool IsAllowedInFieldName(char character)
        {
            return (character >= 'a' && character <= 'z')
                || (character >= 'A' && character <= 'Z')
                || (character >= '0' && character <= '9')
                || character == '_'
                || character == '.'
                || character == '-';
        }
    }
}
