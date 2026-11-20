using System;
using System.Collections.Generic;
using System.Linq;

namespace OceanBaseProviderFeatureProbe;

internal sealed class CheckFailedException(string message) : Exception(message);

internal static class Assert
{
    internal static void True(bool condition)
    {
        if (!condition) throw new CheckFailedException("Expected the condition to hold.");
    }

    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new CheckFailedException($"Expected {expected}, got {actual}.");
    }

    internal static void NotEqual<T>(T left, T right) => True(!EqualityComparer<T>.Default.Equals(left, right));
    internal static void Null(object? value) => True(value is null);
    internal static void Same(object? expected, object? actual) => True(ReferenceEquals(expected, actual));

    internal static T IsType<T>(object? value)
    {
        if (value is T typed && value.GetType() == typeof(T)) return typed;
        throw new CheckFailedException($"Expected {typeof(T).Name}, got {value?.GetType().Name ?? "null"}.");
    }

    internal static T Single<T>(IEnumerable<T> values)
    {
        using var items = values.GetEnumerator();
        if (items.MoveNext())
        {
            var item = items.Current;
            if (!items.MoveNext()) return item;
        }
        throw new CheckFailedException("Expected exactly one row.");
    }

    internal static void Empty<T>(IEnumerable<T> values) => True(!values.Any());

    internal static void Collection<T>(IReadOnlyList<T> values, params Action<T>[] checks)
    {
        Equal(checks.Length, values.Count);
        for (var index = 0; index < checks.Length; index++) checks[index](values[index]);
    }
}
