using System.Collections.Immutable;

namespace Sigurn.Rpc.Tests;

static class ListExtensions
{
    public static void AddWithLock<T>(this IList<T> list, T value)
    {
        lock(list)
            list.Add(value);
    }

    public static ImmutableArray<T> ToImmutableArrayWithLock<T>(this IList<T> list)
    {
        lock(list)
            return list.ToImmutableArray();
    }

    public static T[] ToArrayWithLock<T>(this IList<T> list)
    {
        lock(list)
            return list.ToArray();
    }
}