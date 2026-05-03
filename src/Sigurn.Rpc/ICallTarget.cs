using Sigurn.Rpc.Infrastructure.Packets;

namespace Sigurn.Rpc;

/// <summary>
/// Provides data for the <see cref="ICallTarget.EventTriggered"/> event.
/// </summary>
public class EventDataArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of <see cref="EventDataArgs"/> with the specified event identifier and arguments.
    /// </summary>
    /// <param name="eventId">The identifier of the remote event.</param>
    /// <param name="args">The serialized arguments of the remote event.</param>
    public EventDataArgs(int eventId, IReadOnlyList<byte[]> args)
    {
        EventId = eventId;
        Args = args;
    }

    /// <summary>
    /// Gets the identifier of the remote event.
    /// </summary>
    public int EventId { get; }

    /// <summary>
    /// Gets the serialized arguments of the remote event.
    /// </summary>
    public IReadOnlyList<byte[]> Args { get; }
}

/// <summary>
/// Defines the contract for invoking remote methods, properties, and events on a service instance.
/// </summary>
public interface ICallTarget
{
    //Guid InstanceId { get; }

    /// <summary>
    /// Invokes a remote method asynchronously.
    /// </summary>
    /// <param name="methodId">The identifier of the method to invoke.</param>
    /// <param name="args">The serialized method arguments.</param>
    /// <param name="oneWay">If <see langword="true"/>, the method is invoked without waiting for a result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The serialized return value and any output arguments, or <see langword="null"/> for void methods.</returns>
    Task<(byte[]? Result, IReadOnlyList<byte[]>? Args)> InvokeMethodAsync(int methodId, IReadOnlyList<byte[]>? args, bool oneWay, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the serialized value of a remote property asynchronously.
    /// </summary>
    /// <param name="propertyId">The identifier of the property.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The serialized property value, or <see langword="null"/>.</returns>
    Task<byte[]?> GetPropertyValueAsync(int propertyId, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the value of a remote property asynchronously.
    /// </summary>
    /// <param name="propertyId">The identifier of the property.</param>
    /// <param name="value">The serialized value to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetPropertyValueAsync(int propertyId, byte[]? value, CancellationToken cancellationToken);

    /// <summary>
    /// Subscribes to a remote event asynchronously.
    /// </summary>
    /// <param name="eventId">The identifier of the event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AttachEventHandlerAsync(int eventId, CancellationToken cancellationToken);

    /// <summary>
    /// Unsubscribes from a remote event asynchronously.
    /// </summary>
    /// <param name="eventId">The identifier of the event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DetachEventHandlerAsync(int eventId, CancellationToken cancellationToken);

    /// <summary>
    /// Occures when a remote event is triggered.
    /// </summary>
    event EventHandler<EventDataArgs>? EventTriggered;
}