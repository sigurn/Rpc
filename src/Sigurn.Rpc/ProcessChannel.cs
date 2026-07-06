using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc;

/// <summary>
/// A transport channel that communicates with a child process over its standard input and
/// output streams.
/// </summary>
/// <remarks>
/// The channel can either wrap a pair of already-open streams, or start a new process from a
/// file name (optionally with arguments and a customization callback). When it starts the
/// process it always redirects the child's standard input and output and forces
/// <c>UseShellExecute = false</c>, so RPC traffic flows over those streams. Closing the channel
/// terminates the started process.
/// </remarks>
public class ProcessChannel : BaseChannel
{
    private static readonly ILogger<ProcessChannel> _logger = RpcLogging.CreateLogger<ProcessChannel>();

    private readonly ProcessStartInfo? _processInfo;
    private readonly Action<ProcessStartInfo>? _configure;
    private readonly IProtocol _protocol;
    private Process? _process;
    private Stream? _inputStream;
    private Stream? _outputStream;

    /// <summary>
    /// Creates a channel over a pair of already-open streams. No process is started; the channel
    /// is immediately considered opened.
    /// </summary>
    /// <param name="inputStream">The stream the channel writes outgoing data to.</param>
    /// <param name="outputStream">The stream the channel reads incoming data from.</param>
    /// <param name="protocol">The framing protocol to use.</param>
    public ProcessChannel(Stream inputStream, Stream outputStream, IProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(inputStream);
        ArgumentNullException.ThrowIfNull(outputStream);

        _inputStream = inputStream;
        _outputStream = outputStream;
        _protocol = protocol;

        State = ChannelState.Opened;
    }

    /// <summary>
    /// Creates a channel that starts the given executable when the channel is opened.
    /// </summary>
    /// <param name="fileName">The executable to start.</param>
    /// <param name="protocol">The framing protocol to use.</param>
    public ProcessChannel(string fileName, IProtocol protocol)
    {
        _processInfo = new ProcessStartInfo()
        {
            FileName = fileName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        };
        _protocol = protocol;
    }

    /// <summary>
    /// Creates a channel that starts the given executable with arguments when the channel is opened.
    /// </summary>
    /// <param name="fileName">The executable to start.</param>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="protocol">The framing protocol to use.</param>
    public ProcessChannel(string fileName, string args, IProtocol protocol)
    {
        _processInfo = new ProcessStartInfo()
        {
            FileName = fileName,
            Arguments = args,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        };
        _protocol = protocol;
    }

    /// <summary>
    /// Creates a channel that starts the given executable using the default protocol when the
    /// channel is opened.
    /// </summary>
    /// <param name="fileName">The executable to start.</param>
    public ProcessChannel(string fileName)
        : this(fileName, new ChannelProtocol())
    {
    }

    /// <summary>
    /// Creates a channel that starts the given executable with arguments using the default
    /// protocol when the channel is opened.
    /// </summary>
    /// <param name="fileName">The executable to start.</param>
    /// <param name="args">The command-line arguments.</param>
    public ProcessChannel(string fileName, string args)
        : this(fileName, args, new ChannelProtocol())
    {
    }

