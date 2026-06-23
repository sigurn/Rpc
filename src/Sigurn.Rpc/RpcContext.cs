namespace Sigurn.Rpc;

/// <summary>
/// Provides an ambient context for controlling RPC call behavior within the current async execution context.
/// </summary>
public sealed class RpcContext : IDisposable
{
    private static readonly AsyncLocal<RpcContext?> _current = new();
    private readonly RpcContext? _previous;
    private bool _disposed;

    /// <summary>
    /// Initializes a new <see cref="RpcContext"/> and activates it as the current context.
    /// </summary>
    public RpcContext()
    {
        _previous = _current.Value;
        _current.Value = this;
    }

    /// <summary>
    /// Gets the active <see cref="RpcContext"/> for the current async execution context, or <c>null</c> if none is active.
    /// </summary>
    public static RpcContext? Current => _current.Value;

    /// <summary>
    /// Gets or sets the timeout for RPC calls made within this context.
    /// <c>null</c> defers to <see cref="Sigurn.Rpc.Infrastructure.RpcHandler.AnswerTimeout"/>.
    /// Use <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to disable the timeout entirely.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Deactivates this context and restores the previously active context.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _current.Value = _previous;
        }
    }
}
