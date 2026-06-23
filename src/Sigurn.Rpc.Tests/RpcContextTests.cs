namespace Sigurn.Rpc.Tests;

public class RpcContextTests
{
    [Fact]
    public void CurrentIsNullByDefault()
    {
        Assert.Null(RpcContext.Current);
    }

    [Fact]
    public void SetsCurrentOnCreation()
    {
        using var ctx = new RpcContext();
        Assert.Same(ctx, RpcContext.Current);
    }

    [Fact]
    public void RestoresPreviousOnDispose()
    {
        Assert.Null(RpcContext.Current);
        using (var ctx = new RpcContext())
        {
            Assert.Same(ctx, RpcContext.Current);
        }
        Assert.Null(RpcContext.Current);
    }

    [Fact]
    public void NestedContextsChainCorrectly()
    {
        using var outer = new RpcContext { Timeout = TimeSpan.FromSeconds(10) };
        Assert.Same(outer, RpcContext.Current);

        using (var inner = new RpcContext { Timeout = TimeSpan.FromSeconds(5) })
        {
            Assert.Same(inner, RpcContext.Current);
            Assert.Equal(TimeSpan.FromSeconds(5), RpcContext.Current?.Timeout);
        }

        Assert.Same(outer, RpcContext.Current);
        Assert.Equal(TimeSpan.FromSeconds(10), RpcContext.Current?.Timeout);
    }

    [Fact]
    public void TimeoutPropertyIsReadable()
    {
        using var ctx = new RpcContext { Timeout = TimeSpan.FromSeconds(42) };
        Assert.Equal(TimeSpan.FromSeconds(42), RpcContext.Current?.Timeout);
    }

    [Fact]
    public void NullTimeoutMeansUseDefault()
    {
        using var ctx = new RpcContext();
        Assert.Null(RpcContext.Current?.Timeout);
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var ctx = new RpcContext();
        ctx.Dispose();
        ctx.Dispose();
        Assert.Null(RpcContext.Current);
    }

    [Fact]
    public async Task FlowsThroughAsyncContext()
    {
        using var ctx = new RpcContext { Timeout = TimeSpan.FromSeconds(42) };

        await Task.Yield();

        Assert.Same(ctx, RpcContext.Current);
        Assert.Equal(TimeSpan.FromSeconds(42), RpcContext.Current?.Timeout);
    }

    [Fact]
    public async Task DoesNotLeakAcrossAsyncBranches()
    {
        RpcContext? captured = null;

        var task = Task.Run(() =>
        {
            captured = RpcContext.Current;
        });

        using var ctx = new RpcContext { Timeout = TimeSpan.FromSeconds(1) };
        await task;

        Assert.Null(captured);
    }
}
