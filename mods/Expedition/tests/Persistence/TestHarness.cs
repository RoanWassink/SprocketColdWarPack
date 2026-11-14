using System.Globalization;

namespace SprocketExpedition.Core.Persistence.Tests;

internal sealed class TestFailureException : Exception
{
    public TestFailureException(string message)
        : base(message)
    {
    }
}

internal static class TestRunner
{
    private static readonly List<(string Name, Action Body)> Cases = new();

    public static void Add(string name, Action body) => Cases.Add((name, body));

    public static int RunAll()
    {
        var failures = 0;
        foreach (var testCase in Cases)
        {
            try
            {
                testCase.Body();
                Console.WriteLine($"PASS {testCase.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine($"FAIL {testCase.Name}");
                Console.WriteLine($"     {exception.GetType().Name}: {exception.Message}");
                if (exception.StackTrace is not null)
                {
                    Console.WriteLine(exception.StackTrace);
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{Cases.Count - failures}/{Cases.Count} tests passed.");
        return failures == 0 ? 0 : 1;
    }
}

internal static class AssertEx
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new TestFailureException(message);
        }
    }

    public static void False(bool condition, string message) => True(!condition, message);

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new TestFailureException(
                $"{message} Expected: {expected}; actual: {actual}.");
        }
    }

    public static void NotNull<T>(T? value, string message)
        where T : class
    {
        if (value is null)
        {
            throw new TestFailureException(message);
        }
    }

    public static void SequenceEqual<T>(
        IEnumerable<T> expected,
        IEnumerable<T> actual,
        string message)
    {
        var expectedList = expected.ToList();
        var actualList = actual.ToList();
        if (expectedList.Count != actualList.Count)
        {
            throw new TestFailureException(
                $"{message} Length differs. Expected {expectedList.Count}; actual "
                + $"{actualList.Count}.");
        }

        for (var index = 0; index < expectedList.Count; index++)
        {
            if (!EqualityComparer<T>.Default.Equals(expectedList[index], actualList[index]))
            {
                throw new TestFailureException(
                    $"{message} Item {index} differs. Expected {expectedList[index]}; actual "
                    + $"{actualList[index]}.");
            }
        }
    }

    public static void Throws<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new TestFailureException(message);
    }

    public static void ContainsIssue(RunLoadResult result, string code, string context)
    {
        if (!result.Issues.Any(issue => issue.Code == code))
        {
            throw new TestFailureException(
                $"{context}: expected load issue '{code}', got "
                + $"[{string.Join(", ", result.Issues.Select(issue => issue.Code))}] "
                + $"(status {result.Status}).");
        }
    }

    public static void ContainsIssue(RunStoreWriteResult result, string code, string context)
    {
        if (!result.Issues.Any(issue => issue.Code == code))
        {
            throw new TestFailureException(
                $"{context}: expected write issue '{code}', got "
                + $"[{string.Join(", ", result.Issues.Select(issue => issue.Code))}] "
                + $"(status {result.Status}).");
        }
    }

    public static void HasIssue(
        IReadOnlyList<RunStoreIssue> issues,
        string code,
        string context)
    {
        if (!issues.Any(issue => issue.Code == code))
        {
            throw new TestFailureException(
                $"{context}: expected issue '{code}', got "
                + $"[{string.Join(", ", issues.Select(issue => issue.Code))}].");
        }
    }

    public static string DecimalText(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);
}

internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        var testBase = Path.Combine(AppContext.BaseDirectory, "test-temp");
        Directory.CreateDirectory(testBase);
        Root = Path.Combine(
            testBase,
            "sprocket-expedition-3b-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string StoreRoot => Path.Combine(Root, "store");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover test directory must not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
            // A leftover test directory must not fail the test run.
        }
    }
}
