using Scarlet.Serilog.Sinks.Graylog.Core;
using Serilog.Sinks.PeriodicBatching;
using System;

namespace Scarlet.Serilog.Sinks.Graylog.Batching
{
    public class BatchingGraylogSinkOptions : GraylogSinkOptionsBase
    {
        public BatchingGraylogSinkOptions()
        {
            PeriodicOptions = new PeriodicBatchingSinkOptions()
            {
                BatchSizeLimit = 10,
                Period = TimeSpan.FromSeconds(1),
                QueueLimit = 10,
            };
        }

        public PeriodicBatchingSinkOptions PeriodicOptions { get; set; }
    }
}
