# Performance benchmarks

The small-optimization comparison measures the production message builder against a frozen copy
from commit `152eb0a`, before literal-message rendering and template-property exclusion were optimized.
The copy shares the production field and scalar writers, so it isolates the builder changes only.
Setup fails if the two builders produce different payload bytes:

```powershell
dotnet run -c Release --project benchmarks/Scarlet.Serilog.Sinks.Graylog.Benchmarks -- --filter "*SmallOptimizations*" --job short --warmupCount 3 --iterationCount 5 --launchCount 1
```

It covers plain literals, literals with enriched properties, parameterized messages, and missing
properties, with template-property exclusion enabled where the scenario ends in `Excluded`.
These measurements cover JSON message building and buffer allocation, excluding transport and compression.

Run the message-building allocation and throughput comparison in Release mode:

```powershell
dotnet run -c Release --project benchmarks/Scarlet.Serilog.Sinks.Graylog.Benchmarks -- --filter "*MessageBuilding*"
```

The baseline reproduces the former `JsonObject`/`JsonNode` pipeline for representative scalar
events. The comparison writes the same event directly with the production `GelfMessageBuilder`,
`Utf8JsonWriter`, and normally allocated growable buffer. Keep both cases when changing the hot path
so performance claims remain reproducible rather than living only in commit messages or comments.

The compression-buffer comparison covers both highly compressible and high-entropy data, and
compares input-sized allocation with an initial buffer capped to one default-sized datagram:

```powershell
dotnet run -c Release --project benchmarks/Scarlet.Serilog.Sinks.Graylog.Benchmarks -- --filter "*CompressionBuffer*"
```

The single-datagram dispatch comparison isolates the task allocation added by an unnecessary async
wrapper around the transport client:

```powershell
dotnet run -c Release --project benchmarks/Scarlet.Serilog.Sinks.Graylog.Benchmarks -- --filter "*UdpDispatch*"
```

The pending-send comparison measures the bookkeeping used by unbatched fire-and-forget sends. It
compares the former concurrent dictionary and two continuations with the counter and single
continuation used by the sink:

```powershell
dotnet run -c Release --project benchmarks/Scarlet.Serilog.Sinks.Graylog.Benchmarks -- --filter "*PendingSend*"
```
