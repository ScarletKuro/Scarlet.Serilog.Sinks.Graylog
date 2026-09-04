# Contributing

Thanks for considering a contribution. This is a maintained fork of
[Serilog.Sinks.Graylog](https://github.com/serilog-contrib/serilog-sinks-graylog); bug reports, fixes
and documentation improvements are all welcome.

Everyone taking part is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Reporting issues

Open an [issue](https://github.com/ScarletKuro/Scarlet.Serilog.Sinks.Graylog/issues) with the package
version, the target framework, the transport (UDP, TCP or HTTP) and, where it matters, the Graylog
version. A `GraylogSinkOptions` snippet that reproduces the problem is worth more than a description
of it. Serilog swallows sink failures by default, so enable `Serilog.Debugging.SelfLog` and include
what it writes:

```csharp
Serilog.Debugging.SelfLog.Enable(Console.Error);
```

Before opening a feature request, note that the sink deliberately has one registration API — the
options object — and that everything it does has to work under Native AOT and on
`netstandard2.0`. Proposals that need reflection or a newer language runtime are unlikely to land.

## Prerequisites

- The **.NET 10 SDK**. It builds every target framework in the solution, including the .NET Framework
  ones (through `Microsoft.NETFramework.ReferenceAssemblies`, so Windows is not required), and it is
  new enough to read the `.slnx` solution format.
- The **.NET 8 runtime**, because the main test project also targets `net8.0`.
- **Docker**, only for the integration tests.
- Native AOT additionally needs a C toolchain — `clang` and `zlib1g-dev` on Linux, the C++ build tools
  on Windows.

`global.json` selects the Microsoft.Testing.Platform runner for `dotnet test`; the extra `--` in the
test commands below is what separates its arguments from the SDK's.

## Building

```powershell
dotnet build scarlet-serilog-sinks-graylog.slnx
```

The library is built with `TreatWarningsAsErrors`, so a warning fails the build. That includes the
trimming, single-file and AOT analyzers, which are enabled on `net8.0` and later.

## Running the tests

```powershell
dotnet test scarlet-serilog-sinks-graylog.slnx -- --filter-not-trait "Category=Integration"
```

That is the default run and needs nothing installed: every transport is exercised against loopback
servers in-process.

The tests tagged `Category=Integration` send GELF to a **real Graylog** over UDP, TCP and HTTP and
read the stored message back out of its search API — the only way to check the things a loopback
server cannot answer, such as whether Graylog accepts a field name, reassembles a chunked datagram,
or finishes reading a null-terminated TCP frame. They need a server:

```powershell
docker compose -f tests/integration/docker-compose.yml up -d --wait
dotnet test scarlet-serilog-sinks-graylog.slnx -f net10.0 -- --filter-trait "Category=Integration"
docker compose -f tests/integration/docker-compose.yml down -v
```

The test fixture creates the three GELF inputs itself, so the server needs no setup. Without one the
tests skip rather than fail. Point `GRAYLOG_API_URI`, `GRAYLOG_USERNAME` and `GRAYLOG_PASSWORD` at
another instance to use that instead of the compose file's.

### Native AOT

Analyzers cannot prove AOT compatibility on their own, and neither can a build: only a real publish
runs ILC over the whole graph. `tests/Scarlet.Serilog.Sinks.Graylog.Aot.Tests` exists to be published
and run, twice — once as-is, and once with reflection-based serialization switched off, which forces
every value onto the sink's reflection-free path:

```powershell
dotnet publish tests/Scarlet.Serilog.Sinks.Graylog.Aot.Tests -c Release -r win-x64 -p:IncludeTestReportingExtensions=false
./tests/Scarlet.Serilog.Sinks.Graylog.Aot.Tests/bin/Release/net10.0/win-x64/publish/Scarlet.Serilog.Sinks.Graylog.Aot.Tests.exe
```

`IncludeTestReportingExtensions=false` keeps the coverage and TRX extensions — needed for the JIT run,
unsupported under ILC — out of the published graph. Substitute `linux-x64` and drop the `.exe` on
Linux. On Windows, ILC locates the MSVC linker through `vswhere`; if the publish fails with
`'vswhere.exe' is not recognized`, run it from a Developer Command Prompt or put the Visual Studio
Installer directory on `PATH`. The trim-analysis step runs before linking, so IL warnings surface
either way.

## Repository layout

| Path | What it is |
| --- | --- |
| `src/Scarlet.Serilog.Sinks.Graylog` | The library, and the only packaged project. `Core/` is a subfolder of it and keeps its `...Graylog.Core.*` namespaces, so the namespaces deliberately do not all mirror the assembly name. |
| `tests/Scarlet.Serilog.Sinks.Graylog.Tests` | The xUnit suite, unit and integration alike. |
| `tests/Scarlet.Serilog.Sinks.Graylog.Aot.Tests` | The Native AOT suite described above. Unsigned and named so it does *not* match the library's `InternalsVisibleTo`: it tests the public API as a consumer sees it. |
| `tests/integration` | The Graylog `docker-compose.yml` the integration tests run against. |
| `samples/TestApplication` | An ASP.NET Core demo app for trying the sink by hand. Not packaged. |

## Coding guidelines

- **Follow `.editorconfig`.** It is the style rulebook — four-space indentation, file-scoped
  namespaces, LF endings — and most editors apply it without configuration.
- **`LangVersion` is 11**, not the SDK default, because the library targets `netstandard2.0`,
  `net462` and `net471` alongside `net8.0`–`net10.0`. Anything you add has to compile on all six.
  Guard framework-specific code with `#if` and a `TargetFramework`/`TargetFrameworkIdentifier`
  condition in the `.csproj`, as the existing `WinHttpHandler` and `System.Text.Json` references do.
- **Nullable reference types are enabled** and the library treats every warning as an error.
- **No reflection on the hot path.** The sink builds each GELF field without it, which is what lets it
  run trimmed and AOT-compiled with reflection-based `System.Text.Json` serialization switched off.
  New code has to hold that line, or it will fail the AOT job rather than merely warn.
- **The assembly is strong-named** with `sign.snk`, and the main test project is signed with the same
  key so `InternalsVisibleTo` matches. A new test project that needs internals must be signed too.
- Public API additions need XML documentation — `GenerateDocumentationFile` is on.
- Add tests with a behaviour change. Coverage is reported to
  [Codecov](https://codecov.io/github/ScarletKuro/Scarlet.Serilog.Sinks.Graylog) on every run.
- Anything user-visible — a new option, a changed default, a behaviour worth knowing about — belongs
  in `README.md` too.

## Pull requests

Branch off `master` and open the pull request against it. Keep one concern per pull request; a
formatting sweep mixed into a behaviour change is hard to review.

CI runs on every pull request and has to be green: build and unit tests, the Graylog integration
tests, and the Native AOT publish-and-run. Running the default test command locally catches most of
it before you push.

## Releasing

Maintainers only. Pushing a SemVer tag — `4.0.0`, `4.0.0-preview.1` — runs `release.yml`, which takes
the package version from the tag name, builds, tests, packs, and publishes to NuGet.org and GitHub
Packages. Nothing needs editing in the `.csproj` for a release.
