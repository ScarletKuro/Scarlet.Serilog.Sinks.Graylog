using Xunit.Sdk;
using Xunit.v3;

// Serilog's SelfLog is process-global, so the end-to-end test cannot have other tests emitting
// alongside it. Serialising a suite this small costs nothing.
[assembly: Parallelization(Mode = ParallelMode.None)]
