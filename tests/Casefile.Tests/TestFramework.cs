using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Casefile.Tests;

/// <summary>Marks a public method (void or Task, no parameters) as a test.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute;

public sealed class AssertionException(string message) : Exception(message);

public static class Assert
{
    public static void True(bool condition, string? message = null, [CallerArgumentExpression(nameof(condition))] string? expr = null)
    {
        if (!condition) throw new AssertionException(message ?? $"Expected true: {expr}");
    }

    public static void False(bool condition, string? message = null, [CallerArgumentExpression(nameof(condition))] string? expr = null)
    {
        if (condition) throw new AssertionException(message ?? $"Expected false: {expr}");
    }

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertionException($"{message}{(message is null ? "" : ": ")}expected <{expected}> but was <{actual}>");
    }

    public static void Near(double expected, double actual, double tolerance = 1e-3, string? message = null)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new AssertionException($"{message}{(message is null ? "" : ": ")}expected {expected} +/- {tolerance} but was {actual}");
    }

    public static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string? message = null)
    {
        var e = expected.ToList();
        var a = actual.ToList();
        if (!e.SequenceEqual(a))
            throw new AssertionException($"{message}{(message is null ? "" : ": ")}expected [{string.Join(", ", e)}] but was [{string.Join(", ", a)}]");
    }

    public static void Contains<T>(T item, IEnumerable<T> collection, string? message = null)
    {
        var list = collection.ToList();
        if (!list.Contains(item))
            throw new AssertionException($"{message}{(message is null ? "" : ": ")}<{item}> not found in [{string.Join(", ", list)}]");
    }

    public static void DoesNotContain<T>(T item, IEnumerable<T> collection, string? message = null)
    {
        if (collection.Contains(item)) throw new AssertionException($"{message}{(message is null ? "" : ": ")}<{item}> should not be present");
    }

    public static TException Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException ex) { return ex; }
        catch (Exception ex) { throw new AssertionException($"Expected {typeof(TException).Name} but got {ex.GetType().Name}: {ex.Message}"); }
        throw new AssertionException($"Expected {typeof(TException).Name} but nothing was thrown");
    }
}

/// <summary>
/// Minimal test runner: finds public methods marked [Test] on public classes in this assembly, runs each on a
/// fresh instance (async tests are awaited, with a timeout), and disposes the instance if it is disposable.
/// Optional first argument filters tests by substring of "Class.Method".
/// </summary>
public static class TestRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        var filter = args.FirstOrDefault();
        var tests = typeof(TestRunner).Assembly.GetTypes()
            .Where(t => t.IsPublic && t.IsClass && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<TestAttribute>() is not null)
                .Select(m => (Type: t, Method: m, Name: $"{t.Name}.{m.Name}")))
            .Where(x => filter is null || x.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToList();

        var failures = new List<(string Name, Exception Error)>();
        var total = Stopwatch.StartNew();
        foreach (var (type, method, name) in tests)
        {
            if (method.GetParameters().Length != 0) { failures.Add((name, new InvalidOperationException("Test methods must take no parameters."))); continue; }
            var sw = Stopwatch.StartNew();
            try
            {
                var instance = Activator.CreateInstance(type)!;
                try
                {
                    var result = method.Invoke(instance, null);
                    if (result is Task task) await task.WaitAsync(TimeSpan.FromSeconds(60));
                }
                finally
                {
                    if (instance is IAsyncDisposable ad) await ad.DisposeAsync();
                    else if (instance is IDisposable d) d.Dispose();
                }
                Console.WriteLine($"  PASS  {name} ({sw.ElapsedMilliseconds} ms)");
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException { InnerException: { } ie } ? ie : ex;
                failures.Add((name, inner));
                Console.WriteLine($"  FAIL  {name} ({sw.ElapsedMilliseconds} ms)");
                Console.WriteLine($"        {inner.GetType().Name}: {inner.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{tests.Count - failures.Count} passed, {failures.Count} failed, {tests.Count} total in {total.Elapsed.TotalSeconds:F1} s");
        if (failures.Count > 0)
        {
            Console.WriteLine();
            foreach (var (name, error) in failures)
            {
                Console.WriteLine($"--- {name}");
                Console.WriteLine(error.ToString());
            }
        }
        return tests.Count == 0 ? 2 : failures.Count == 0 ? 0 : 1;
    }
}
