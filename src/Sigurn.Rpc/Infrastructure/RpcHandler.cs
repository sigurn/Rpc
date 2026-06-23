using System.Collections.Concurrent;
using System.Data;
using Microsoft.Extensions.Logging;
using Sigurn.Rpc.Infrastructure.Packets;
using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure;

class RpcHandler : IDisposable
{
    private static readonly ILogger<RpcHandler> _logger = RpcLogging.CreateLogger<RpcHandler>();

    private static int _instanceCounter;

    private static int GetInstanceId()
    {
        return Interlocked.Increment(ref _instanceCounter);
    }

    private readonly object _lock = new object();

    private readonly Func<RpcPacket, CancellationToken, Task<RpcPacket?>> _handler;
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<RpcPacket>> _requests = new ();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellationSources = new ();
    private readonly IChannel _channel;
    private CancellationTokenSource? _cancellationSource;
    private Task? _handlingTask;

    private List<Func<RpcPacket, CancellationToken, Task<RpcPacket?>>> _packetHandlers = new();

    private readonly int _instanceId;
    private readonly SerializationContext _context = RpcPacket.DefaultSerializationContext;

    public RpcHandler(IChannel channel)
        : this(channel, InvalidHandler)
    {
    }

    public RpcHandler(IChannel channel, Func<RpcPacket, CancellationToken, Task<RpcPacket?>> handler)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(handler);

        _instanceId = GetInstanceId();

        _channel = channel;
        _handler = handler;

        _channel.Opened += OnChannelOpened;
        _channel.Closed += OnChannelClosed;
        _channel.Faulted += OnChannelFaulted;

