namespace Sigurn.Rpc.Infrastructure;

/// <summary>
/// Client-side proxy that forwards <see cref="IRemoteStream"/> calls to the remote implementation.
/// Hand-written (the library does not run the RPC source generator on its own code) and kept in sync
/// with <see cref="RemoteStreamAdapter"/> by method id.
/// </summary>
sealed class RemoteStreamProxy : InterfaceProxy, IRemoteStream
{
    private const string @__method_0 = "GetInfoAsync(System.Threading.CancellationToken)";
    private const string @__method_1 = "ReadAsync(int, System.Threading.CancellationToken)";
    private const string @__method_2 = "WriteAsync(byte[], System.Threading.CancellationToken)";
    private const string @__method_3 = "SeekAsync(long, System.IO.SeekOrigin, System.Threading.CancellationToken)";
    private const string @__method_4 = "SetLengthAsync(long, System.Threading.CancellationToken)";
    private const string @__method_5 = "FlushAsync(System.Threading.CancellationToken)";
    private const string @__method_6 = "GetPositionAsync(System.Threading.CancellationToken)";
    private const string @__method_7 = "SetPositionAsync(long, System.Threading.CancellationToken)";
    private const string @__method_8 = "GetLengthAsync(System.Threading.CancellationToken)";

    public RemoteStreamProxy(Guid instanceId)
        : base(instanceId)
    {
    }

    public async Task<RemoteStreamInfo> GetInfoAsync(CancellationToken cancellationToken)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_0, 0);
        try
        {
            var (res, _) = await InvokeMethodAsync(0, [], false, cancellationToken).ConfigureAwait(false);
            return await FromBytesAsync<RemoteStreamInfo>(res, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Server returned null instead of result.");
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_0, 0))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_0, 0);
        }
    }

    public async Task<byte[]> ReadAsync(int count, CancellationToken cancellationToken)
    {
        using var _noTimeout = new RpcContext { Timeout = Timeout.InfiniteTimeSpan };

        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_1, 1);
        try
        {
            var (res, _) = await InvokeMethodAsync(1, [ToBytes(count)], false, cancellationToken).ConfigureAwait(false);
            return await FromBytesAsync<byte[]>(res, cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_1, 1))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_1, 1);
        }
    }

    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var _noTimeout = new RpcContext { Timeout = Timeout.InfiniteTimeSpan };

        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_2, 2);
        try
        {
            await InvokeMethodAsync(2, [ToBytes(data)], false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_2, 2))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_2, 2);
        }
    }

    public async Task<long> SeekAsync(long offset, SeekOrigin origin, CancellationToken cancellationToken)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_3, 3);
        try
        {
            var (res, _) = await InvokeMethodAsync(3, [ToBytes(offset), ToBytes(origin)], false, cancellationToken).ConfigureAwait(false);
            return await FromBytesAsync<long>(res, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_3, 3))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_3, 3);
        }
    }

    public async Task SetLengthAsync(long value, CancellationToken cancellationToken)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_4, 4);
        try
        {
            await InvokeMethodAsync(4, [ToBytes(value)], false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_4, 4))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_4, 4);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_5, 5);
        try
        {
            await InvokeMethodAsync(5, [], false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_5, 5))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_5, 5);
        }
    }

    public async Task<long> GetPositionAsync(CancellationToken cancellationToken)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_6, 6);
        try
        {
            var (res, _) = await InvokeMethodAsync(6, [], false, cancellationToken).ConfigureAwait(false);
            return await FromBytesAsync<long>(res, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_6, 6))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_6, 6);
        }
    }

    public async Task SetPositionAsync(long value, CancellationToken cancellationToken)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_7, 7);
        try
        {
            await InvokeMethodAsync(7, [ToBytes(value)], false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_7, 7))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_7, 7);
        }
    }

    public async Task<long> GetLengthAsync(CancellationToken cancellationToken)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_8, 8);
        try
        {
            var (res, _) = await InvokeMethodAsync(8, [], false, cancellationToken).ConfigureAwait(false);
            return await FromBytesAsync<long>(res, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_8, 8))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_8, 8);
        }
    }
}
