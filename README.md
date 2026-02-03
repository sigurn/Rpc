# Sigurn.Rpc

A .NET library for inter-process communication (IPC/RPC) using native .NET interfaces and binary serialization.

## Overview

Sigurn.Rpc is designed with simplicity in mind, allowing you to use standard .NET interfaces for remote procedure calls without code generation or complex configuration. The library makes remote service calls feel just like local method invocations.

### Key Features

- **Interface-based**: Use standard .NET interfaces with attributes
- **Transparent remoting**: Remote calls look like local method calls
- **Multiple transports**: TCP, SSL/TLS, process stdin/stdout
- **Lifetime management**: Flexible object creation strategies (ShareWithin scopes)
- **Channel chains**: Compose channels for encryption, compression, logging, etc.
- **Full async support**: Built on async/await from the ground up
- **Events and properties**: Remote events and properties just work
- **Session management**: Track and manage client sessions with custom state
- **Service discovery**: Query available services at runtime

### Why Sigurn.Rpc?

- **No code generation**: Unlike gRPC or WCF, no proto files or service references needed
- **Type-safe**: Compile-time safety with standard C# interfaces
- **Flexible architecture**: Build custom transports and middleware channels
- **Modern .NET**: Designed for .NET 10+ with latest C# features

## Getting Started

### Installation

```bash
dotnet add package Sigurn.Rpc
```

### Hello World Example

This example demonstrates a complete RPC setup with three projects: contracts, server, and client.

#### 1. Create the Contracts Assembly

The contracts assembly contains service interface definitions shared by both client and server.

**HelloWorld.Contracts/HelloWorld.Contracts.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Sigurn.Rpc" Version="1.0.0" />
  </ItemGroup>
</Project>
```

**HelloWorld.Contracts/IHelloService.cs:**
```csharp
using Sigurn.Rpc;

namespace HelloWorld.Contracts;

/// <summary>
/// Mark the interface with [RemoteInterface] attribute
/// </summary>
[RemoteInterface]
public interface IHelloService
{
    // Synchronous methods
    string SayHello(string name);
    int Add(int a, int b);

    // Async methods with CancellationToken
    Task<string> SayHelloAsync(string name, CancellationToken cancellationToken);

    // Properties are supported
    string Greeting { get; set; }

    // Events are supported
    event EventHandler<MessageEventArgs> MessageReceived;
}

public class MessageEventArgs : EventArgs
{
    public string Message { get; set; } = string.Empty;
}
```

#### 2. Create the Server Assembly

The server implements the service interfaces and hosts them.

**HelloWorld.Server/HelloWorld.Server.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\HelloWorld.Contracts\HelloWorld.Contracts.csproj" />
  </ItemGroup>
</Project>
```

**HelloWorld.Server/HelloService.cs:**
```csharp
using HelloWorld.Contracts;
using Sigurn.Rpc;

namespace HelloWorld.Server;

/// <summary>
/// Mark the implementation with [RemoteService] attribute
/// </summary>
[RemoteService]
public class HelloService : IHelloService
{
    private string _greeting = "Hello";

    public string SayHello(string name)
    {
        var message = $"{_greeting}, {name}!";

        // Raise events to notify clients
        OnMessageReceived(new MessageEventArgs { Message = message });

        return message;
    }

    public int Add(int a, int b)
    {
        return a + b;
    }

    public Task<string> SayHelloAsync(string name, CancellationToken cancellationToken)
    {
        return Task.FromResult(SayHello(name));
    }

    public string Greeting
    {
        get => _greeting;
        set => _greeting = value;
    }

    public event EventHandler<MessageEventArgs>? MessageReceived;

    protected virtual void OnMessageReceived(MessageEventArgs e)
    {
        MessageReceived?.Invoke(this, e);
    }
}
```

**HelloWorld.Server/Program.cs:**
```csharp
using HelloWorld.Contracts;
using Sigurn.Rpc;
using System.Net;

namespace HelloWorld.Server;

class Program
{
    static void Main(string[] args)
    {
        // Create a TCP host listening on port 5000
        var tcpHost = new TcpHost
        {
            EndPoint = new IPEndPoint(IPAddress.Loopback, 5000)
        };

        // Create the service host
        var host = new ServiceHost(tcpHost);

        // Register the service with Session scope
        // A new instance is created per client session
        host.RegisterSerive<IHelloService>(
            ShareWithin.Session,
            () => new HelloService()
        );

        // Enable service catalog for service discovery
        host.PublishServicesCatalog = true;

        // Start the server
        host.Start();
        Console.WriteLine("Server started on port 5000");
        Console.WriteLine("Press Enter to stop...");
        Console.ReadLine();

        // Stop and cleanup
        host.Stop();
    }
}
```

