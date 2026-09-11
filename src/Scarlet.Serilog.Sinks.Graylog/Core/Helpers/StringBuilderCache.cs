using System;
using System.Text;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Helpers
{
    /// <summary>
    /// Caches one <see cref="StringBuilder"/> per thread per <typeparamref name="TSlot"/>, the way the
    /// BCL's own internal <c>StringBuilderCache</c> backs <see cref="string.Format(string, object?)"/>
    /// and friends.
    /// </summary>
    /// <typeparam name="TSlot">
    /// A marker type distinguishing independent caches. Two call sites that both need their own
    /// outstanding builder at once - <see cref="MessageBuilders.ExceptionMessageBuilder"/> needs a
    /// messages builder and a stack-trace builder in the same call - use two different marker types
    /// so each builder can settle at the capacity that call site actually needs.
    /// </typeparam>
    /// <remarks>
    /// Building an exception's flattened message and stack trace, or rendering a sequence or
    /// dictionary element back to text, needs a scratch buffer that is thrown away as soon as the
    /// result is copied out to a <c>string</c>. Every event otherwise allocated a fresh
    /// <see cref="StringBuilder"/> instance purely to be discarded. A single thread-static slot removes
    /// that in steady state: the buffer from the previous event on this thread is still sitting there,
    /// already grown to a useful size.
    /// <para>
    /// A builder that grew past <see cref="MaxCachedCapacity"/> is not cached back - the largest stack
    /// trace a thread ever logs must not pin that much memory on it for the rest of the process just
    /// because it happened once.
    /// </para>
    /// </remarks>
    internal static class StringBuilderCache<TSlot>
    {
        /// <summary>
        /// Stack traces run far longer than ordinary rendered values, so the cap sits well above the
        /// BCL's own default (360 chars) - large enough that a typical stack trace still gets cached,
        /// small enough that one pathological event cannot pin an outsized buffer forever.
        /// </summary>
        private const int MaxCachedCapacity = 8192;

        [ThreadStatic]
        // ReSharper disable once StaticMemberInGenericType
        private static StringBuilder? _cached;

        /// <summary>
        /// Returns this thread's cached builder for <typeparamref name="TSlot"/>, cleared and ready to
        /// use, or a new one if none is cached or one is already checked out.
        /// </summary>
        public static StringBuilder Acquire()
        {
            StringBuilder? builder = _cached;

            if (builder == null)
            {
                return new StringBuilder();
            }

            // Cleared before handing it back out, not before caching it: an exception thrown between
            // Acquire and Release would otherwise leave the previous event's text sitting in the slot
            // for whatever acquires it next.
            _cached = null;
            builder.Clear();

            return builder;
        }

        /// <summary>
        /// Returns a builder <see cref="Acquire"/> produced. Safe to call even when the builder was
        /// never released - the cache only ever holds the most recently released instance.
        /// </summary>
        public static void Release(StringBuilder builder)
        {
            if (builder.Capacity <= MaxCachedCapacity)
            {
                _cached = builder;
            }
        }

        /// <summary>Reads the builder's text and releases it in one step.</summary>
        public static string GetStringAndRelease(StringBuilder builder)
        {
            string result = builder.ToString();

            Release(builder);

            return result;
        }
    }
}
