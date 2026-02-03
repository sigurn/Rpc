using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc;

class ProcessChannel : BaseChannel
{
    private static readonly ILogger<ProcessChannel> _logger = RpcLogging.CreateLogger<ProcessChannel>();

    private readonly ProcessStartInfo? _processInfo;
    private readonly IProtocol _protocol;
    private Process? _process;
    private Stream? _inputStream;
    private Stream? _outputStream;

    public ProcessChannel(Stream inputStream, Stream outputStream, IProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(inputStream);
        ArgumentNullException.ThrowIfNull(outputStream);

        _inputStream = inputStream;
        _outputStream = outputStream;
        _protocol = protocol;

        State = ChannelState.Opened;
    }

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

    public ProcessChannel(string fileName)
        : this(fileName, new ChannelProtocol())
    {
    }

    public ProcessChannel(string fileName, string args)
        : this(fileName, args, new ChannelProtocol())
    {
    }

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
            _process = process;
        }

        if (_inputStream is null && _outputStream is null) return;

        try
        {
            _inputStream?.Close();
            _outputStream?.Close();

            if (process is not null)
            {
                process.Exited -= OnProcessExited;
                await SendSignalAsync(process.Id, cancellationToken);
                await process.WaitForExitAsync(cancellationToken);                
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

        var process = Process.Start(_processInfo);

        if (process is null)
            throw new Exception($"Failed to start process {_processInfo.FileName}");

        process.Exited += OnProcessExited;

        lock(_lock)
        {
            _process = process;
            _inputStream = process.StandardInput.BaseStream; //for writing
            _outputStream = process.StandardOutput.BaseStream; //for reading
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
                size = _protocol.ApplyNextReceivedBlock(await ReceiveData(stream, size, cancellationToken));

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
                    await SendData(stream, buf, cancellationToken);
            }
            while(buf is not null);

            _protocol.EndSending();

            await stream.FlushAsync();
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

            var task = await Task.WhenAny(readTask, cancelTask);
            if (task == cancelTask)
                throw new TaskCanceledException();
            
            var len = await readTask;

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

        var task = await Task.WhenAny(readTask, cancelTask);
        if (task == cancelTask)
            throw new TaskCanceledException();

        await readTask;
    }

    public static async Task<bool> SendSignalAsync(int pid, CancellationToken cancellationToken)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var psi = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = $"/PID {pid}",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            var p = Process.Start(psi);
            if (p is null)
                throw new InvalidOperationException("Cannot stop process");
            await p.WaitForExitAsync(cancellationToken);
            return p.ExitCode == 0;
        }
        else
        {
            var psi = new ProcessStartInfo
            {
                FileName = "kill",
                Arguments = $"-TERM {pid}",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            var p = Process.Start(psi);
            if (p is null)
                throw new InvalidOperationException("Cannot stop process");
            await p.WaitForExitAsync(cancellationToken);
            return p.ExitCode == 0;
        }
    }
}