using Microsoft.Extensions.Logging;

namespace Sigurn.Rpc.Tests;

/// <summary>A single captured log entry, with its structured state flattened into a dictionary.</summary>
public sealed record LogRecord(
    string Category,
    LogLevel Level,
    string Message,
    IReadOnlyDictionary<string, object?> State,
    Exception? Exception)
{
    public object? Field(string name) => State.TryGetValue(name, out var value) ? value : null;

    public bool HasField(string name, object? value)
    {
        if (!State.TryGetValue(name, out var actual)) return false;
        if (actual is null) return value is null;
        return Equals(actual, value) || string.Equals(actual.ToString(), value?.ToString(), StringComparison.Ordinal);
    }
}

/// <summary>
/// An <see cref="ILoggerFactory"/> that records everything written through it, so tests can assert
/// on what the RPC runtime logs. <see cref="MinLevel"/> makes it possible to verify that call sites
/// compute nothing when the level is off.
/// </summary>
public sealed class CapturingLoggerFactory : ILoggerFactory
{
    private readonly List<LogRecord> _records = [];

    public LogLevel MinLevel { get; set; } = LogLevel.Trace;

    public IReadOnlyList<LogRecord> Records
    {
        get { lock (_records) return [.. _records]; }
    }

    /// <summary>Records written by the given category only. Other tests may run concurrently.</summary>
    public IReadOnlyList<LogRecord> RecordsOf<T>()
    {
        var category = typeof(T).FullName;
        lock (_records)
            return [.. _records.Where(x => x.Category == category)];
    }

    public void Clear()
    {
        lock (_records) _records.Clear();
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void AddProvider(ILoggerProvider provider) { }

    public void Dispose() { }

    private void Add(LogRecord record)
    {
        lock (_records) _records.Add(record);
    }

    private sealed class CapturingLogger(CapturingLoggerFactory factory, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= factory.MinLevel && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (state is IReadOnlyList<KeyValuePair<string, object?>> pairs)
                foreach (var pair in pairs)
                    fields[pair.Key] = pair.Value;

            factory.Add(new LogRecord(category, logLevel, formatter(state, exception), fields, exception));
        }
    }
}

/// <summary>
/// Swaps the process-wide <see cref="RpcLogging"/> factory for the lifetime of the scope and puts
/// the previous one back afterwards.
/// </summary>
public sealed class RpcLoggingScope : IDisposable
{
    public RpcLoggingScope(CapturingLoggerFactory factory)
    {
        Factory = factory;
        RpcLogging.Configure(factory);
    }

    public CapturingLoggerFactory Factory { get; }

    public void Dispose() => RpcLogging.Configure(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
}

/// <summary>
/// <see cref="RpcLogging"/> is process-wide state, so every test that reconfigures it runs alone.
/// </summary>
[CollectionDefinition("RpcLogging", DisableParallelization = true)]
public class RpcLoggingCollection;
