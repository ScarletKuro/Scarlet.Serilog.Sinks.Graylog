using Scarlet.Serilog.Sinks.Graylog.Core;
using Serilog.Configuration;

namespace Scarlet.Serilog.Sinks.Graylog
{
    public class GraylogSinkOptions : GraylogSinkOptionsBase
    {
        /// <summary>
        /// When set, log events are buffered and delivered to Graylog in batches using Serilog's
        /// built-in batching sink. When <c>null</c> (the default) each event is written as it is
        /// emitted.
        /// </summary>
        /// <remarks>
        /// A batched logger must be disposed, or flushed with <c>Log.CloseAndFlush()</c>, or the
        /// tail of the buffer is lost at shutdown.
        /// </remarks>
        public BatchingOptions? Batching { get; set; }
    }
}