    /// <summary>
    /// Creates a channel that starts the given executable, letting the caller customize how
    /// the process is launched (for example, to run it as a specific OS user).
    /// </summary>
    /// <param name="fileName">The executable to start.</param>
    /// <param name="configure">
    /// A callback invoked with the <see cref="ProcessStartInfo"/> just before the process is
    /// started. Use it to set credentials and other options. On Windows you can set
    /// <c>UserName</c>/<c>Domain</c>/<c>Password</c> (or <c>PasswordInClearText</c>) and
    /// <c>LoadUserProfile</c>. On Linux/macOS those properties are not supported, so launch
    /// through a wrapper instead (e.g. <c>sudo -u user</c> / <c>runuser</c>). The channel
    /// re-applies the stdin/stdout redirection flags it requires after this callback runs.
    /// </param>
    /// <param name="protocol">The framing protocol to use.</param>
    /// <example>
    /// Windows:
    /// <code>
    /// var channel = new ProcessChannel("worker.exe", psi =>
    /// {
    ///     psi.UserName = "bob";
    ///     psi.Domain = "CORP";
    ///     psi.PasswordInClearText = "secret";
    ///     psi.LoadUserProfile = true;
    /// }, new ChannelProtocol());
    /// </code>
    /// Linux/macOS:
    /// <code>
    /// var channel = new ProcessChannel("sudo", "-u bob worker", psi => { }, new ChannelProtocol());
    /// </code>
    /// </example>
    public ProcessChannel(string fileName, Action<ProcessStartInfo> configure, IProtocol protocol)
        : this(fileName, protocol)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configure = configure;
    }

    /// <summary>
    /// Creates a channel that starts the given executable with arguments, letting the caller
    /// customize how the process is launched (for example, to run it as a specific OS user).
    /// </summary>
    /// <param name="fileName">The executable to start.</param>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="configure">
    /// A callback invoked with the <see cref="ProcessStartInfo"/> just before the process is
    /// started. See <see cref="ProcessChannel(string, Action{ProcessStartInfo}, IProtocol)"/>
    /// for usage notes on running as a specific user across platforms.
    /// </param>
    /// <param name="protocol">The framing protocol to use.</param>
    public ProcessChannel(string fileName, string args, Action<ProcessStartInfo> configure, IProtocol protocol)
        : this(fileName, args, protocol)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configure = configure;
    }

    /// <summary>
    /// Creates a channel that starts the given executable using the default protocol, letting
    /// the caller customize how the process is launched (for example, to run it as a specific OS user).
    /// </summary>
    /// <param name="fileName">The executable to start.</param>
    /// <param name="configure">
    /// A callback invoked with the <see cref="ProcessStartInfo"/> just before the process is
    /// started. See <see cref="ProcessChannel(string, Action{ProcessStartInfo}, IProtocol)"/>
    /// for usage notes on running as a specific user across platforms.
    /// </param>
    public ProcessChannel(string fileName, Action<ProcessStartInfo> configure)
        : this(fileName, configure, new ChannelProtocol())
    {
    }

    /// <summary>
    /// Creates a channel that starts the given executable with arguments using the default
    /// protocol, letting the caller customize how the process is launched (for example, to run
    /// it as a specific OS user).
    /// </summary>
    /// <param name="fileName">The executable to start.</param>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="configure">
    /// A callback invoked with the <see cref="ProcessStartInfo"/> just before the process is
    /// started. See <see cref="ProcessChannel(string, Action{ProcessStartInfo}, IProtocol)"/>
    /// for usage notes on running as a specific user across platforms.
    /// </param>
    public ProcessChannel(string fileName, string args, Action<ProcessStartInfo> configure)
        : this(fileName, args, configure, new ChannelProtocol())
    {
    }

    /// <summary>
    /// Gets the operating-system identifier of the started process.
    /// </summary>
    /// <exception cref="InvalidOperationException">The process has not been started yet.</exception>
    public int ProcessId
    {
        get
        {
            lock(_lock)
            {
                if (_process is null)
                    throw new InvalidOperationException("The process is not started");

                return _process.Id;
            }
        }
    }

    protected override async Task InternalCloseAsync(CancellationToken cancellationToken)
    {
        using var _ = _logger.Scope();
        Process? process;

        lock(_lock)
        {
            process = _process;
            _process = null;
        }

        if (_inputStream is null && _outputStream is null) return;

        try
        {
            _inputStream?.Close();
            _outputStream?.Close();

            if (process is not null)
            {
                process.Exited -= OnProcessExited;
                await SendSignalAsync(process.Id, cancellationToken).ConfigureAwait(false);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);                
            }
        }
        finally
        {
            process?.Dispose();
        }
    }

    protected override Task InternalOpenAsync(CancellationToken cancellationToken)
    {
        using var _ = _logger.Scope();

        cancellationToken.ThrowIfCancellationRequested();

        if (_processInfo is null)
            throw new InvalidOperationException("Cannot open unknown process");

        _configure?.Invoke(_processInfo);

        // The channel owns these — re-apply after the callback so it can't be broken.
        // UseShellExecute=false is also required for UserName/Password on Windows.
        _processInfo.RedirectStandardInput = true;
        _processInfo.RedirectStandardOutput = true;
        _processInfo.UseShellExecute = false;

        var process = Process.Start(_processInfo);

        if (process is null)
            throw new Exception($"Failed to start process {_processInfo.FileName}");

        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += OnProcessExited;

            lock(_lock)
            {
                _process = process;
                _inputStream = process.StandardInput.BaseStream; //for writing
                _outputStream = process.StandardOutput.BaseStream; //for reading
            }
        }
        catch
        {
            // Failed to wire up the freshly started child process — kill and release it
            // instead of leaking the running OS process.
            try { if (!process.HasExited) process.Kill(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to kill process during open cleanup"); }
            process.Dispose();
            throw;
        }

        return Task.CompletedTask;
    }

    protected override async Task<IPacket> InternalReceiveAsync(CancellationToken cancellationToken)
    {
        Stream? stream;

        lock(_lock)
            stream = _outputStream;

        if (stream is null)
            throw new InvalidOperationException($"There is no process to read from");

        int size = _protocol.StartReceiving();

        try
        {
            while(size != 0)
                size = _protocol.ApplyNextReceivedBlock(await ReceiveData(stream, size, cancellationToken).ConfigureAwait(false));

            return IPacket.Create(_protocol.EndReceiving());
        }
        catch(IOException)
        {
            _protocol.EndReceiving();

            GoToFaultedState();
            throw;
        }
        catch
        {
            _protocol.EndReceiving();
            throw;
        }
    }

    protected override async Task<IPacket> InternalSendAsync(IPacket packet, CancellationToken cancellationToken)
    {
        Stream? stream;
        lock(_lock)
        {
            stream = _inputStream;
        }

        if (stream is null)
            throw new InvalidOperationException("There is no process to send data to");

        _protocol.StartSending(packet.Data);

        try
        {
            byte[]? buf = null;

            do
            {
                buf = _protocol.GetNextBlockToSend();
                if (buf is not null)
                    await SendData(stream, buf, cancellationToken).ConfigureAwait(false);
            }
            while(buf is not null);

            _protocol.EndSending();

            await stream.FlushAsync().ConfigureAwait(false);
        }
        catch(IOException)
        {
            _protocol.EndSending();

            GoToFaultedState();
            throw;
        }
        catch
        {
            if (_protocol.IsSending)
                _protocol.EndSending();
            throw;
        }

        return packet;
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        GoToFaultedState();
    }

    private async Task<byte[]> ReceiveData(Stream stream, int size, CancellationToken cancellationToken)
    {
        var buf = new byte[size];
        int pos = 0;

        using var _ = cancellationToken.Register(() =>
        {
            if (State == ChannelState.Closing)
                stream.Close();
        });
        
        while(pos < size)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var readTask = stream.ReadAsync(new Memory<byte>(buf, pos, size - pos)).AsTask();
            var cancelTask = cancellationToken.WaitHandle.WaitOneAsync(CancellationToken.None);

            var task = await Task.WhenAny(readTask, cancelTask).ConfigureAwait(false);
            if (task == cancelTask)
                throw new TaskCanceledException();
            
            var len = await readTask.ConfigureAwait(false);

            if (len == 0)
                throw new IOException("Cannot read data from process output");

            pos += len;
        }

        return buf;
    }

    private static async Task SendData(Stream stream, byte[] data, CancellationToken cancellationToken)
    {
        var readTask = stream.WriteAsync(new ReadOnlyMemory<byte>(data, 0, data.Length)).AsTask();
        var cancelTask = cancellationToken.WaitHandle.WaitOneAsync(CancellationToken.None);

        var task = await Task.WhenAny(readTask, cancelTask).ConfigureAwait(false);
        if (task == cancelTask)
            throw new TaskCanceledException();

        await readTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a termination signal to the process with the given identifier (Ctrl+C on Windows,
    /// SIGINT on Linux/macOS) so it can shut down gracefully.
    /// </summary>
    /// <param name="pid">The identifier of the target process.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to <c>true</c> once the signal has been sent.</returns>
    public static async Task<bool> SendSignalAsync(int pid, CancellationToken cancellationToken)
    {
        ProcessTermination.TerminateProcess(pid);
        return true;
        // if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        // {
        //     var psi = new ProcessStartInfo
        //     {
        //         FileName = "taskkill",
        //         Arguments = $"/PID {pid}",
        //         CreateNoWindow = true,
        //         UseShellExecute = false
        //     };
        //     var p = Process.Start(psi);
        //     if (p is null)
        //         throw new InvalidOperationException("Cannot stop process");
        //     await p.WaitForExitAsync(cancellationToken);
        //     return p.ExitCode == 0;
        // }
        // else
        // {
        //     var psi = new ProcessStartInfo
        //     {
        //         FileName = "kill",
        //         Arguments = $"-TERM {pid}",
        //         CreateNoWindow = true,
        //         UseShellExecute = false
        //     };
        //     var p = Process.Start(psi);
        //     if (p is null)
        //         throw new InvalidOperationException("Cannot stop process");
        //     await p.WaitForExitAsync(cancellationToken);
        //     return p.ExitCode == 0;
        // }
    }
}