using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Sigurn.Rpc;

public class ProcessTermination
{
    private static readonly ILogger<ProcessTermination> _logger = RpcLogging.CreateLogger<ProcessTermination>();

    private static readonly object _lock = new ();
    private static CancellationTokenSource? _cts = null;
    public static CancellationToken CancellationToken
    {
        get
        {
            CancellationTokenSource cts;
            lock(_lock)
            {
                if (_cts is not null) return _cts.Token;
                _cts = new CancellationTokenSource();
                cts = _cts;
            }

            PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
            {
                _logger.LogDebug("SIGINT received (Ctrl+C)");
                cts.Cancel();
            });

            PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                _logger.LogDebug("SIGTERM received (taskkill/kill)");
                cts.Cancel();
            });

            return cts.Token;
        }
    }

    public static bool Cancel(string reason)
    {
        lock(_lock)
        {
            if (_cts is null) return false;

            _logger.LogDebug("Process cancellation requested. Reason: {reason}", reason);

            if (!_cts.IsCancellationRequested)
                _cts.Cancel();
            
            return true;
        }
    }

    internal static void TerminateProcess(int processId)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WindowsSendCtrlC(processId);
        }
        else
        {
            LinuxSendCtrlC(processId);
        }        
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "kill")]
    private static extern int kill(int pid, int sig);

    const int SIGINT = 2;

    private static void LinuxSendCtrlC(int processId)
    {
        kill(processId, SIGINT);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate? handler, bool add);

    delegate bool ConsoleCtrlDelegate(uint ctrlType);

    const uint CTRL_C_EVENT = 0;

    public static void WindowsSendCtrlC(int processId)
    {
        SetConsoleCtrlHandler(null, true);

        FreeConsole();
        AttachConsole((uint)processId);
        GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0);
        FreeConsole();

        SetConsoleCtrlHandler(null, false);
    }
}