**Alternative: Process Host (stdin/stdout)**

For inter-process communication via stdin/stdout:

```csharp
static void Main(string[] args)
{
    // Use ProcessHost instead of TcpHost
    var host = new ServiceHost(new ProcessHost());

    host.RegisterSerive<IHelloService>(
        ShareWithin.Session,
        () => new HelloService()
    );

    host.PublishServicesCatalog = true;
    host.Start();

    // Wait for termination
    using var stopEvent = new ManualResetEvent(false);
    Console.CancelKeyPress += (s, a) =>
    {
        stopEvent.Set();
        a.Cancel = true;
    };
    stopEvent.WaitOne();

    host.Stop();
}
```

#### 3. Create the Client Assembly

The client connects to the server and uses service proxies.

**HelloWorld.Client/HelloWorld.Client.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\HelloWorld.Contracts\HelloWorld.Contracts.csproj" />
  </ItemGroup>
</Project>
```

**HelloWorld.Client/Program.cs:**
```csharp
using HelloWorld.Contracts;
using Sigurn.Rpc;
using System.Net;

namespace HelloWorld.Client;

class Program
{
    static async Task Main(string[] args)
    {
        // Create RPC client with a channel factory
        var client = new RpcClient(
            async ct =>
            {
                var endPoint = new IPEndPoint(IPAddress.Loopback, 5000);
                var channel = new TcpChannel(endPoint);
                await channel.OpenAsync(ct);
                return channel;
            }
        );

        // Enable auto-reconnect on connection failures
        client.AutoReopen = true;
        client.ReopenInterval = TimeSpan.FromSeconds(5);

        try
        {
            // Connect to the server
            await client.OpenAsync(CancellationToken.None);
            Console.WriteLine("Connected to server");

            // Get the service proxy
            var helloService = await client.GetService<IHelloService>(
                CancellationToken.None
            );

            // Subscribe to events
            helloService.MessageReceived += (sender, e) =>
            {
                Console.WriteLine($"[Event] {e.Message}");
            };

            // Call methods just like local objects
            var greeting = helloService.SayHello("World");
            Console.WriteLine(greeting);

            var sum = helloService.Add(2, 3);
            Console.WriteLine($"2 + 3 = {sum}");

            // Work with properties
            helloService.Greeting = "Hi";
            Console.WriteLine(helloService.SayHello("Alice"));

            // Async calls
            var asyncResult = await helloService.SayHelloAsync(
                "Bob",
                CancellationToken.None
            );
            Console.WriteLine(asyncResult);

            Console.WriteLine("\nPress Enter to exit...");
            Console.ReadLine();
        }
        finally
        {
            await client.CloseAsync(CancellationToken.None);
        }
    }
}
```

**Alternative: Process Channel Client**

For launching a server process and communicating via stdin/stdout:

```csharp
static async Task Main(string[] args)
{
    var client = new RpcClient(
        async ct =>
        {
            // Launch server process and communicate via stdin/stdout
            var channel = new ProcessChannel(
                "HelloWorld.Server.exe"  // Path to server executable
            );
            await channel.OpenAsync(ct);
            return channel;
        }
    );

    try
    {
        await client.OpenAsync(CancellationToken.None);

        var helloService = await client.GetService<IHelloService>(
            CancellationToken.None
        );

        Console.WriteLine(helloService.SayHello("World"));
    }
    finally
    {
        await client.CloseAsync(CancellationToken.None);
    }
}
```

### Running the Example

1. Build all projects:
   ```bash
   dotnet build
   ```

2. Start the server:
   ```bash
   cd HelloWorld.Server
   dotnet run
   ```

3. In another terminal, run the client:
   ```bash
   cd HelloWorld.Client
   dotnet run
   ```

## Advanced Topics

### Object Lifetime Management (ShareWithin)

The library provides flexible lifetime scopes for service instances:

```csharp
public enum ShareWithin
{
    None,      // New instance per call (Transient)
    Session,   // One instance per client session (Scoped)
    Host,      // One instance per host, shared across all sessions (Singleton)
    Process,   // Global singleton for the entire process
}
```

**Examples:**

```csharp
// Transient: New instance for each call
host.RegisterSerive<ICalculator>(
    ShareWithin.None,
    () => new Calculator()
);

