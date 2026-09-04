# Performance benchmarks

Run the message-building allocation and throughput comparison in Release mode:

```powershell
dotnet run -c Release --project benchmarks/Scarlet.Serilog.Sinks.Graylog.Benchmarks -- --filter "*MessageBuilding*"
```

The baseline reproduces the former `JsonObject`/`JsonNode` pipeline for representative scalar
events. The comparison writes the same event directly with the production `GelfMessageBuilder`,
`Utf8JsonWriter`, and normally allocated growable buffer. Keep both cases when changing the hot path
so performance claims remain reproducible rather than living only in commit messages or comments.
