using System.Collections.Immutable;
using System.Threading;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc;

/// <summary>
/// Helper operations for remote interface instances (proxies obtained from the RPC layer and local
/// implementations passed to the remote side).
/// </summary>
public static class RpcInterface
{
    // Instances the caller marked "borrowed" (do not dispose) in the current execution flow. Ownership
    // is a property of a single act of passing, not of the object, so it is kept per-flow (AsyncLocal,
    // copy-on-write) rather than in a global registry — a global map keyed by the instance could not
    // represent two concurrent passes of the same object with different intents, and would collide
    // across sessions. The mark flows with the ExecutionContext into argument serialization and is
    // consumed exactly once when the adapter is created.
    private static readonly AsyncLocal<ImmutableHashSet<object>?> _borrowed = new();

    /// <summary>
    /// Gets the channel that backs a remote interface proxy, or <see langword="null"/> if the object is
    /// not a proxy or is not bound to a channel.
    /// </summary>
    /// <typeparam name="T">The interface type.</typeparam>
    /// <param name="obj">The proxy instance.</param>
    public static IChannel? GetChannel<T>(T obj)
    {
        if (obj is not InterfaceProxy ip) return null;
        if (ip.CallTargetOrNull is ServiceInstance si) return si.Handler.Channel;
        return null;
    }

    /// <summary>
    /// Determines whether a remote interface instance is still usable. Returns <see langword="false"/>
    /// for objects that are not proxies, disposed proxies, proxies whose call target has been detached,
    /// proxies invalidated by a reconnect, and proxies whose channel is closed or faulted.
    /// </summary>
    /// <typeparam name="T">The interface type.</typeparam>
    /// <param name="obj">The proxy instance.</param>
    public static bool IsValid<T>(T obj)
    {
        if (obj is not InterfaceProxy ip) return false;
        if (ip.IsDisposedInternal) return false;

        var callTarget = ip.CallTargetOrNull;
        if (callTarget is null) return false;

        if (callTarget is ServiceInstance si)
        {
            if (!si.IsValid) return false;

            var state = si.Handler.Channel.State;
            if (state == ChannelState.Closed || state == ChannelState.Faulted) return false;
        }

        return true;
    }

    /// <summary>
    /// Marks a local interface implementation as <em>borrowed</em> for the current call: the RPC layer
    /// will not dispose it when the remote reference is released or the channel reconnects. Returns the
    /// same instance so it can be passed inline, e.g.
    /// <c>service.SetCallback(RpcInterface.NoDispose(callback))</c>.
    /// </summary>
    /// <typeparam name="T">The interface type.</typeparam>
    /// <param name="instance">The instance to keep ownership of.</param>
    /// <returns>The same <paramref name="instance"/>.</returns>
    public static T NoDispose<T>(T instance)
    {
        if (instance is null) return instance;

        var set = _borrowed.Value ?? ImmutableHashSet.Create<object>(ReferenceEqualityComparer.Instance);
        _borrowed.Value = set.Add(instance);
        return instance;
    }

    // True if the instance was marked borrowed in the current flow; consumes the mark.
    internal static bool ConsumeBorrowed(object instance)
    {
        var set = _borrowed.Value;
        if (set is null || !set.Contains(instance)) return false;

        _borrowed.Value = set.Remove(instance);
        return true;
    }
}
