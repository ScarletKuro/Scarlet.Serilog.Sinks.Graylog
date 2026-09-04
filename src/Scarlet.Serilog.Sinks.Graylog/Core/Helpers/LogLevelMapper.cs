using Serilog.Events;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Helpers
{
    internal static class LogLevelMapper
    {
        /// <summary>
        /// Gets the mapped level.
        /// </summary>
        /// <param name="logEventLevel">The log event level.</param>
        /// <returns>Syslog level</returns>
        /// <remarks>
        /// SyslogLevels:
        /// 0 Emergency: system is unusable
        /// 1 Alert: action must be taken immediately
        /// 2 Critical: critical conditions
        /// 3 Error: error conditions
        /// 4 Warning: warning conditions
        /// 5 Notice: normal but significant condition
        /// 6 Informational: informational messages
        /// 7 Debug: debug-level messages
        ///
        /// A switch over the enum rather than a dictionary lookup, which is a hash and a bucket probe
        /// on the hottest path the sink has. A value outside the enum - <c>LogEventLevel</c> is an
        /// <see cref="int"/> underneath, and nothing stops a cast - is clamped to the nearest syslog
        /// level rather than throwing <c>KeyNotFoundException</c> and costing the event: anything below
        /// <see cref="LogEventLevel.Verbose"/> is as unimportant as debug, and anything above
        /// <see cref="LogEventLevel.Fatal"/> is at least as severe as fatal.
        /// </remarks>
        internal static int GetMappedLevel(LogEventLevel logEventLevel)
        {
            return logEventLevel switch
            {
                LogEventLevel.Verbose or LogEventLevel.Debug => 7,
                LogEventLevel.Information => 6,
                LogEventLevel.Warning => 4,
                LogEventLevel.Error => 3,
                LogEventLevel.Fatal => 0,
                _ => logEventLevel < LogEventLevel.Verbose ? 7 : 0
            };
        }

        /// <summary>
        /// The Serilog level name written to <c>_stringLevel</c>.
        /// </summary>
        /// <remarks>
        /// A switch over literals rather than <see cref="object.ToString"/>, which allocates a fresh
        /// string for the field on every single event.
        /// </remarks>
        internal static string GetLevelName(LogEventLevel logEventLevel)
        {
            return logEventLevel switch
            {
                LogEventLevel.Verbose => nameof(LogEventLevel.Verbose),
                LogEventLevel.Debug => nameof(LogEventLevel.Debug),
                LogEventLevel.Information => nameof(LogEventLevel.Information),
                LogEventLevel.Warning => nameof(LogEventLevel.Warning),
                LogEventLevel.Error => nameof(LogEventLevel.Error),
                LogEventLevel.Fatal => nameof(LogEventLevel.Fatal),
                _ => logEventLevel.ToString()
            };
        }
    }
}
