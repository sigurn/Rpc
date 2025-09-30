// using Sigurn.Rpc.Infrastructure.Packets;
// using Sigurn.Serialize;

// namespace Sigurn.Rpc.Infrastructure;

// class InstanceManager : IDisposable
// {
//     private readonly RpcHandler _handler;
//     private readonly Dictionary<Guid, ObjectRef<ICallTarget>> _adapters = new();
//     private readonly Dictionary<Guid, WeakReference<InterfaceProxy>> _proxies = new();

//     public InstanceManager(RpcHandler handler)
//     {
//         _handler = handler;
//         InterfaceProxy.InstanceDestroyed += OnProxyDestroyed;
//     }

//     public void Dispose()
//     {
//         ICallTarget[] adapters;
//         lock (_adapters)
//         {
//             adapters = _adapters.Values.ToArray();
//             _adapters.Clear();
//         }

//         lock (_proxies)
//             _proxies.Clear();

//         foreach (var adapter in adapters.OfType<IDisposable>())
//             adapter.Dispose();
//     }

//     public Guid RegisterInstance(Type type, object instance, SerializationContext context)
//     {
//         lock (_adapters)
//         {
//             ICallTarget? adapter;

//             if (!_adapters.TryGetValue(instance, out adapter))
//             {
//                 adapter = InterfaceAdapter.CreateAdapter(type, instance, context);
//                 adapter.EventTriggered += OnEventRaised;
//                 _adapters.Add(instance, adapter);
//             }

//             return adapter.InstanceId;
//         }
//     }

//     public Guid RegisterInstance(Type type, ObjectRef<ICallTarget> instance, SerializationContext context)
//     {
//         lock (_adapters)
//         {
//             ObjectRef<ICallTarget> adapter;

//             if (!_adapters.TryGetValue(instance.Value.InstanceId, out adapter))
//             {
//                 adapter.EventTriggered += OnEventRaised;
//                 _adapters.Add(instance, adapter);
//             }

//             return adapter.InstanceId;
//         }
//     }

//     public object GetInstance(Type type, Guid instanceId, SerializationContext context)
//     {
//         lock (_proxies)
//         {
//             InterfaceProxy? proxy;

//             if (_proxies.TryGetValue(instanceId, out var proxyRef) && proxyRef.TryGetTarget(out proxy))
//             {
//                 proxy.AddRef();
//                 return proxy;
//             }

//             proxy = InterfaceProxy.CreateProxy(type, new ServiceInstance(instanceId, _handler), context);
//             _proxies.Add(instanceId, new WeakReference<InterfaceProxy>(proxy));

//             return proxy;
//         }
//     }

//     public bool ReleaseInstace(Guid instanceId)
//     {
//         ICallTarget? adapter = null;
//         lock (_adapters)
//         {
//             foreach (var kvp in _adapters)
//             {
//                 if (kvp.Value.InstanceId == instanceId)
//                 {
//                     adapter = kvp.Value;
//                     _adapters.Remove(kvp.Key);
//                     break;
//                 }
//             }
//         }

//         if (adapter is not null)
//             adapter.EventTriggered -= OnEventRaised;

//         if (adapter is IDisposable d)
//             d.Dispose();

//         return adapter is not null;
//     }

//     public ICallTarget? GetCallTarget(Guid instanceId)
//     {
//         lock (_adapters)
//         {
//             foreach (var kvp in _adapters)
//             {
//                 if (kvp.Value.InstanceId == instanceId)
//                     return kvp.Value;
//             }
//         }

//         return null;
//     }

//     private void OnProxyDestroyed(Guid instanceId)
//     {
//         ThreadPool.QueueUserWorkItem(id =>
//         {
//             lock (_proxies)
//             {
//                 if (_proxies.ContainsKey(instanceId))
//                 {
//                     _proxies.Remove(instanceId);
//                     _handler.ReleaseServiceInstanceAsync(instanceId, CancellationToken.None).Wait();
//                 }
//             }
//         }, instanceId, true);
//     }

//     private void OnEventRaised(object? sender, EventDataArgs args)
//     {
//         if (sender is ICallTarget ct)
//         {
//             var packet = new EventDataPacket(ct.InstanceId, args.EventId, args.Args);
//             _handler.SendAsync(packet, CancellationToken.None).Wait();
//         }
//     }
// }