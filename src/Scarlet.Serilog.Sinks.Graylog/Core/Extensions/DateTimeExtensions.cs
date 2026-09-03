using System;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Extensions
{
    internal static class DateTimeExtensions
    {
        private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Converts to nix date time.
        /// </summary>
        /// <param name="dateTimeOffset"></param>
        /// <returns></returns>
        public static double ConvertToNix(this DateTimeOffset dateTimeOffset)
        {
            var duration = dateTimeOffset.ToUniversalTime() - Epoch;

            return Math.Round(duration.TotalSeconds, 3, MidpointRounding.AwayFromZero);
        }
    }
}
