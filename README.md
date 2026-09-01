# Scarlet.Serilog.Sinks.Graylog

A maintained fork of [Serilog.Sinks.Graylog](https://github.com/whir1/serilog-sinks-graylog) by Anton Volkov, which has not received updates for a long time.

## Status

### Scarlet.Serilog.Sinks.Graylog

[![NuGet](https://img.shields.io/nuget/v/Scarlet.Serilog.Sinks.Graylog.svg)](https://www.nuget.org/packages/Scarlet.Serilog.Sinks.Graylog/)
[![Downloads](https://img.shields.io/nuget/dt/Scarlet.Serilog.Sinks.Graylog.svg)](https://www.nuget.org/packages/Scarlet.Serilog.Sinks.Graylog/)

### Scarlet.Serilog.Sinks.Graylog.Batching

[![NuGet](https://img.shields.io/nuget/v/Scarlet.Serilog.Sinks.Graylog.Batching.svg)](https://www.nuget.org/packages/Scarlet.Serilog.Sinks.Graylog.Batching/)
[![Downloads](https://img.shields.io/nuget/dt/Scarlet.Serilog.Sinks.Graylog.Batching.svg)](https://www.nuget.org/packages/Scarlet.Serilog.Sinks.Graylog.Batching/)

## Migrating from Serilog.Sinks.Graylog

The package IDs, assembly names and root namespace all gained a `Scarlet.` prefix. To migrate:

1. Replace the `Serilog.Sinks.Graylog` / `Serilog.Sinks.Graylog.Batching` package references with `Scarlet.Serilog.Sinks.Graylog` / `Scarlet.Serilog.Sinks.Graylog.Batching`.
2. Update `using Serilog.Sinks.Graylog...;` to `using Scarlet.Serilog.Sinks.Graylog...;`.
3. Update the `Using` array in `appsettings.json` to `"Scarlet.Serilog.Sinks.Graylog"`.

The API itself is unchanged — `WriteTo.Graylog(...)`, `GraylogSinkOptions` and the transports all keep their names.

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

All options you can see at https://github.com/ScarletKuro/Scarlet.Serilog.Sinks.Graylog/blob/master/src/Scarlet.Serilog.Sinks.Graylog.Core/GraylogSinkOptions.cs

You can create your own implementation of transports or converter and set it to options. But maybe i'll delete this feature in the future

## Credits

Originally written by [Anton Volkov](https://github.com/whir1) and contributors. Licensed under the MIT License.
