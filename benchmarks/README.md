# Performance benchmarks

Run the message-building allocation and throughput comparison in Release mode:

```powershell
dotnet run -c Release --project benchmarks/Scarlet.Serilog.Sinks.Graylog.Benchmarks -- --filter "*MessageBuilding*"
```

The baseline reproduces the former `JsonObject`/`JsonNode` pipeline for representative scalar
events. The comparisons write the same event directly with the production `GelfMessageBuilder` and
`Utf8JsonWriter`, first with a normal allocated buffer and then with the production
`ArrayPool<byte>` buffer. Keep all three cases when changing the hot path so performance claims
remain reproducible rather than living only in commit messages or comments.
