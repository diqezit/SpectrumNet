#nullable enable

namespace SpectrumNet.Service.ErrorHandlers;

public static class Extensions
{
    public static T? AsOrNull<T>(this object obj) where T : class => obj as T;

    public static void Apply<T>(this T? obj, Action<T> action) where T : class
    {
        if (obj != null) action(obj);
    }

    public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
    {
        foreach (var item in source)
            action(item);
    }

    public static bool IsOneOf<T>(this T value, params T[] values) => values.Contains(value);

    public static T With<T>(this T obj, Action<T> action)
    {
        action(obj);
        return obj;
    }

    public static async Task<T> WithAsync<T>(this T obj, Func<T, Task> action)
    {
        await action(obj);
        return obj;
    }
}
