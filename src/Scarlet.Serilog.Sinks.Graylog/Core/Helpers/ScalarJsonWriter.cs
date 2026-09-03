using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Helpers
{
    /// <summary>
    /// Turns a <see cref="global::Serilog.Events.ScalarValue"/> payload into a <see cref="System.Text.Json.Nodes.JsonNode"/> without
    /// relying on reflection-based serialization, so the sink stays usable under Native AOT.
    /// </summary>
    /// <remarks>
    /// The reflection-based <c>JsonSerializer.SerializeToNode(object, JsonSerializerOptions)</c> overload
    /// cannot be used here: the value is statically typed as <see cref="object"/>, so it carries
    /// <c>RequiresUnreferencedCode</c>/<c>RequiresDynamicCode</c> and fails outright once reflection-based
    /// serialization is unavailable. Source generation is not an option either - a sink cannot enumerate
    /// the types a consumer will log, and the set is genuinely open via <c>Destructure.AsScalar&lt;T&gt;()</c>
    /// or any enricher, since <c>ScalarValue</c> accepts an unvalidated <see cref="object"/>.
    /// <para>
    /// So contracts are resolved through the caller's <see cref="JsonSerializerOptions"/> first, which keeps
    /// custom converters and source-generated contexts working exactly as before, and falls back to
    /// <see cref="WriteWithoutReflection"/> when no contract is available - the situation under Native AOT.
    /// Both <see cref="JsonSerializerOptions.GetTypeInfo(Type)"/> and the <see cref="JsonTypeInfo"/> overload
    /// of <c>SerializeToNode</c> are free of trimming annotations.
    /// </para>
    /// </remarks>
    internal sealed class ScalarJsonWriter
    {
        private readonly JsonSerializerOptions _options;

        /// <summary>
        /// Contract per runtime type, or <c>null</c> for types that must take the reflection-free path.
        /// Scoped to one <see cref="JsonSerializerOptions"/> instance, because a different instance can
        /// resolve a different contract for the same type.
        /// </summary>
        private readonly ConcurrentDictionary<Type, JsonTypeInfo?> _contracts = new ConcurrentDictionary<Type, JsonTypeInfo?>();

        public ScalarJsonWriter(JsonSerializerOptions options)
        {
            _options = options;
        }

        /// <summary>
        /// The options this instance was built for, so the caller can detect a swapped-out instance.
        /// </summary>
        public JsonSerializerOptions Options => _options;

        /// <summary>
        /// Converts a non-null scalar payload to a <see cref="JsonNode"/>.
        /// </summary>
        public JsonNode? ToJsonNode(object value)
        {
            Type type = value.GetType();

            if (_contracts.TryGetValue(type, out JsonTypeInfo? cached))
            {
                return cached is null
                    ? WriteWithoutReflection(value)
                    : JsonSerializer.SerializeToNode(value, cached);
            }

            JsonTypeInfo? contract = ResolveContract(type);

            if (contract != null)
            {
                try
                {
                    // Resolving a contract is not proof that it can write the value: IntPtr and UIntPtr
                    // resolve but then throw. Only cache the contract once it has served a value.
                    JsonNode? node = JsonSerializer.SerializeToNode(value, contract);

                    _contracts[type] = contract;

                    return node;
                } catch (NotSupportedException)
                {
                    // Fall through to the reflection-free path and remember that for next time.
                }
            }

            _contracts[type] = null;

            return WriteWithoutReflection(value);
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
        /// Writes a scalar using only the annotation-free <see cref="JsonValue"/> factory overloads.
        /// </summary>
        /// <remarks>
        /// <see cref="JsonValue"/> has dedicated, unannotated overloads for the types handled by the first
        /// group below; every other type has to be converted explicitly, because the generic
        /// <c>JsonValue.Create&lt;T&gt;</c> is annotated and would reintroduce the AOT problem. The formats
        /// used here were verified byte-for-byte against the reflection-based path.
        /// </remarks>
        private static JsonNode? WriteWithoutReflection(object value)
        {
            switch (value)
            {
                // Dedicated JsonValue.Create overloads exist for these; System.Text.Json formats them.
                case string stringValue:
                    return JsonValue.Create(stringValue);
                case bool boolValue:
                    return JsonValue.Create(boolValue);
                case int intValue:
                    return JsonValue.Create(intValue);
                case long longValue:
                    return JsonValue.Create(longValue);
                case double doubleValue:
                    return JsonValue.Create(doubleValue);
                case decimal decimalValue:
                    return JsonValue.Create(decimalValue);
                case float floatValue:
                    return JsonValue.Create(floatValue);
                case byte byteValue:
                    return JsonValue.Create(byteValue);
                case sbyte sbyteValue:
                    return JsonValue.Create(sbyteValue);
                case short shortValue:
                    return JsonValue.Create(shortValue);
                case ushort ushortValue:
                    return JsonValue.Create(ushortValue);
                case uint uintValue:
                    return JsonValue.Create(uintValue);
                case ulong ulongValue:
                    return JsonValue.Create(ulongValue);
                case char charValue:
                    return JsonValue.Create(charValue);
                case Guid guidValue:
                    return JsonValue.Create(guidValue);
                case DateTime dateTimeValue:
                    return JsonValue.Create(dateTimeValue);
                case DateTimeOffset dateTimeOffsetValue:
                    return JsonValue.Create(dateTimeOffsetValue);

                // No dedicated overload - convert explicitly, matching System.Text.Json's own output.
                case TimeSpan timeSpanValue:
                    return JsonValue.Create(timeSpanValue.ToString("c", CultureInfo.InvariantCulture));
                case Uri uriValue:
                    return JsonValue.Create(uriValue.OriginalString);
#if NET
                case DateOnly dateOnlyValue:
                    return JsonValue.Create(dateOnlyValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                case TimeOnly timeOnlyValue:
                    // System.Text.Json omits the fraction entirely when it is zero, and otherwise writes all
                    // seven digits without trimming.
                    return JsonValue.Create(timeOnlyValue.Ticks % TimeSpan.TicksPerSecond == 0
                        ? timeOnlyValue.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                        : timeOnlyValue.ToString("O", CultureInfo.InvariantCulture));
#endif
                // IsPrimitive, so Serilog captures these as scalars, but System.Text.Json refuses them.
                // Widening is lossless and gives a usable number instead of a dropped event.
                case IntPtr intPtrValue:
                    return JsonValue.Create(intPtrValue.ToInt64());
                case UIntPtr uintPtrValue:
                    return JsonValue.Create(uintPtrValue.ToUInt64());

                case Enum enumValue:
                    return WriteEnum(enumValue);

                // Type derives from MemberInfo, so this covers both.
                case MemberInfo memberInfoValue:
                    return JsonValue.Create(memberInfoValue.ToString());

                default:
                    // Reached for Destructure.AsScalar<T>() types and anything an enricher put into a
                    // ScalarValue directly. Serilog itself renders unknown values this way.
                    return JsonValue.Create(value.ToString());
            }
        }

        /// <summary>
        /// Writes an enum as its numeric value, which is what System.Text.Json does by default.
        /// </summary>
        /// <remarks>
        /// Going through the underlying type code rather than <c>Convert.ToInt64</c> matters: the latter
        /// overflows for <see cref="ulong"/>-backed enums holding values above <see cref="long.MaxValue"/>.
        /// </remarks>
        private static JsonNode? WriteEnum(Enum value)
        {
            switch (Convert.GetTypeCode(value))
            {
                case TypeCode.Byte:
                    return JsonValue.Create((byte)(object)value);
                case TypeCode.SByte:
                    return JsonValue.Create((sbyte)(object)value);
                case TypeCode.Int16:
                    return JsonValue.Create((short)(object)value);
                case TypeCode.UInt16:
                    return JsonValue.Create((ushort)(object)value);
                case TypeCode.Int32:
                    return JsonValue.Create((int)(object)value);
                case TypeCode.UInt32:
                    return JsonValue.Create((uint)(object)value);
                case TypeCode.Int64:
                    return JsonValue.Create((long)(object)value);
                case TypeCode.UInt64:
                    return JsonValue.Create((ulong)(object)value);
                default:
                    return JsonValue.Create(value.ToString());
            }
        }
    }
}