// Scoped: One instance per client session
host.RegisterSerive<IUserSession>(
    ShareWithin.Session,
    () => new UserSession()
);

// Singleton per host: Shared across all clients connected to this host
host.RegisterSerive<IConfigService>(
    ShareWithin.Host,
    () => new ConfigService()
);

// Global singleton: Shared across the entire process
host.RegisterSerive<ILogService>(
    ShareWithin.Process,
    () => LogService.Instance
);
```

### Session Management

#### Server-Side Session Access

Services can access the current session via `ISession.Current`:

```csharp
[RemoteService]
public class AuthService : IAuthService
{
    public bool Login(string username, string password)
    {
        if (ValidateCredentials(username, password))
        {
            // Store data in the session
            var session = ISession.Current;
            if (session != null)
            {
                session.SetProperty(SessionKeys.Username, username);
                session.SetProperty(SessionKeys.LoginTime, DateTime.Now);
            }
            return true;
        }
        return false;
    }

    public string GetCurrentUser()
    {
        var session = ISession.Current;
        if (session?.TryGetProperty(SessionKeys.Username, out var username) == true)
        {
            return username?.ToString() ?? "Anonymous";
        }
        return "Anonymous";
    }
}

enum SessionKeys
{
    Username,
    LoginTime,
    Permissions
}
```

#### Session Events

Implement `ISessionsAware` to track session lifecycle:

```csharp
[RemoteService]
public class ConnectionMonitor : IConnectionMonitor, ISessionsAware
{
    private readonly List<ISession> _sessions = new();

    public void OnSessionOpened(ISession session)
    {
        lock (_sessions)
        {
            _sessions.Add(session);
            Console.WriteLine($"Client connected: {session.Id}");
        }
    }

    public void OnSessionClosed(ISession session)
    {
        lock (_sessions)
        {
            _sessions.Remove(session);
            Console.WriteLine($"Client disconnected: {session.Id}");
        }
    }

    public int GetActiveConnections()
    {
        lock (_sessions)
            return _sessions.Count;
    }
}
```

### Channel Chains and Middleware

Build processing pipelines by chaining channels together.

#### Encryption (AES)

```csharp
using Sigurn.Rpc.Channels;

// Server with AES encryption
var host = new ServiceHost(
    new TcpHost { EndPoint = new IPEndPoint(IPAddress.Any, 5000) },
    channelFactory: baseChannel =>
    {
        var aesChannel = new AesChannel(baseChannel);
        aesChannel.SetKey(myAesKey, myAesIV);
        return aesChannel;
    }
);

// Client with AES encryption
var client = new RpcClient(async ct =>
{
    var tcpChannel = new TcpChannel(
        new IPEndPoint(IPAddress.Loopback, 5000)
    );
    await tcpChannel.OpenAsync(ct);

    var aesChannel = new AesChannel(tcpChannel);
    aesChannel.SetKey(myAesKey, myAesIV);

    return aesChannel;
});
```

#### Compression (GZip)

```csharp
using Sigurn.Rpc.Channels;

// Server with compression
var host = new ServiceHost(
    new TcpHost(),
    channelFactory: baseChannel => new GZipChannel(baseChannel)
);

// Client with compression
var client = new RpcClient(async ct =>
{
    var tcpChannel = new TcpChannel(endPoint);
    await tcpChannel.OpenAsync(ct);
    return new GZipChannel(tcpChannel);
});
```

#### Multiple Channel Layers

Combine multiple channels in a processing pipeline:

```csharp
// Chain: TCP -> Compression -> Encryption -> Queue
var host = new ServiceHost(
    new TcpHost(),
    channelFactory: baseChannel =>
    {
        var gzipChannel = new GZipChannel(baseChannel);
        var aesChannel = new AesChannel(gzipChannel);
        aesChannel.SetKey(key, iv);
        var queueChannel = new QueueChannel(aesChannel);
        return queueChannel;
    }
);
```

### SSL/TLS Support

Use `SslChannel` and `SslHost` for secure connections:

```csharp
using System.Security.Cryptography.X509Certificates;

