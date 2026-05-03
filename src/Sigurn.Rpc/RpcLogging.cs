using System.Runtime.CompilerServices;
using System.Xml;
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

    /// <summary>
    /// Configures the logger factory used by the RPC library.
    /// </summary>
    /// <param name="loggerFactory">The logger factory to use.</param>
    public static void Configure(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        LoggerFactory = loggerFactory;
    }

    private static ILoggerFactory LoggerFactory
    {
        get
        {
            lock(_lock)
                return _loggerFactory;
        }

        set
        {
            lock(_lock)
                _loggerFactory = value;
        }
    }

    /// <summary>
    /// Creates a logger for the specified type using the configured logger factory.
    /// </summary>
    /// <typeparam name="T">The type to create the logger for.</typeparam>
    /// <returns>An <see cref="ILogger{T}"/> instance.</returns>
    public static ILogger<T> CreateLogger<T>()
    {
        return LoggerFactory.CreateLogger<T>();
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
        logger.LogTrace("==> {Scope}", scopeName);
        return Disposable.Create( () => logger.LogTrace("<== {Scope}", scopeName));
    }

    /// <summary>
    /// Creates a logging scope that traces method entry and exit at the Trace level.
    /// </summary>
    /// <param name="logger">The logger to write trace messages to.</param>
    /// <param name="scopeName">The scope name, defaulting to the calling member name.</param>
    /// <returns>A disposable that logs the scope exit when disposed.</returns>
    public static IDisposable Scope(this ILogger logger, [CallerMemberName]string scopeName = "")
    {
        logger.LogTrace("==> {Scope}", scopeName);
        return Disposable.Create( () => logger.LogTrace("<== {Scope}", scopeName));
    }
}