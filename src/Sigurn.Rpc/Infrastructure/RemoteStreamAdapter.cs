namespace Sigurn.Rpc.Infrastructure;

/// <summary>
/// Server-side adapter that dispatches <see cref="IRemoteStream"/> calls to a local implementation.
/// Hand-written (the library does not run the RPC source generator on its own code) and kept in sync
/// with <see cref="RemoteStreamProxy"/> by method id.
/// </summary>
sealed class RemoteStreamAdapter : InterfaceAdapter
{
    private static readonly string[] _methodNames =
    [
        "GetInfoAsync(System.Threading.CancellationToken)",
        "ReadAsync(int, System.Threading.CancellationToken)",
        "WriteAsync(byte[], System.Threading.CancellationToken)",
        "SeekAsync(long, System.IO.SeekOrigin, System.Threading.CancellationToken)",
        "SetLengthAsync(long, System.Threading.CancellationToken)",
        "FlushAsync(System.Threading.CancellationToken)",
        "GetPositionAsync(System.Threading.CancellationToken)",
        "SetPositionAsync(long, System.Threading.CancellationToken)",
        "GetLengthAsync(System.Threading.CancellationToken)",
    ];

    private readonly IRemoteStream _instance;

    public RemoteStreamAdapter(IRemoteStream instance)
        : base(typeof(IRemoteStream), instance)
    {
        _instance = instance ?? throw new ArgumentNullException(nameof(instance));
    }

    protected internal override string? GetMemberName(RpcTraceOperation operation, int memberId)
    {
        if (operation != RpcTraceOperation.MethodCall) return null;
        if (memberId < 0 || memberId >= _methodNames.Length) return null;

        return _methodNames[memberId];
    }

    public override async Task<(byte[]? Result, IReadOnlyList<byte[]>? Args)> InvokeMethodAsync(int methodId, IReadOnlyList<byte[]>? args, bool oneWay, CancellationToken cancellationToken)
    {
        switch (methodId)
        {
            case 0: // GetInfoAsync()
            {
                EnsureArgCount(args, 0);
                var info = await _instance.GetInfoAsync(cancellationToken).ConfigureAwait(false);
                return (await ToBytesAsync(info, cancellationToken).ConfigureAwait(false), null);
            }
            case 1: // ReadAsync(int count)
            {
                EnsureArgCount(args, 1);
                var count = await FromBytesAsync<int>(args![0], cancellationToken).ConfigureAwait(false);
                var data = await _instance.ReadAsync(count, cancellationToken).ConfigureAwait(false);
                return (await ToBytesAsync(data, cancellationToken).ConfigureAwait(false), null);
            }
            case 2: // WriteAsync(byte[] data)
            {
                EnsureArgCount(args, 1);
                var data = await FromBytesAsync<byte[]>(args![0], cancellationToken).ConfigureAwait(false) ?? [];
                await _instance.WriteAsync(data, cancellationToken).ConfigureAwait(false);
                return (null, null);
            }
            case 3: // SeekAsync(long offset, SeekOrigin origin)
            {
                EnsureArgCount(args, 2);
                var offset = await FromBytesAsync<long>(args![0], cancellationToken).ConfigureAwait(false);
                var origin = await FromBytesAsync<SeekOrigin>(args![1], cancellationToken).ConfigureAwait(false);
                var position = await _instance.SeekAsync(offset, origin, cancellationToken).ConfigureAwait(false);
                return (await ToBytesAsync(position, cancellationToken).ConfigureAwait(false), null);
            }
            case 4: // SetLengthAsync(long value)
            {
                EnsureArgCount(args, 1);
                var value = await FromBytesAsync<long>(args![0], cancellationToken).ConfigureAwait(false);
                await _instance.SetLengthAsync(value, cancellationToken).ConfigureAwait(false);
                return (null, null);
            }
            case 5: // FlushAsync()
            {
                EnsureArgCount(args, 0);
                await _instance.FlushAsync(cancellationToken).ConfigureAwait(false);
                return (null, null);
            }
            case 6: // GetPositionAsync()
            {
                EnsureArgCount(args, 0);
                var position = await _instance.GetPositionAsync(cancellationToken).ConfigureAwait(false);
                return (await ToBytesAsync(position, cancellationToken).ConfigureAwait(false), null);
            }
            case 7: // SetPositionAsync(long value)
            {
                EnsureArgCount(args, 1);
                var value = await FromBytesAsync<long>(args![0], cancellationToken).ConfigureAwait(false);
                await _instance.SetPositionAsync(value, cancellationToken).ConfigureAwait(false);
                return (null, null);
            }
            case 8: // GetLengthAsync()
            {
                EnsureArgCount(args, 0);
                var length = await _instance.GetLengthAsync(cancellationToken).ConfigureAwait(false);
                return (await ToBytesAsync(length, cancellationToken).ConfigureAwait(false), null);
            }
        }

        return await base.InvokeMethodAsync(methodId, args, oneWay, cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureArgCount(IReadOnlyList<byte[]>? args, int expected)
    {
        if ((args?.Count ?? 0) != expected)
            throw new InvalidOperationException("Invalid number of arguments");
    }
}
