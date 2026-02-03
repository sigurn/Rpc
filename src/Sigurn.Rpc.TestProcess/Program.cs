using System.Text;
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
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.With(new ThreadIdEnricher())
            .WriteTo.File("/tmp/log.txt", rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [Thread:{ThreadId}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    
        RpcLogging.Configure(new SerilogLoggerFactory(Log.Logger));

        using ManualResetEvent stopEvent = new ManualResetEvent(false);

        using CancellationTokenSource cts = new CancellationTokenSource();
        var host = new ServiceHost(new ProcessHost());
        host.PublishServicesCatalog = true;

        host.RegisterSerive<ITestProcess>(ShareWithin.Host, () =>
        {
            return new TestProcessService(() => 
            {
                Log.Debug("ExitHandler");
                Task.Run(async () =>
                {
                    Log.Debug("Set stop event");
                   //await Task.Delay(TimeSpan.FromMilliseconds(100));
                   stopEvent?.Set();
                });
            });
        });

        Console.TreatControlCAsInput = false;
        Console.CancelKeyPress += (s,a) =>
        {
            stopEvent.Set();
        };

        Log.Debug("Starting");
        host.Start();
        Log.Debug("Started");

        stopEvent.WaitOne();

        Log.Debug("Stopping");
        host.Stop();
        Log.Debug("Stopped");
    }
}