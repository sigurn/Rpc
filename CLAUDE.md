# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Workflow

Always use TDD. For any bug fix or new feature, write a failing test first, then implement the code
until it passes. Do not write production code before there is a test that fails for the right reason.

## Build & Test Commands

```bash
# Build
dotnet build rpc.sln
dotnet build rpc.sln -c Release

# Run unit tests
dotnet test src/Sigurn.Rpc.Tests/

# Run a single test class
dotnet test src/Sigurn.Rpc.Tests/ --filter "FullyQualifiedName~RpcHandlerTests"

# Run integration tests (requires building the NuGet package first)
dotnet build src/Sigurn.Rpc/ -c Release
dotnet test src/Sigurn.Rpc.IntegrationTests/
```

## Architecture Overview

**Sigurn.Rpc** is a .NET RPC library where interfaces decorated with `[RemoteInterface]` are automatically marshaled over a transport channel. A Roslyn source generator (Sigurn.Rpc.Generator, targets netstandard2.0) emits Adapter and Proxy classes at compile time; module initializers register them automatically so no manual setup is needed.

### Layer Model (bottom to top)

1. **Transport** (`IChannel`) — TCP, SSL, or stdin/stdout Process channels. `BaseChannel` is the abstract base; `ProcessionChannel` is the base for middleware that wraps another channel.
2. **Middleware** — composable channel wrappers: `AesChannel` (encryption), `GZipChannel` (compression), `QueueChannel` (message queuing), `RestorableChannel` (auto-reconnect with configurable retry intervals).
3. **Protocol** (`IProtocol`, default: `ChannelProtocol`) — framing: splits application data into transport blocks and reassembles them.
4. **RPC Handler** (`Infrastructure/RpcHandler.cs`) — drives the request/response loop, tracks concurrent requests, applies a 15s default timeout, and handles `CancellationToken` forwarding.
5. **Adapter/Proxy** (`Infrastructure/InterfaceAdapter.cs`, `InterfaceProxy.cs`) — server-side adapters deserialize incoming packets and call the real implementation; client-side proxies implement the interface by serializing calls into packets.
6. **Hosting** — `ServiceHost` / `ServiceHostAsync` accept connections via `IAsyncChannelAcceptor` and route each session to service instances. `RpcClient` is the client-side counterpart with auto-reconnection.

### Key Abstractions

| Type | Role |
|---|---|
| `IChannel` | Transport: open/close/send/receive + lifecycle events |
| `IProtocol` | Framing: application↔transport block conversion |
| `ISession` | Per-connection identity, property store, `ISession.Current` (AsyncLocal) |
| `IAsyncChannelAcceptor` | Server: `AcceptAsync` loop producing new `IChannel` instances |
| `IAsyncRunnable` | Hosting: `RunAsync(CancellationToken)` main loop |
| `ShareWithin` (enum) | Instance lifetime: None, Session, Host, or Process |

### Packet System

`Infrastructure/Packets/` contains 16 packet types for methods (`MethodCallPacket`, `MethodResultPacket`), properties (`GetPropertyPacket`, `SetPropertyPacket`, `PropertyValuePacket`), events (`SubscribeForEventPacket`, `UnsubscribeFromEventPacket`, `EventDataPacket`), lifecycle (`GetInstancePacket`, `ReleaseInstancePacket`), and control flow (`CancelRequestPacket`, `ErrorPacket`, `ExceptionPacket`, `SuccessPacket`, `ServiceInstancePacket`). Default serialization is via `RpcSerializationContext` backed by `Sigurn.Serialize`.

### Source Generator

`RpcGenerator.cs` is an `IIncrementalGenerator` that detects `[RemoteInterface]` attributes and emits sealed `<InterfaceName>_Adapter` and `<InterfaceName>_Proxy` classes into `<Namespace>.Rpc.Infrastructure`. Verify snapshot tests in `Sigurn.Rpc.Tests/` use `.verified.cs` files to assert generated output.

The generated members carry tracing: each dispatch branch and each proxy member is wrapped into `TraceEnter` / `TraceExit` / `TraceFailure` calls (declared on `InterfaceAdapter` and `InterfaceProxy`) that log the full interface and member name alongside the numeric member id. Every call site is guarded by `IsTraceEnabled`, so nothing is computed when trace logging is off. `Sigurn.Rpc.Tests` does not run the generator (it registers hand-written adapters/proxies for the same interfaces); generated code is compiled and exercised by `Sigurn.Rpc.TestProcess` and `Sigurn.Rpc.IntegrationTests`.

### Integration Tests

`Sigurn.Rpc.IntegrationTests` depends on the library as a NuGet package (not a project reference) to verify the packaged artifact. Build the library in Release before running integration tests; the project is configured to use a local NuGet cache from the artifacts directory.

## Project Targets

- Main library: `net10.0`
- Source generator: `netstandard2.0`
- Tests: `net10.0`

## Versioning

MinVer drives semantic versioning from git tags. Pre-release builds use the `dev` identifier.
