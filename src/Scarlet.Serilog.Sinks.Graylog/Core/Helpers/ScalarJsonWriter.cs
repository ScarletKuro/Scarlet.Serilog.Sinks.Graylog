using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Helpers
{
    /// <summary>
    /// Writes a <see cref="global::Serilog.Events.ScalarValue"/> payload straight into a
    /// <see cref="Utf8JsonWriter"/> without relying on reflection-based serialization, so the sink
    /// stays usable under Native AOT.
    /// </summary>
    /// <remarks>
    /// The reflection-based <c>JsonSerializer</c> overloads taking a bare <see cref="object"/> cannot be
    /// used here: the value is statically typed as <see cref="object"/>, so they carry
    /// <c>RequiresUnreferencedCode</c>/<c>RequiresDynamicCode</c> and fail outright once reflection-based
    /// serialization is unavailable. Source generation is not an option either - a sink cannot enumerate
    /// the types a consumer will log, and the set is genuinely open via <c>Destructure.AsScalar&lt;T&gt;()</c>
    /// or any enricher, since <c>ScalarValue</c> accepts an unvalidated <see cref="object"/>.
    /// <para>
    /// So contracts are resolved through a copy of the caller's <see cref="JsonSerializerOptions"/> first, which keeps
    /// custom converters and source-generated contexts working exactly as before, and falls back to
    /// <see cref="WriteWithoutReflection"/> when no contract is available - the situation under Native AOT.
    /// Both <see cref="JsonSerializerOptions.TryGetTypeInfo"/> and the <see cref="JsonTypeInfo"/> overload
    /// of <c>Serialize</c> are free of trimming annotations.
    /// </para>
    /// <para>
    /// Values go directly to the caller's writer rather than through
    /// <c>JsonSerializer.SerializeToNode</c>. That method serializes to a pooled UTF-8 buffer and then
    /// parses the result back into a <c>JsonDocument</c>-backed node, which the payload writer then
    /// re-serializes - so every scalar crossed UTF-8 three times and left a parsed document behind.
    /// Writing once is both cheaper and more faithful: re-serializing an already-escaped value escaped
    /// it a second time, which is why the <c>+</c> in a <see cref="DateTimeOffset"/> offset used to
    /// reach Graylog as a unicode escape sequence.
    /// </para>
    /// </remarks>
    internal sealed class ScalarJsonWriter
    {
        /// <summary>
        /// A private copy of the caller's options, and what every contract is resolved through.
        /// </summary>
        /// <remarks>
        /// Resolving a contract makes a <see cref="JsonSerializerOptions"/> instance read-only, and
        /// <see cref="TryEnsureResolver"/> does so outright. Doing that to the instance the consumer
        /// handed the sink would freeze their options as a side effect of the first log event, so a
        /// setter they call afterwards throws <see cref="InvalidOperationException"/> - and one options
        /// instance shared between the sink and the application's own serialization is an ordinary
        /// arrangement. The copy carries the converters, the resolver and every other setting across,
        /// so nothing about how a value is written changes; only the freeze stays on this side.
        /// </remarks>
        private readonly JsonSerializerOptions _options;

        /// <summary>
        /// Contract per runtime type, or <c>null</c> for types that must take the reflection-free path.
        /// Scoped to one <see cref="JsonSerializerOptions"/> instance, because a different instance can
        /// resolve a different contract for the same type.
        /// </summary>
        private readonly ConcurrentDictionary<Type, JsonTypeInfo?> _contracts =
            new ConcurrentDictionary<Type, JsonTypeInfo?>();

        public ScalarJsonWriter(JsonSerializerOptions options)
        {
            _options = new JsonSerializerOptions(options);
        }

        /// <summary>
        /// Writes a non-null scalar payload as the current JSON value.
        /// </summary>
        public void WriteValue(Utf8JsonWriter writer, object value)
        {
            // Ahead of the contract lookup, because Graylog drops boolean additional fields and the
            // text substitution has to happen before anything is written; see WriteBooleanText. The
            // cost is that a custom JsonConverter<bool> no longer decides how a boolean is written -
            // a deliberate trade, since honouring it would mean routing every boolean through a
            // scratch buffer to find out whether the result needs substituting.
            if (value is bool flag)
            {
                WriteBooleanText(writer, flag);

                return;
            }

            Type type = value.GetType();

            if (_contracts.TryGetValue(type, out JsonTypeInfo? cached))
            {
                if (cached == null)
                {
                    WriteWithoutReflection(writer, value);
                }
                else
                {
                    JsonSerializer.Serialize(writer, value, cached);
                }

                return;
            }

            WriteFirstValueOfType(writer, value, type);
        }

        /// <summary>
        /// Writes the first value the sink sees of a given type, and remembers how to write the rest.
        /// </summary>
        /// <remarks>
        /// Resolving a contract is not proof that it can write the value: <see cref="IntPtr"/>,
        /// <see cref="UIntPtr"/>, <see cref="Type"/> and <see cref="MemberInfo"/> all resolve and then
        /// throw. System.Text.Json raises that before writing anything, so the recovery below is
        /// guarded on the writer not having advanced - if a converter ever did throw halfway through a
        /// value there would be no way back, and losing the event beats emitting a corrupt payload.
        /// </remarks>
        private void WriteFirstValueOfType(Utf8JsonWriter writer, object value, Type type)
        {
            JsonTypeInfo? contract = ResolveContract(type);

            if (contract == null)
            {
                _contracts[type] = null;

                WriteWithoutReflection(writer, value);

                return;
            }

            long written = writer.BytesPending + writer.BytesCommitted;

            try
            {
                JsonSerializer.Serialize(writer, value, contract);
            }
            catch (NotSupportedException) when (writer.BytesPending + writer.BytesCommitted == written)
            {
                _contracts[type] = null;

                WriteWithoutReflection(writer, value);

                return;
            }

            // Only cached once the contract has actually served a value.
            _contracts[type] = contract;
        }

        /// <summary>
        /// Graylog drops boolean additional fields, so they are written as their text form instead.
        /// </summary>
        /// <remarks>
        /// Verified against Graylog 6.1: a field sent as a JSON <c>true</c> or <c>false</c> never appears
        /// on the stored message, while the strings <c>"true"</c> and <c>"false"</c> do, and stay
        /// searchable as <c>MyFlag:true</c>. Losing the field entirely is the worse of the two.
        /// </remarks>
        private static void WriteBooleanText(Utf8JsonWriter writer, bool value)
        {
            writer.WriteStringValue(value ? "true".AsSpan() : "false".AsSpan());
        }

        private JsonTypeInfo? ResolveContract(Type type)
        {
            // Type and MemberInfo do resolve a contract, but serializing one throws. They reach a sink
            // whenever '{@Property}' captures a Type or a MemberInfo, which Serilog's
            // ReflectionTypesScalarDestructuringPolicy passes through as-is, so they are short-circuited
            // rather than left to cost a throw on first use.
            if (typeof(MemberInfo).IsAssignableFrom(type))
            {
                return null;
            }

            if (!TryEnsureResolver())
            {
                return null;
            }

            // Reports a missing contract rather than throwing for it - which is the Native AOT case, and
            // any type the resolver does not cover. GetTypeInfo threw NotSupportedException for both.
            return _options.TryGetTypeInfo(type, out JsonTypeInfo? typeInfo) ? typeInfo : null;
        }

        /// <summary>
        /// Makes sure the options carry a contract resolver, and reports whether one is available.
        /// </summary>
        /// <remarks>
        /// A <see cref="JsonSerializerOptions"/> instance that has never been used has no resolver, and
        /// <see cref="JsonSerializerOptions.GetTypeInfo(Type)"/> throws in that state. Serializing used to
        /// populate the default reflection-based resolver as a side effect, so it has to be requested
        /// explicitly now to keep behaving as before for consumers who never configured one.
        /// </remarks>
        private bool TryEnsureResolver()
        {
            if (_options.TypeInfoResolver != null)
            {
                return true;
            }
#if NET
            // A feature switch, so publishing with JsonSerializerIsReflectionEnabledByDefault=false folds
            // this to a constant and lets the trimmer drop the reflection-based branch below entirely.
            if (!JsonSerializer.IsReflectionEnabledByDefault)
            {
                return false;
            }
#endif
            PopulateDefaultResolver();

            return true;
        }

#if NET
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
            Justification = "Guarded by JsonSerializer.IsReflectionEnabledByDefault. When reflection is unavailable the caller falls back to WriteWithoutReflection.")]
        [UnconditionalSuppressMessage("AotAnalysis", "IL3050",
            Justification = "Guarded by JsonSerializer.IsReflectionEnabledByDefault. When reflection is unavailable the caller falls back to WriteWithoutReflection.")]
