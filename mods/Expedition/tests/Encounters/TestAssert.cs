namespace EncountersTests;

internal sealed class TestFailureException : Exception
{
    public TestFailureException(string message)
        : base(message)
    {
    }
}

internal static class TestAssert
{
    public static void True(bool condition, string message = "expected true")
    {
        if (!condition)
        {
            throw new TestFailureException(message);
        }
    }

    public static void False(bool condition, string message = "expected false")
    {
        if (condition)
        {
            throw new TestFailureException(message);
        }
    }

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new TestFailureException(
                message ?? $"expected '{expected}', actual '{actual}'");
        }
    }

    public static void NotEqual<T>(T unexpected, T actual, string? message = null)
    {
        if (EqualityComparer<T>.Default.Equals(unexpected, actual))
        {
            throw new TestFailureException(
                message ?? $"did not expect '{actual}'");
        }
    }

    public static void Null(object? value, string? message = null)
    {
        if (value is not null)
        {
            throw new TestFailureException(message ?? $"expected null, actual '{value}'");
        }
    }

    public static T NotNull<T>(T? value, string? message = null)
        where T : class
    {
        if (value is null)
        {
            throw new TestFailureException(message ?? "expected a non-null value");
        }

        return value;
    }

    public static void Contains(string expectedSubstring, string actual)
    {
        if (!actual.Contains(expectedSubstring, StringComparison.Ordinal))
        {
            throw new TestFailureException(
                $"expected text containing '{expectedSubstring}', actual '{actual}'");
        }
    }

    public static void ContainsError(
        IEnumerable<string> errors,
        string expectedSubstring)
    {
        var list = errors.ToArray();
        if (!list.Any(error => error.Contains(
            expectedSubstring, StringComparison.Ordinal)))
        {
            throw new TestFailureException(
                $"expected an error containing '{expectedSubstring}', actual: {string.Join(" | ", list)}");
        }
    }

    public static void InRange(
        int inclusiveMin,
        int inclusiveMax,
        int actual,
        string? message = null)
    {
        if (actual < inclusiveMin || actual > inclusiveMax)
        {
            throw new TestFailureException(
                message ?? $"expected {actual} in [{inclusiveMin}, {inclusiveMax}]");
        }
    }

    public static T Throws<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new TestFailureException(
                $"expected {typeof(T).Name}, got {exception.GetType().Name}: {exception.Message}");
        }

        throw new TestFailureException($"expected {typeof(T).Name}, no exception thrown");
    }
}
