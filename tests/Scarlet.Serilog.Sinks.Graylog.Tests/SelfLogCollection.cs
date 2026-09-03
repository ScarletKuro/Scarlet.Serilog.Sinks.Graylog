namespace Scarlet.Serilog.Sinks.Graylog.Tests
{
    /// <summary>
    /// Names the xUnit collection that serializes every test class driving Serilog's SelfLog.
    /// </summary>
    /// <remarks>
    /// SelfLog is a single process-global handler. Test classes run in parallel by default, so two
    /// classes calling Enable at once replace each other's handler and both assertions become
    /// unreliable - one of them sees no output at all. Sharing a collection keeps them sequential.
    /// </remarks>
    internal static class SelfLogCollection
    {
        public const string Name = "SelfLog";
    }
}