#endif
        private void PopulateDefaultResolver()
        {
            _options.MakeReadOnly(populateMissingResolver: true);
        }

        /// <summary>
        /// Writes a scalar using only the annotation-free <see cref="Utf8JsonWriter"/> methods.
        /// </summary>
        /// <remarks>
        /// <see cref="Utf8JsonWriter"/> has a dedicated method for the types handled by the first group
        /// below; every other type has to be converted explicitly. The formats used here were verified
        /// byte-for-byte against the reflection-based path.
        /// </remarks>
        private static void WriteWithoutReflection(Utf8JsonWriter writer, object value)
        {
            if (TryWriteBuiltInScalar(writer, value))
            {
                return;
            }

            switch (value)
            {
                case bool boolValue:
                    // Unreachable through WriteValue, which substitutes the text form first; here so
                    // that the two switches together are a complete account of what Serilog can hand
                    // a sink.
                    WriteBooleanText(writer, boolValue);

                    break;

                // IsPrimitive, so Serilog captures these as scalars, but System.Text.Json refuses them.
                // Widening is lossless and gives a usable number instead of a dropped event.
                case IntPtr intPtrValue:
                    writer.WriteNumberValue(intPtrValue.ToInt64());

                    break;
                case UIntPtr uintPtrValue:
                    writer.WriteNumberValue(uintPtrValue.ToUInt64());

                    break;

                case Enum enumValue:
                    WriteEnum(writer, enumValue);

                    break;

                // Type derives from MemberInfo, so this covers both.
                case MemberInfo memberInfoValue:
                    writer.WriteStringValue(memberInfoValue.ToString().AsSpan());

                    break;

                default:
                    // Reached for Destructure.AsScalar<T>() types and anything an enricher put into a
                    // ScalarValue directly. Serilog itself renders unknown values this way.
                    writer.WriteStringValue(value.ToString().AsSpan());

                    break;
            }
        }

        /// <summary>
        /// Writes framework scalar types without reflection when no serializer contract is available.
        /// </summary>
        /// <returns><c>false</c> when <paramref name="value"/> is not one of them.</returns>
        /// <remarks>
        /// Every type here is one a consumer cannot decorate with a <c>JsonConverterAttribute</c>,
        /// because they own none of them - which is what makes it safe to bypass the contract for
        /// these and these only. An enum is deliberately absent for exactly that reason.
        /// </remarks>
        private static bool TryWriteBuiltInScalar(Utf8JsonWriter writer, object value)
        {
            switch (value)
            {
                // Dedicated Utf8JsonWriter methods exist for these; System.Text.Json formats them.
                case string stringValue:
                    writer.WriteStringValue(stringValue.AsSpan());

                    break;
                case int intValue:
                    writer.WriteNumberValue(intValue);

                    break;
                case long longValue:
                    writer.WriteNumberValue(longValue);

                    break;
                case double doubleValue:
                    writer.WriteNumberValue(doubleValue);

                    break;
                case decimal decimalValue:
                    writer.WriteNumberValue(decimalValue);

                    break;
                case float floatValue:
                    writer.WriteNumberValue(floatValue);

                    break;
                case byte byteValue:
                    writer.WriteNumberValue(byteValue);

                    break;
                case sbyte sbyteValue:
                    writer.WriteNumberValue(sbyteValue);

                    break;
                case short shortValue:
                    writer.WriteNumberValue(shortValue);

                    break;
                case ushort ushortValue:
                    writer.WriteNumberValue(ushortValue);

                    break;
                case uint uintValue:
                    writer.WriteNumberValue(uintValue);

                    break;
                case ulong ulongValue:
                    writer.WriteNumberValue(ulongValue);

                    break;
                case char charValue:
                    WriteChar(writer, charValue);

                    break;
                case Guid guidValue:
                    writer.WriteStringValue(guidValue);

                    break;
                case DateTime dateTimeValue:
                    writer.WriteStringValue(dateTimeValue);

                    break;
                case DateTimeOffset dateTimeOffsetValue:
                    writer.WriteStringValue(dateTimeOffsetValue);

                    break;

                // No dedicated method - formatted explicitly, matching System.Text.Json's own output.
                case TimeSpan timeSpanValue:
                    WriteFormatted(writer, timeSpanValue, "c");

                    break;
                case Uri uriValue:
                    writer.WriteStringValue(uriValue.OriginalString.AsSpan());

                    break;
#if NET
                case DateOnly dateOnlyValue:
                    WriteFormatted(writer, dateOnlyValue, "yyyy-MM-dd");

                    break;
                case TimeOnly timeOnlyValue:
                    // System.Text.Json omits the fraction entirely when it is zero, and otherwise writes all
                    // seven digits without trimming.
                    WriteFormatted(writer, timeOnlyValue,
                        timeOnlyValue.Ticks % TimeSpan.TicksPerSecond == 0 ? "HH:mm:ss" : "O");

                    break;
#endif
                default:
                    return false;
            }

            return true;
        }

        private static void WriteChar(Utf8JsonWriter writer, char value)
        {
            Span<char> single = stackalloc char[1];

            single[0] = value;

            writer.WriteStringValue(single);
        }

        /// <summary>
        /// Writes a formattable value as a JSON string without allocating one first.
        /// </summary>
        private static void WriteFormatted<T>(Utf8JsonWriter writer, T value, string format)
