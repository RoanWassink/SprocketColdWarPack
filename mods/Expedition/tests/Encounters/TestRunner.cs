using System.Diagnostics;
using System.Reflection;

namespace EncountersTests;

internal static class Program
{
    public static int Main(string[] args)
    {
        string? filter = args.Length > 0 ? args[0] : null;

        var tests = typeof(Program).Assembly
            .GetTypes()
            .Where(type => type.Name.EndsWith("Tests", StringComparison.Ordinal))
            .SelectMany(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.ReturnType == typeof(void)
                    && method.GetParameters().Length == 0
                    && !method.IsGenericMethodDefinition))
            .OrderBy(method => method.DeclaringType!.Name, StringComparer.Ordinal)
            .ThenBy(method => method.Name, StringComparer.Ordinal)
            .Where(method => filter is null
                || method.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (tests.Length == 0)
        {
            Console.Error.WriteLine("no tests matched");
            return 2;
        }

        int passed = 0;
        var failures = new List<string>();
        var total = Stopwatch.StartNew();

        foreach (var test in tests)
        {
            string name = $"{test.DeclaringType!.Name}.{test.Name}";
            var stopwatch = Stopwatch.StartNew();
            try
            {
                test.Invoke(null, null);
                stopwatch.Stop();
                passed++;
                Console.WriteLine($"PASS {name} ({stopwatch.ElapsedMilliseconds} ms)");
            }
            catch (TargetInvocationException exception)
            {
                stopwatch.Stop();
                var inner = exception.InnerException ?? exception;
                failures.Add($"{name}: {inner.GetType().Name}: {inner.Message}");
                Console.WriteLine(
                    $"FAIL {name} ({stopwatch.ElapsedMilliseconds} ms)");
                Console.WriteLine($"     {inner.GetType().Name}: {inner.Message}");
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                failures.Add($"{name}: {exception.GetType().Name}: {exception.Message}");
                Console.WriteLine(
                    $"FAIL {name} ({stopwatch.ElapsedMilliseconds} ms)");
                Console.WriteLine($"     {exception.GetType().Name}: {exception.Message}");
            }
        }

        total.Stop();
        Console.WriteLine();
        Console.WriteLine(
            $"{passed}/{tests.Length} tests passed in {total.ElapsedMilliseconds} ms");

        if (failures.Count == 0)
        {
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("failures:");
        foreach (var failure in failures)
        {
            Console.WriteLine($"  - {failure}");
        }

        return 1;
    }
}
