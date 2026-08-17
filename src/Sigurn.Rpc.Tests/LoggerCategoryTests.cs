using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Sigurn.Rpc.Tests;

/// <summary>
/// A logger created for the wrong type files its lines under someone else's category, which breaks
/// both reading the log and filtering it. The mistake is invisible at the call site, so it is checked
/// here instead.
/// </summary>
public class LoggerCategoryTests
{
    [Fact]
    public void EveryLogger_ReportsTheCategoryOfTheTypeThatOwnsIt()
    {
        var assembly = typeof(RpcLogging).Assembly;
        var wrong = new List<string>();

        foreach (var type in assembly.GetTypes())
        {
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (!typeof(ILogger).IsAssignableFrom(field.FieldType)) continue;

                var logger = field.GetValue(null);
                if (logger is null) continue;

                // RpcLogging hands out a forwarding ILogger<T>; T is the category it will report.
                var loggerType = logger.GetType();
                if (!loggerType.IsGenericType) continue;

                var category = loggerType.GetGenericArguments()[0];

                // A nested helper may share the category of the type it belongs to, but nothing else.
                if (category == type || category == Outermost(type)) continue;

                wrong.Add($"{type.FullName}.{field.Name} logs as {category.FullName}");
            }
        }

        Assert.Empty(wrong);
    }

    private static Type Outermost(Type type)
    {
        while (type.DeclaringType is { } declaring)
            type = declaring;

        return type;
    }
}
