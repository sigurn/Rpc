using Sigurn.Rpc.Infrastructure.Packets;

namespace Sigurn.Rpc;

public class EventDataArgs : EventArgs
{
    public EventDataArgs(int eventId, IReadOnlyList<byte[]> args)
    {
        EventId = eventId;
        Args = args;
    }

    public int EventId { get; }

    public IReadOnlyList<byte[]> Args { get; }
}

public interface ICallTarget
{
    //Guid InstanceId { get; }
    
    Task<(byte[]? Result, IReadOnlyList<byte[]>? Args)> InvokeMethodAsync(int methodId, IReadOnlyList<byte[]>? args, bool oneWay, CancellationToken cancellationToken);

    Task<byte[]?> GetPropertyValueAsync(int propertyId, CancellationToken cancellationToken);

    Task SetPropertyValueAsync(int propertyId, byte[]? value, CancellationToken cancellationToken);

    Task AttachEventHandlerAsync(int eventId, CancellationToken cancellationToken);

    Task DetachEventHandlerAsync(int eventId, CancellationToken cancellationToken);

    event EventHandler<EventDataArgs>? EventTriggered;
}