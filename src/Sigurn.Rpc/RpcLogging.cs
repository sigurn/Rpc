using System.Runtime.CompilerServices;
using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sigurn.Rpc;

public static class RpcLogging
{
    private static readonly Lock _lock = new Lock();
    private static ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

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

    public static ILogger<T> CreateLogger<T>()
    {
        return LoggerFactory.CreateLogger<T>();
    }

    public static IDisposable Scope<T>(this ILogger<T> logger, [CallerMemberName]string scopeName = "")
    {
        logger.LogTrace("==> {Scope}", scopeName);
        return Disposable.Create( () => logger.LogTrace("<== {Scope}", scopeName));
    }

    public static IDisposable Scope(this ILogger logger, [CallerMemberName]string scopeName = "")
    {
        logger.LogTrace("==> {Scope}", scopeName);
        return Disposable.Create( () => logger.LogTrace("<== {Scope}", scopeName));
    }
}