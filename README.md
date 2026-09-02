# Scarlet.Serilog.Sinks.Graylog

A maintained fork of [Serilog.Sinks.Graylog](https://github.com/whir1/serilog-sinks-graylog) by Anton Volkov, which has not received updates for a long time.

## Status

[![CI](https://github.com/ScarletKuro/Scarlet.Serilog.Sinks.Graylog/actions/workflows/ci.yml/badge.svg)](https://github.com/ScarletKuro/Scarlet.Serilog.Sinks.Graylog/actions/workflows/ci.yml)

[![NuGet](https://img.shields.io/nuget/v/Scarlet.Serilog.Sinks.Graylog.svg)](https://www.nuget.org/packages/Scarlet.Serilog.Sinks.Graylog/)
[![Downloads](https://img.shields.io/nuget/dt/Scarlet.Serilog.Sinks.Graylog.svg)](https://www.nuget.org/packages/Scarlet.Serilog.Sinks.Graylog/)

## Migrating from Serilog.Sinks.Graylog

The package IDs, assembly names and root namespace all gained a `Scarlet.` prefix. To migrate:

1. Replace the `Serilog.Sinks.Graylog` package reference with `Scarlet.Serilog.Sinks.Graylog`.
2. Update `using Serilog.Sinks.Graylog...;` to `using Scarlet.Serilog.Sinks.Graylog...;`.
3. Update the `Using` array in `appsettings.json` to `"Scarlet.Serilog.Sinks.Graylog"`.

`WriteTo.Graylog(...)`, `GraylogSinkOptions` and the transports all keep their names, and an
unbatched sink behaves exactly as before.

## Migrating from Serilog.Sinks.Graylog.Batching

There is no `Scarlet.Serilog.Sinks.Graylog.Batching` package — [batching](#batching) is built into
`Scarlet.Serilog.Sinks.Graylog`, on top of Serilog 4's own batching support. Drop the
`Serilog.Sinks.Graylog.Batching` and `Serilog.Sinks.PeriodicBatching` references and:

| Before | Now |
| --- | --- |
| `using Serilog.Sinks.Graylog.Batching;` | `using Scarlet.Serilog.Sinks.Graylog;` |
| `new BatchingGraylogSinkOptions { PeriodicOptions = new PeriodicBatchingSinkOptions { ... } }` | `new GraylogSinkOptions { Batching = new BatchingOptions { ... } }` |
| `PeriodicBatchingSinkOptions.Period` | `BatchingOptions.BufferingTimeLimit` |
| `period:` argument | `bufferingTimeLimit:` argument |

Two things to know:

- **The defaults changed**, following Serilog's: `BatchSizeLimit` 10 → 1000, buffering 1s → 2s,
  `QueueLimit` 10 (options) / 1000 (arguments) → 100000.
- `BufferingTimeLimit` is a *maximum* delay that a full batch pre-empts, not the fixed timer tick
  that `Period` was.

## What is this sink ?
The Serilog Graylog sink project is a sink (basically a writer) for the Serilog logging framework. Structured log events are written to sinks and each sink is responsible for writing it to its own backend, database, store etc. This sink delivers the data to Graylog2, a NoSQL search engine.

## Quick start

```powershell
Install-Package Scarlet.Serilog.Sinks.Graylog
```
Register the sink in code.
```csharp
using Scarlet.Serilog.Sinks.Graylog;
using Scarlet.Serilog.Sinks.Graylog.Core;

var loggerConfig = new LoggerConfiguration()
    .WriteTo.Graylog(new GraylogSinkOptions
      {
          HostnameOrAddress = "localhost",
          Port = 12201
      });
```
...or alternatively configure the sink in appsettings.json configuration like so:

```json
{
  "Serilog": {
    "Using": ["Scarlet.Serilog.Sinks.Graylog"],
    "MinimumLevel": "Debug",
    "WriteTo": [
    {
        "Name": "Graylog",
        "Args": {
            "hostnameOrAddress": "localhost",
            "port": "12201",
            "transportType": "Udp"
        }
    }
    ]
  }
}
```

Note that because of the limitations of the Serilog.Settings.Configuration package, you cannot configure IGelfConverter using json. 

by default udp protocol is using, if you want to use http define sink options like 

```csharp
new GraylogSinkOptions
      {
          HostnameOrAddress = "http://localhost",
          Port = 12201,
          TransportType = TransportType.Http,
      }
```

All options you can see at
[`GraylogSinkOptions.cs`](https://github.com/ScarletKuro/Scarlet.Serilog.Sinks.Graylog/blob/master/src/Scarlet.Serilog.Sinks.Graylog/GraylogSinkOptions.cs)
(which adds `Batching`) and its base
[`Core/GraylogSinkOptions.cs`](https://github.com/ScarletKuro/Scarlet.Serilog.Sinks.Graylog/blob/master/src/Scarlet.Serilog.Sinks.Graylog/Core/GraylogSinkOptions.cs).

You can create your own implementation of transports or converter and set it to options. But maybe i'll delete this feature in the future

## Batching

Events are written as they are emitted by default. Set `GraylogSinkOptions.Batching` to buffer them
and deliver them in batches instead, using Serilog's built-in batching:

```csharp
using Serilog.Configuration;

var loggerConfig = new LoggerConfiguration()
    .WriteTo.Graylog(new GraylogSinkOptions
      {
          HostnameOrAddress = "localhost",
          Port = 12201,
          Batching = new BatchingOptions
          {
              BatchSizeLimit = 500,
              BufferingTimeLimit = TimeSpan.FromSeconds(5)
          }
      });
```

Or pass any of the batching arguments to the shorthand overload:

```csharp
.WriteTo.Graylog("localhost", 12201, TransportType.Udp, batchSizeLimit: 500)
```

`batched` controls this explicitly: `true` always batches, `false` never does even alongside other
batching arguments, and leaving it unset batches only if you supplied at least one other batching
argument. So `WriteTo.Graylog("localhost", 12201, TransportType.Udp)` stays unbatched.

> **A batched logger must be disposed, or flushed with `Log.CloseAndFlush()`** — otherwise the tail
> of the buffer is lost at shutdown.

In `appsettings.json` (note that `TimeSpan` values use `TimeSpan.Parse` format, so `"00:00:05"`, not `"5s"`):

```json
{
  "Serilog": {
    "Using": [ "Scarlet.Serilog.Sinks.Graylog" ],
    "WriteTo": [
    {
        "Name": "Graylog",
        "Args": {
            "hostnameOrAddress": "localhost",
            "port": 12201,
            "transportType": "Udp",
            "batched": true,
            "batchSizeLimit": 500,
            "bufferingTimeLimit": "00:00:05"
        }
    }
    ]
  }
}
```

Batching adds retry: a batch that fails is retried for up to `RetryTimeLimit` (10 minutes by
default). Note that once `QueueLimit` is reached further events are **dropped**, not throttled.

## Native AOT and trimming

Native AOT and trimming are supported on `net8.0` and later. Nothing needs configuring — publish with
`<PublishAot>true</PublishAot>` and log as usual:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

The sink builds every GELF field without reflection, so it works with reflection-based
`System.Text.Json` serialization switched off. The assembly is marked trimmable and is built with the
trim, single-file and AOT analyzers enabled; CI publishes a harness with `PublishAot` and asserts the
payloads, so compatibility is verified end to end rather than only by analyzers.

### Customizing how values are written

`GraylogSinkOptions.JsonSerializerOptions` is the hook, and under AOT the customization has to arrive
through a **`TypeInfoResolver`** — that is, a source-generated `JsonSerializerContext`. Declare the
types whose serialization you want to control:

```csharp
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(LogEventLevel))]
[JsonSerializable(typeof(OrderStatus))]
internal partial class MyLogContext : JsonSerializerContext;
```

```csharp
new GraylogSinkOptions
{
    HostnameOrAddress = "localhost",
    Port = 12201,
    JsonSerializerOptions = new JsonSerializerOptions
    {
        TypeInfoResolver = MyLogContext.Default
    }
}
```

Property values whose type the resolver covers are serialized through it; everything else is written
directly, matching `System.Text.Json`'s own formatting.

Adding a converter to `JsonSerializerOptions.Converters` **without** also supplying a resolver has no
effect under AOT — applying a converter needs a contract, and building one from nothing requires
reflection. It does work when running on a JIT runtime, which makes it easy to miss, so route
customization through the context above. If you do add converters, use the generic
`JsonStringEnumConverter<TEnum>`; the non-generic `JsonStringEnumConverter` is itself annotated
`RequiresDynamicCode`.

Two defaults worth knowing:

- **Enums are written as numbers**, which is what `System.Text.Json` does by default. Use
  `UseStringEnumConverter` on the context, as above, for names.
- A `DateTimeOffset`, or a `DateTime` with `DateTimeKind.Local`, may write the `+` in its UTC offset
  literally rather than as a JSON unicode escape. Different bytes, same string, same instant.

`nint`, `nuint`, and a `Type` or `MemberInfo` captured with `{@Property}` are written as well; plain
`System.Text.Json` rejects all four.

### Requirements

- Configure the sink **in code**. `ReadFrom.Configuration` (`Serilog.Settings.Configuration`) binds
  sink arguments reflectively and is not AOT-friendly. It is not a dependency of this package.

## Credits

Originally written by [Anton Volkov](https://github.com/whir1) and contributors. Licensed under the MIT License.