// Server with SSL
var certificate = new X509Certificate2("server.pfx", "password");
var host = new ServiceHost(new SslHost(certificate)
{
    EndPoint = new IPEndPoint(IPAddress.Any, 5001)
});

// Client with SSL
var client = new RpcClient(async ct =>
{
    var channel = new SslChannel(
        new IPEndPoint(IPAddress.Loopback, 5001),
        "server.example.com"  // Server name for certificate validation
    );
    await channel.OpenAsync(ct);
    return channel;
});
```

### Creating Custom Channels

#### Custom Transport Channel

Inherit from `BaseChannel` to create a custom transport:

```csharp
public class MyCustomChannel : BaseChannel
{
    protected override Task InternalOpenAsync(CancellationToken cancellationToken)
    {
        // Open connection logic
        return Task.CompletedTask;
    }

    protected override Task InternalCloseAsync(CancellationToken cancellationToken)
    {
        // Close connection logic
        return Task.CompletedTask;
    }

    protected override async Task<IPacket> InternalReceiveAsync(
        CancellationToken cancellationToken)
    {
        // Receive data from transport
        byte[] data = await ReceiveDataFromTransport();
        return IPacket.Create(data);
    }

    protected override async Task<IPacket> InternalSendAsync(
        IPacket packet,
        CancellationToken cancellationToken)
    {
        // Send data to transport
        await SendDataToTransport(packet.Data);
        return packet;
    }
}
```

#### Custom Processing Channel (Middleware)

Inherit from `ProcessionChannel` to create middleware that processes packets:

```csharp
public class LoggingChannel : ProcessionChannel
{
    public LoggingChannel(IChannel baseChannel) : base(baseChannel)
    {
    }

    protected override Task<IPacket> ProcessReceivedPacket(
        IPacket packet,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"<< Received {packet.Data.Length} bytes");
        return Task.FromResult(packet);
    }

    protected override Task<IPacket> ProcessSendingPacket(
        IPacket packet,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($">> Sending {packet.Data.Length} bytes");
        return Task.FromResult(packet);
    }
}

// Usage
var host = new ServiceHost(
    new TcpHost(),
    channelFactory: baseChannel => new LoggingChannel(baseChannel)
);
```

### Channel Factories and Failover

`RestorableChannel` (used internally by `RpcClient`) supports multiple factories for automatic failover:

```csharp
var client = new RpcClient(
    // Primary server
    async ct =>
    {
        var channel = new TcpChannel(
            new IPEndPoint(IPAddress.Parse("192.168.1.10"), 5000)
        );
        await channel.OpenAsync(ct);
        return channel;
    },
    // Backup server
    async ct =>
    {
        var channel = new TcpChannel(
            new IPEndPoint(IPAddress.Parse("192.168.1.11"), 5000)
        );
        await channel.OpenAsync(ct);
        return channel;
    }
);

// Configure auto-reconnect behavior
client.AutoReopen = true;                           // Enable auto-reconnect
client.ReopenInterval = TimeSpan.FromSeconds(5);    // Retry interval
client.ResetOnSuccess = true;                       // Reset to first factory on success
```

### Service Discovery

Clients can query available services at runtime:

```csharp
var client = new RpcClient(...);
await client.OpenAsync(CancellationToken.None);

// Get the service catalog
var catalog = await client.GetService<IServiceCatalog>(CancellationToken.None);
var services = await catalog.GetServicesAsync(CancellationToken.None);

foreach (var serviceInfo in services)
{
    Console.WriteLine($"Service: {serviceInfo.InterfaceType.Name}");
    Console.WriteLine($"  GUID: {serviceInfo.InterfaceGuid}");
    Console.WriteLine($"  Scope: {serviceInfo.Share}");
}
```

Enable catalog publishing on the server:

```csharp
var host = new ServiceHost(new TcpHost());
host.PublishServicesCatalog = true;  // Important!
host.Start();
```

### Protocol Customization

Customize the binary protocol used for communication:

```csharp
// Custom protocol factory
Func<IProtocol> protocolFactory = () => new ChannelProtocol
{
    // Customize protocol settings if needed
};

// Server with custom protocol
var host = new ServiceHost(
    new TcpHost(protocolFactory)
);

// Client with custom protocol
var client = new RpcClient(async ct =>
{
    var channel = new TcpChannel(endPoint, protocolFactory());
    await channel.OpenAsync(ct);
    return channel;
});
```

## License

This project is licensed under the [MIT License](LICENSE).

