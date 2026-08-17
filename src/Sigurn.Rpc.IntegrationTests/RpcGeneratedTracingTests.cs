using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sigurn.Rpc.IntegrationTests;

/// <summary>
/// Exercises tracing over generated code: this project consumes the packaged library, so the adapter
/// and proxy under test are the real generated ones.
/// </summary>
public class RpcGeneratedTracingTests
{
    private sealed record Record(string Category, LogLevel Level, string Message, IReadOnlyDictionary<string, object?> State);

    private sealed class CapturingFactory : ILoggerFactory
    {
        private readonly List<Record> _records = new();

        public LogLevel MinLevel { get; set; } = LogLevel.Trace;

        public IReadOnlyList<Record> Records
        {
            get { lock (_records) return _records.ToArray(); }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() { }

        private void Add(Record record)
        {
            lock (_records) _records.Add(record);
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly CapturingFactory _factory;
            private readonly string _category;

            public CapturingLogger(CapturingFactory factory, string category)
            {
                _factory = factory;
                _category = category;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= _factory.MinLevel && logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;

                var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
                if (state is IReadOnlyList<KeyValuePair<string, object?>> pairs)
                    foreach (var pair in pairs)
                        fields[pair.Key] = pair.Value;

                _factory.Add(new Record(_category, logLevel, formatter(state, exception), fields));
            }
        }
    }

    private static bool Has(Record record, string field, string value)
        => record.State.TryGetValue(field, out var actual)
            && string.Equals(actual?.ToString(), value, StringComparison.Ordinal);

    private static bool IsTrace(Record record, string category, string operation, string member, string prefix)
        => record.Category == category
            && record.Level == LogLevel.Trace
            && record.Message.StartsWith(prefix)
            && Has(record, "Operation", operation)
            && Has(record, "Interface", "Sigurn.Rpc.IntegrationTests.ITestService")
            && Has(record, "Member", member);

    // The proxy owns its instance id and traces itself; on the server the session traces, because the
    // generated adapter owns neither the id nor the request.
    private const string ProxyCategory = "Sigurn.Rpc.Infrastructure.InterfaceProxy";
    private const string AdapterCategory = "Sigurn.Rpc.Infrastructure.InterfaceAdapter";
    private const string SessionCategory = "Sigurn.Rpc.Session";

    [Fact]
    public async Task GeneratedCode_IsTracedByMemberName_OnBothSides()
    {
        var factory = new CapturingFactory();
        RpcLogging.Configure(factory);

        try
        {
            using var tcpHost = new TcpHost();
            var serviceHost = new ServiceHost(tcpHost);
            serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService());
            serviceHost.Start();

            var client = new RpcClient(async cancellationToken =>
            {
                var channel = new TcpChannel(tcpHost.EndPoint);
                await channel.OpenAsync(cancellationToken);
                return channel;
            });

            using var _ = client;
            await client.OpenAsync(CancellationToken.None);

            var service = await client.GetService<ITestService>(CancellationToken.None);
            var instanceId = Assert.IsAssignableFrom<Sigurn.Rpc.Infrastructure.InterfaceProxy>(service).InstanceId;

            using ManualResetEvent eventTriggered = new ManualResetEvent(false);
            void Handler(object? sender, EventArgs e) => eventTriggered.Set();

            service.Event1 += Handler;
            service.Prop1 = 5;
            Assert.Equal(5, service.Prop1);
            Assert.Equal("ab", await service.Method4("a", "b", CancellationToken.None));
            service.Method1();
            Assert.True(eventTriggered.WaitOne(TimeSpan.FromSeconds(5)));
            service.Event1 -= Handler;

            var records = factory.Records;

            // Method4 is id 3 in declaration order; the trace must name it, not just the id.
            Assert.Contains(records, x => IsTrace(x, SessionCategory, "MethodCall",
                "Method4(string, string, System.Threading.CancellationToken)", "==>"));
            Assert.Contains(records, x => IsTrace(x, SessionCategory, "MethodCall",
                "Method4(string, string, System.Threading.CancellationToken)", "<=="));

            Assert.Contains(records, x => IsTrace(x, SessionCategory, "PropertySet", "Prop1", "==>"));
            Assert.Contains(records, x => IsTrace(x, SessionCategory, "PropertyGet", "Prop1", "==>"));
            Assert.Contains(records, x => IsTrace(x, SessionCategory, "EventAttach", "Event1", "==>"));
            Assert.Contains(records, x => IsTrace(x, SessionCategory, "EventDetach", "Event1", "==>"));
            Assert.Contains(records, x => IsTrace(x, ProxyCategory, "EventRaise", "Event1", "==>"));

            // The id comes from the request the session is dispatching.
            Assert.Contains(records, x => IsTrace(x, SessionCategory, "MethodCall",
                "Method4(string, string, System.Threading.CancellationToken)", "==>")
                && Has(x, "InstanceId", instanceId.ToString()));

            // An event is fanned out to every subscribed session, so the id belongs to the send.
            Assert.Contains(records, x => x.Message.Contains("Sending event")
                && Has(x, "InstanceId", instanceId.ToString())
                && Has(x, "Interface", "Sigurn.Rpc.IntegrationTests.ITestService")
                && Has(x, "Member", "Event1"));

            // The generated adapter does no logging at all.
            Assert.DoesNotContain(records, x => x.Category == AdapterCategory && x.Level == LogLevel.Trace);

            // Instance lifecycle must be identifiable too.
            Assert.Contains(records, x => x.Level == LogLevel.Information
                && x.Message.Contains("Instance registered")
                && Has(x, "Interface", "Sigurn.Rpc.IntegrationTests.ITestService")
                && Has(x, "InstanceType", "Sigurn.Rpc.IntegrationTests.TestService"));
        }
        finally
        {
            RpcLogging.Configure(NullLoggerFactory.Instance);
        }
    }

    [Fact]
    public async Task GeneratedCode_WritesNoTrace_WhenTraceLevelIsDisabled()
    {
        var factory = new CapturingFactory { MinLevel = LogLevel.Information };
        RpcLogging.Configure(factory);

        try
        {
            using var tcpHost = new TcpHost();
            var serviceHost = new ServiceHost(tcpHost);
            serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService());
            serviceHost.Start();

            var client = new RpcClient(async cancellationToken =>
            {
                var channel = new TcpChannel(tcpHost.EndPoint);
                await channel.OpenAsync(cancellationToken);
                return channel;
            });

            using var _ = client;
            await client.OpenAsync(CancellationToken.None);

            var service = await client.GetService<ITestService>(CancellationToken.None);
            service.Prop1 = 5;
            Assert.Equal("ab", await service.Method4("a", "b", CancellationToken.None));

            Assert.DoesNotContain(factory.Records, x => x.Level == LogLevel.Trace);
        }
        finally
        {
            RpcLogging.Configure(NullLoggerFactory.Instance);
        }
    }
}