        _cancellationSource = new CancellationTokenSource();
        _handlingTask = StartPacketHandling(_cancellationSource.Token);
    }


    public void Dispose()
    {
        if (!StopHandling().Wait(TimeSpan.FromSeconds(5)))
            throw new Exception("Failed to stop packet handling within 5 seconds");
    }

    public IChannel Channel => _channel;

    private TimeSpan _answerTimeout = TimeSpan.FromSeconds(15);
    public TimeSpan AnswerTimeout
    {
        get
        {
            lock(_lock)
                return _answerTimeout;
        }

        set
        {
            lock(_lock)
                _answerTimeout = value;
        }
    }

    public IDisposable Handle<T>(Func<T, CancellationToken, Task<RpcPacket?>> handler) where T : RpcPacket
    {
        lock (_packetHandlers)
        {
            Func<RpcPacket, CancellationToken, Task<RpcPacket?>> proxyHandler = async (packet, cancellationToken) =>
            {
                if (packet is not T typedPacket) return null;
                return await handler(typedPacket, cancellationToken);
            };

            _packetHandlers.Add(proxyHandler);

            return Disposable.Create(() =>
            {
                lock (_packetHandlers)
                    _packetHandlers.Remove(proxyHandler);
            });
        }
    }

    public ICallTarget CreateCallTargetForInstance(Guid instanceId)
    {
        return new ServiceInstance(instanceId, this);
    }

    private IReadOnlyList<Func<RpcPacket, CancellationToken, Task<RpcPacket?>>> GetPacketHandlers()
    {
        lock (_packetHandlers)
            return _packetHandlers.ToList();
    }

    public async Task SendAsync(RpcPacket packet, CancellationToken cancellationToken)
    {
        var request = IPacket.Create(await packet.ToBytesAsync(_context, cancellationToken));
        await _channel.SendAsync(request, cancellationToken);
    }

    public async Task<RpcPacket> RequestAsync(RpcPacket packet, CancellationToken cancellationToken)
    {
        var timeout = Sigurn.Rpc.RpcContext.Current?.Timeout ?? AnswerTimeout;
        using var taskCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var completionSource = new TaskCompletionSource<RpcPacket>();
        CancellationTokenRegistration? ctr = null;
        
        try
        {
            if (!_requests.TryAdd(packet.RequestId, completionSource))
                throw new InvalidOperationException("Request with the same Id is already being processed");

            var request = IPacket.Create(await packet.ToBytesAsync(_context, cancellationToken));
            await _channel.SendAsync(request, cancellationToken);

            ctr = cancellationToken.Register(async () =>
            {
                var cancelPacket = IPacket.Create(await new CancelRequestPacket(packet).ToBytesAsync(_context, CancellationToken.None));
                await _channel.SendAsync(cancelPacket, CancellationToken.None);
                if (_requests.TryRemove(packet.RequestId, out var rcs))
                    rcs.TrySetCanceled();
            });
            return await completionSource.Task.WaitAsync(timeout);
        }
        finally
        {
            if (ctr.HasValue) await ctr.Value.DisposeAsync();
            _requests.Remove(packet.RequestId, out var _);
        }
    }

    public async Task<T> RequestAsync<T>(RpcPacket packet, CancellationToken cancellationToken) where T : RpcPacket
    {
        var timeout = Sigurn.Rpc.RpcContext.Current?.Timeout ?? AnswerTimeout;
        using var taskCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var completionSource = new TaskCompletionSource<RpcPacket>();
        CancellationTokenRegistration? ctr = null;

        try
        {
            if (!_requests.TryAdd(packet.RequestId, completionSource))
                throw new InvalidOperationException("Request with the same Id is already being processed");

            var request = IPacket.Create(await packet.ToBytesAsync(_context, cancellationToken));
            await _channel.SendAsync(request, cancellationToken);
            ctr = cancellationToken.Register(async () =>
            {
                var cancelPacket = IPacket.Create(await new CancelRequestPacket(packet).ToBytesAsync(_context, CancellationToken.None));
                await _channel.SendAsync(cancelPacket, CancellationToken.None);
                if (_requests.TryRemove(packet.RequestId, out var rcs))
                    rcs.TrySetCanceled();
            });

            var answer = await completionSource.Task.WaitAsync(timeout);
            if (answer is T a)
                return a;
            else if (answer is ExceptionPacket exp)
                RpcServerException.Throw(exp);
            else if (answer is ErrorPacket erp)
                RpcErrorException.Throw(erp);

            throw new Exception("Unknown server answer");
        }
        finally
        {
            if (ctr.HasValue) await ctr.Value.DisposeAsync();
            _requests.Remove(packet.RequestId, out var _);
        }
    }

    public async Task<Guid> GetServiceInstanceAsync(Guid interfaceId, CancellationToken cancellationToken)
    {
        var request = new GetInstancePacket
        {
            InterfaceId = interfaceId
        };

        var answer = await RequestAsync<ServiceInstancePacket>(request, cancellationToken);
        return answer.InstanceId;
    }

    public async Task ReleaseServiceInstanceAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        // Releasing an instance is a best-effort notification to the remote side.
        // When the channel is not opened (typically the disconnect that triggered the
        // session teardown) there is no remote instance to release, so skip the send.
        // A restorable channel that reconnects gets a brand new remote session, so the
        // old instance id would be meaningless there anyway.
        if (_channel.State != ChannelState.Opened)
            return;

        var request = new ReleaseInstancePacket
        {
            InstanceId = instanceId
        };

        await SendAsync(request, cancellationToken);
    }

    public async Task<(byte[]? Result, IReadOnlyList<byte[]>? Args)> InvokeMethodAsync(Guid instanceId, int methodId, IReadOnlyList<byte[]> args, bool oneWay, CancellationToken cancellationToken)
    {
        var request = new MethodCallPacket
        {
            InstanceId = instanceId,
            MethodId = methodId,
            Args = args,
            OneWay = oneWay
        };

        var answer = await RequestAsync<MethodResultPacket>(request, cancellationToken);
        return (answer.Result, answer.Args);
    }

    public async Task<byte[]?> GetPropertyAsync(Guid instanceId, int propertyId, CancellationToken cancellationToken)
    {
        var request = new GetPropertyPacket
        {
            InstanceId = instanceId,
            PropertyId = propertyId,
        };

        var answer = await RequestAsync<PropertyValuePacket>(request, cancellationToken);
        return answer.Value;
    }

    public async Task SetPropertyAsync(Guid instanceId, int propertyId, byte[]? value, CancellationToken cancellationToken)
    {
        var request = new SetPropertyPacket
        {
            InstanceId = instanceId,
            PropertyId = propertyId,
            Value = value
        };

        await RequestAsync<SuccessPacket>(request, cancellationToken);
    }

    public async Task AttachEventAsync(Guid instanceId, int eventId, CancellationToken cancellationToken)
    {
        var request = new SubscribeForEventPacket
        {
            InstanceId = instanceId,
            EventId = eventId,
        };

        await RequestAsync<SuccessPacket>(request, cancellationToken);
    }

    public async Task DetachEventAsync(Guid instanceId, int eventId, CancellationToken cancellationToken)
    {
        var request = new UnsubscribeFromEventPacket
        {
            InstanceId = instanceId,
            EventId = eventId,
        };

        await RequestAsync<SuccessPacket>(request, cancellationToken);
    }

    private void OnChannelOpened(object? sender, EventArgs args)
    {
        lock (_lock)
        {
            if (_handlingTask is not null) return;

            _cancellationSource = new CancellationTokenSource();
            _handlingTask = StartPacketHandling(_cancellationSource.Token);
        }
    }

    private void OnChannelClosed(object? sender, EventArgs args)
    {
        StopHandling().Wait(TimeSpan.FromSeconds(5));
    }

    private void OnChannelFaulted(object? sender, EventArgs args)
    {
        var requests = _requests.ToArray();
        _requests.Clear();

        foreach(var kvp in requests)
            kvp.Value.SetCanceled();

        // The channel is gone, so any in flight server side handlers will never be able to
        // deliver their result. Cancel their tokens right away so blocking handlers can unwind
        // cooperatively instead of hanging until the teardown timeout.
        CancelPendingRequests();
    }

    private void CancelPendingRequests()
    {
        foreach (var kvp in _cancellationSources.ToArray())
        {
            try
            {
                kvp.Value.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The source may have already been disposed by the handling loop cleanup.
            }
        }
    }

    private async Task StopHandling()
    {
        CancellationTokenSource? cts;
        Task? task;
        lock(_lock)
        {
            cts = _cancellationSource;
            task = _handlingTask;
            _cancellationSource = null;
            _handlingTask = null;
        }

        try
        {
            if (task is null || task.IsCompleted) return;
            if (cts is not null) cts.Cancel();
            // Cancel in flight handler tokens explicitly so blocking handlers unwind immediately
            // rather than relying solely on linked token propagation through the handling token.
            CancelPendingRequests();
            await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch(TimeoutException)
        {

        }
        finally
        {
            // Task instances do not need to be disposed; a Task that is still running (e.g. a
            // hung handler that survived the WaitAsync timeout) throws from Dispose() because it
            // is not in a completion state. Just drop the reference and let it finish on its own.
            cts?.Dispose();
        }
    }

    private async Task StartPacketHandling(CancellationToken cancellationToken)
    {
        List<Task> tasks = [];
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_channel.State != ChannelState.Opened &&
                    !await WaitForChannelOpenAsync(_channel, cancellationToken).ConfigureAwait(false)) return;

                var packet = await _channel.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                var request = await RpcPacket.FromPacketAsync(packet, _context, cancellationToken).ConfigureAwait(false);
                if (request is null) continue;

                if (_requests.TryGetValue(request.RequestId, out var tcs))
                {
                    ThreadPool.QueueUserWorkItem(state =>
                    {
                        if (state is RpcPacket packet)
                            tcs.TrySetResult(request);
                    }, request);
                    continue;
                }

                if (request is CancelRequestPacket crp && _cancellationSources.TryGetValue(crp.RequestId, out var rcts))
                {
                    rcts.Cancel();
                    continue;
                }

                var requestCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _cancellationSources.TryAdd(request.RequestId, requestCancellationToken);

                var task = Task.Run(() => HandlePacket(request, requestCancellationToken.Token))
                    .ContinueWith(async t =>
                    {
                        if (_cancellationSources.TryRemove(request.RequestId, out var rcts))
                            rcts.Dispose();

                        if (cancellationToken.IsCancellationRequested) return;

                        RpcPacket? answer = null;
                        if (t.IsFaulted)
                            answer = new ExceptionPacket(request, t.Exception);
                        else if (t.IsCompletedSuccessfully)
                            answer = t.Result;

                        if (answer is null) return;

                        var answerPacket = packet.CreateAnswer(await answer.ToBytesAsync(_context, cancellationToken));
                        if (_channel.State != ChannelState.Opened || cancellationToken.IsCancellationRequested) return;
                        await _channel.SendAsync(answerPacket, cancellationToken);
                    });

                lock (tasks)
                {
                    tasks.Add(task);
                    foreach (var t in tasks.Where(t => t.IsCanceled).ToArray())
                        tasks.Remove(t);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error during RPC packet handling");
                continue;
            }
        }

        Task[] waitTasks;
        lock (tasks)
        {
            waitTasks = tasks.Where(x => !x.IsCompleted).ToArray();
            tasks.Clear();
        }

        // Give in flight handlers a chance to finish, but never call Dispose() on the wrappers:
        // a Task that is still running (a hung handler) is not in a completion state and Dispose()
        // would throw InvalidOperationException, crashing the handling loop. Incomplete tasks are
        // simply abandoned - they complete on their own once their cancelled handlers unwind.
        Task.WaitAll(waitTasks, TimeSpan.FromSeconds(5));

        var tokens = _cancellationSources.ToArray();
        _cancellationSources.Clear();
        
        foreach (var t in tokens)
            t.Value.Dispose();
    }

    private async Task<RpcPacket?> HandlePacket(RpcPacket request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var packetHandlers = GetPacketHandlers();
        foreach (var packetHandler in packetHandlers)
        {
            var response = await packetHandler(request, cancellationToken);
            if (response is not null) return response;
        }

        if (_handler is null) return null;
        return await _handler(request, cancellationToken);
    }

    private static async Task<bool> WaitForChannelOpenAsync(IChannel channel, CancellationToken cancellationToken)
    {
        if (channel.State == ChannelState.Opened) return true;

        var tcs = new TaskCompletionSource<bool>();
        EventHandler handler = (s, e) => tcs.TrySetResult(true);
        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(), false);

        try
        {
            channel.Opened += handler;
            if (channel.State == ChannelState.Opened) tcs.TrySetResult(true);
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            channel.Opened -= handler;
        }
    }

    private static Task<RpcPacket?> InvalidHandler(RpcPacket request, CancellationToken cancellationToken)
    {
        return Task.FromResult<RpcPacket?>(null);
    }
}