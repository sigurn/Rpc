using System.Collections.Immutable;

namespace Sigurn.Rpc;

/// <summary>
/// Defines the context for filtering event delivery to specific sessions within the current async execution context.
/// </summary>
public record EventContext
{
    private static readonly AsyncLocal<EventContext?> _current = new AsyncLocal<EventContext?>();

    internal static EventContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    internal IReadOnlyList<ISession>? Include { get; init; }

    internal IReadOnlyList<ISession>? Exclude { get; init; }

    /// <summary>
    /// Sets the event context for the current async execution context, controlling which sessions receive events.
    /// </summary>
    /// <param name="include">The sessions to include in event delivery, or an empty list to include all sessions.</param>
    /// <param name="exclude">The sessions to exclude from event delivery.</param>
    /// <returns>A disposable that clears the event context when disposed.</returns>
    public static IDisposable SetEventContext(IReadOnlyList<ISession> include, IReadOnlyList<ISession> exclude)
    {
        Current = new EventContext
        {
            Include = include,
            Exclude = exclude
        };

        return Disposable.Create(() => Current = null);
    }
}

/// <summary>
/// Provides extension methods for controlling event delivery scope on a per-session basis.
/// </summary>
public static class SessionExtensions
{
    /// <summary>
    /// Sets the event context to exclude the specified session from receiving events in the current async context.
    /// </summary>
    /// <param name="session">The session to exclude.</param>
    /// <returns>A disposable that clears the event context when disposed.</returns>
    public static IDisposable ExcludeSession(this ISession session)
    {
        EventContext.Current = new EventContext
        {
            Exclude = [session]
        };

        return Disposable.Create(() => EventContext.Current = null);
    }

    /// <summary>
    /// Sets the event context to exclude the specified sessions from receiving events in the current async context.
    /// </summary>
    /// <param name="sessions">The sessions to exclude.</param>
    /// <returns>A disposable that clears the event context when disposed.</returns>
    public static IDisposable ExcludeSessions(this IEnumerable<ISession> sessions)
    {
        EventContext.Current = new EventContext
        {
            Exclude = sessions.ToImmutableArray()
        };

        return Disposable.Create(() => EventContext.Current = null);
    }

    /// <summary>
    /// Sets the event context to deliver events only to the specified session in the current async context.
    /// </summary>
    /// <param name="session">The session to include.</param>
    /// <returns>A disposable that clears the event context when disposed.</returns>
    public static IDisposable IncludeSession(this ISession session)
    {
        EventContext.Current = new EventContext
        {
            Include = [session]
        };

        return Disposable.Create(() => EventContext.Current = null);
    }

    /// <summary>
    /// Sets the event context to deliver events only to the specified sessions in the current async context.
    /// </summary>
    /// <param name="sessions">The sessions to include.</param>
    /// <returns>A disposable that clears the event context when disposed.</returns>
    public static IDisposable IncludeSessions(this IEnumerable<ISession> sessions)
    {
        EventContext.Current = new EventContext
        {
            Include = sessions.ToImmutableArray()
        };

        return Disposable.Create(() => EventContext.Current = null);
    }
}