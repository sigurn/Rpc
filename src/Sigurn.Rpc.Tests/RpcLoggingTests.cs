using Microsoft.Extensions.Logging;

namespace Sigurn.Rpc.Tests;

[Collection("RpcLogging")]
public class RpcLoggingTests
{
    // Every logger in the library lives in a `static readonly` field initialized on first touch of
    // its type. Configuring the factory afterwards must still take effect, otherwise a host that
    // sets logging up after the first RPC type is loaded is stuck with NullLogger forever.
    [Fact]
    public void Configure_AffectsLoggerCreatedBeforeIt()
    {
        var logger = RpcLogging.CreateLogger<RpcLoggingTests>();

        var factory = new CapturingLoggerFactory();
        using (new RpcLoggingScope(factory))
            logger.LogTrace("configured {Value}", 42);

        var records = factory.RecordsOf<RpcLoggingTests>();
        Assert.Contains(records, x => x.Level == LogLevel.Trace && x.HasField("Value", 42));
    }

    [Fact]
    public void Logger_StopsWriting_WhenFactoryIsReset()
    {
        var factory = new CapturingLoggerFactory();
        ILogger<RpcLoggingTests> logger;

        using (new RpcLoggingScope(factory))
        {
            logger = RpcLogging.CreateLogger<RpcLoggingTests>();
            logger.LogTrace("while configured");
        }

        factory.Clear();
        logger.LogTrace("after reset");

        Assert.Empty(factory.RecordsOf<RpcLoggingTests>());
    }

    [Fact]
    public void IsEnabled_FollowsTheConfiguredLevel()
    {
        var factory = new CapturingLoggerFactory { MinLevel = LogLevel.Information };
        using var scope = new RpcLoggingScope(factory);

        var logger = RpcLogging.CreateLogger<RpcLoggingTests>();

        Assert.False(logger.IsEnabled(LogLevel.Trace));
        Assert.True(logger.IsEnabled(LogLevel.Information));
    }

    [Fact]
    public void Scope_WritesNothing_WhenTraceIsDisabled()
    {
        var factory = new CapturingLoggerFactory { MinLevel = LogLevel.Information };
        using var scope = new RpcLoggingScope(factory);

        var logger = RpcLogging.CreateLogger<RpcLoggingTests>();
        using (logger.Scope("SomeScope")) { }

        Assert.Empty(factory.RecordsOf<RpcLoggingTests>());
    }

    [Fact]
    public void Scope_WritesEnterAndExit_WhenTraceIsEnabled()
    {
        var factory = new CapturingLoggerFactory();
        using var scope = new RpcLoggingScope(factory);

        var logger = RpcLogging.CreateLogger<RpcLoggingTests>();
        using (logger.Scope("SomeScope")) { }

        var records = factory.RecordsOf<RpcLoggingTests>();
        Assert.Contains(records, x => x.Message.StartsWith("==>") && x.HasField("Scope", "SomeScope"));
        Assert.Contains(records, x => x.Message.StartsWith("<==") && x.HasField("Scope", "SomeScope"));
    }
}
