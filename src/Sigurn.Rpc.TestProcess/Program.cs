using Microsoft.CodeAnalysis.CSharp;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace Sigurn.Rpc.TestProcess;

class ThreadIdEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                "ThreadId", Thread.CurrentThread.ManagedThreadId));
    }
}

static class Program
{
    public static async Task Main(string[] args)
    {
        var logFile = Path.Combine(Path.GetTempPath(), "log.txt");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.With(new ThreadIdEnricher())
            .WriteTo.File(logFile, rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [Thread:{ThreadId}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    
        RpcLogging.Configure(new SerilogLoggerFactory(Log.Logger));

        var host = new ServiceHostAsync()
        {
            PublishServicesCatalog = true
        };

        host.RegisterAcceptor(() => ProcessHostAsync.Open());
        host.RegisterSerive<ITestProcess>(ShareWithin.Host, () =>
        {
            return new TestProcessService(() => ProcessTermination.Cancel("Canceled by service request"));
        });

        await host.RunAsync(ProcessTermination.CancellationToken);
    }
}