using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sigurn.Rpc;

/// <summary>
/// Provides logging configuration and logger creation for the RPC library.
/// </summary>
public static class RpcLogging
{
    private static readonly Lock _lock = new Lock();
    private static ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

    // Bumped on every Configure call. Loggers handed out earlier compare their cached version
    // against this one and re-resolve when it moved, so configuring logging works no matter how
    // many types already captured their logger in a static field.
    private static int _version;

    /// <summary>
    /// Configures the logger factory used by the RPC library.
    /// </summary>
    /// <param name="loggerFactory">The logger factory to use.</param>
    public static void Configure(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        lock (_lock)
        {
            _loggerFactory = loggerFactory;
            _version++;
        }
    }

    private static (ILoggerFactory Factory, int Version) Current
    {
        get
        {
            lock (_lock)
                return (_loggerFactory, _version);
        }
    }

    /// <summary>
    /// Creates a logger for the specified type using the configured logger factory.
    /// </summary>
    /// <typeparam name="T">The type to create the logger for.</typeparam>
    /// <returns>An <see cref="ILogger{T}"/> instance.</returns>
    /// <remarks>
    /// The returned logger resolves the underlying logger lazily, so a logger stored in a static
    /// field keeps following <see cref="Configure(ILoggerFactory)"/> calls made later.
    /// </remarks>
    public static ILogger<T> CreateLogger<T>()
    {
        return new ForwardingLogger<T>();
    }

    /// <summary>
    /// Creates a logging scope that traces method entry and exit at the Trace level.
    /// </summary>
    /// <typeparam name="T">The type owning the logger.</typeparam>
    /// <param name="logger">The logger to write trace messages to.</param>
    /// <param name="scopeName">The scope name, defaulting to the calling member name.</param>
    /// <returns>A disposable that logs the scope exit when disposed.</returns>
    public static IDisposable Scope<T>(this ILogger<T> logger, [CallerMemberName]string scopeName = "")
    {
        return Scope((ILogger)logger, scopeName);
    }

    /// <summary>
    /// Creates a logging scope that traces method entry and exit at the Trace level.
    /// </summary>
    /// <param name="logger">The logger to write trace messages to.</param>
    /// <param name="scopeName">The scope name, defaulting to the calling member name.</param>
    /// <returns>A disposable that logs the scope exit when disposed.</returns>
    public static IDisposable Scope(this ILogger logger, [CallerMemberName]string scopeName = "")
    {
        // Nothing is written and nothing is allocated when the level is off.
        if (!logger.IsEnabled(LogLevel.Trace)) return NoopDisposable.Instance;

        logger.LogTrace("==> {Scope}", scopeName);
        return Disposable.Create( () => logger.LogTrace("<== {Scope}", scopeName));
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly IDisposable Instance = new NoopDisposable();

        private NoopDisposable() { }

        public void Dispose() { }
    }

    // Resolves the real logger on demand and caches it until the factory is replaced.
    private sealed class ForwardingLogger<T> : ILogger<T>
    {
        // Version and logger are kept in a single reference so a reader never pairs a fresh version
        // with a stale logger; a lost race only costs one redundant CreateLogger call.
        private sealed record Resolved(int Version, ILogger Logger);

        private Resolved? _resolved;

        private ILogger Logger
        {
            get
            {
                var (factory, version) = Current;

                var resolved = _resolved;
                if (resolved is not null && resolved.Version == version) return resolved.Logger;

                resolved = new Resolved(version, factory.CreateLogger<T>());
                _resolved = resolved;

                return resolved.Logger;
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => Logger.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => Logger.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Logger.Log(logLevel, eventId, state, exception, formatter);
    }
}
