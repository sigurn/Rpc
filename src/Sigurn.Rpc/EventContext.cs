using System.Collections.Immutable;

namespace Sigurn.Rpc;

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

public static class SessionExtensions
{
    public static IDisposable ExcludeSession(this ISession session)
    {
        EventContext.Current = new EventContext
        {
            Exclude = [session]
        };

        return Disposable.Create(() => EventContext.Current = null);
    }

    public static IDisposable ExcludeSessions(this IEnumerable<ISession> sessions)
    {
        EventContext.Current = new EventContext
        {
            Exclude = sessions.ToImmutableArray()
        };

        return Disposable.Create(() => EventContext.Current = null);
    }

    public static IDisposable IncludeSession(this ISession session)
    {
        EventContext.Current = new EventContext
        {
            Include = [session]
        };

        return Disposable.Create(() => EventContext.Current = null);
    }

    public static IDisposable IncludeSessions(this IEnumerable<ISession> sessions)
    {
        EventContext.Current = new EventContext
        {
            Include = sessions.ToImmutableArray()
        };

        return Disposable.Create(() => EventContext.Current = null);
    }
}