#if NET
            where T : ISpanFormattable
#else
            where T : IFormattable
#endif
        {
#if NET
            Span<char> buffer = stackalloc char[32];

            if (value.TryFormat(buffer, out int written, format.AsSpan(), CultureInfo.InvariantCulture))
            {
                writer.WriteStringValue(buffer.Slice(0, written));

                return;
            }
#endif
            writer.WriteStringValue(value.ToString(format, CultureInfo.InvariantCulture).AsSpan());
        }

        /// <summary>
        /// Writes an enum as its numeric value, which is what System.Text.Json does by default.
        /// </summary>
        /// <remarks>
        /// Going through the underlying type code rather than <c>Convert.ToInt64</c> matters: the latter
        /// overflows for <see cref="ulong"/>-backed enums holding values above <see cref="long.MaxValue"/>.
        /// </remarks>
        private static void WriteEnum(Utf8JsonWriter writer, Enum value)
        {
            switch (Convert.GetTypeCode(value))
            {
                case TypeCode.Byte:
                    writer.WriteNumberValue((byte)(object)value);

                    break;
                case TypeCode.SByte:
                    writer.WriteNumberValue((sbyte)(object)value);

                    break;
                case TypeCode.Int16:
                    writer.WriteNumberValue((short)(object)value);

                    break;
                case TypeCode.UInt16:
                    writer.WriteNumberValue((ushort)(object)value);

                    break;
                case TypeCode.Int32:
                    writer.WriteNumberValue((int)(object)value);

                    break;
                case TypeCode.UInt32:
                    writer.WriteNumberValue((uint)(object)value);

                    break;
                case TypeCode.Int64:
                    writer.WriteNumberValue((long)(object)value);

                    break;
                case TypeCode.UInt64:
                    writer.WriteNumberValue((ulong)(object)value);

                    break;
                default:
                    writer.WriteStringValue(value.ToString().AsSpan());

                    break;
            }
        }
    }
}
