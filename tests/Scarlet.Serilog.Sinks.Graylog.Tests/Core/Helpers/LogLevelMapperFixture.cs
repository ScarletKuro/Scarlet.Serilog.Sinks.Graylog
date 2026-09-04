using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using Serilog.Events;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.Helpers
{
    public class LogLevelMapperFixture
    {
        [Theory]
        [InlineData(LogEventLevel.Verbose, 7, "Verbose")]
        [InlineData(LogEventLevel.Debug, 7, "Debug")]
        [InlineData(LogEventLevel.Information, 6, "Information")]
        [InlineData(LogEventLevel.Warning, 4, "Warning")]
        [InlineData(LogEventLevel.Error, 3, "Error")]
        [InlineData(LogEventLevel.Fatal, 0, "Fatal")]
        [InlineData((LogEventLevel)(-1), 7, "-1")]
        [InlineData((LogEventLevel)6, 0, "6")]
        public void MapsEverySerilogLevelAndClampsUnknownValues(
            LogEventLevel level,
            int expectedNumber,
            string expectedName)
        {
            Assert.Equal(expectedNumber, LogLevelMapper.GetMappedLevel(level));
            Assert.Equal(expectedName, LogLevelMapper.GetLevelName(level));
        }
    }
}
