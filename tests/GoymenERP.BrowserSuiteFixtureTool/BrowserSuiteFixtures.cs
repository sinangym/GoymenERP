using System.Reflection;

namespace GoymenERP.BrowserSuiteFixtureTool;

/// <summary>
/// Finds the suite owners' seeds by type. Fail-closed: a seed must be a sealed class with a public
/// parameterless constructor in GoymenERP.BrowserSuiteFixtureTool.Suites.&lt;Suite&gt; whose
/// <see cref="IBrowserSuiteFixture.Suite"/> is that namespace's suite, and a suite has at most one.
/// </summary>
public static class BrowserSuiteFixtures
{
    public const string SuitesNamespace = "GoymenERP.BrowserSuiteFixtureTool.Suites";

    public static IReadOnlyDictionary<string, IBrowserSuiteFixture> Discover(Assembly assembly)
    {
        var fixtures = new SortedDictionary<string, IBrowserSuiteFixture>(StringComparer.Ordinal);
        foreach (var type in assembly.GetTypes()
                     .Where(type => typeof(IBrowserSuiteFixture).IsAssignableFrom(type) && !type.IsInterface)
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            var suiteNamespace = type.Namespace ?? string.Empty;
            if (!suiteNamespace.StartsWith(SuitesNamespace + ".", StringComparison.Ordinal)
                || suiteNamespace.Count(character => character == '.') != SuitesNamespace.Count(character => character == '.') + 1)
            {
                throw new InvalidOperationException(
                    $"Browser suite fixture {type.FullName} must live in {SuitesNamespace}.<Suite>.");
            }

            if (!type.IsSealed || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is null)
            {
                throw new InvalidOperationException(
                    $"Browser suite fixture {type.FullName} must be sealed with a public parameterless constructor.");
            }

            var fixture = (IBrowserSuiteFixture)Activator.CreateInstance(type)!;
            var expectedSuite = suiteNamespace[(SuitesNamespace.Length + 1)..].ToLowerInvariant();
            if (!string.Equals(fixture.Suite, expectedSuite, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Browser suite fixture {type.FullName} declares suite '{fixture.Suite}' "
                    + $"but lives in the '{expectedSuite}' namespace.");
            }

            if (!fixtures.TryAdd(fixture.Suite, fixture))
            {
                throw new InvalidOperationException(
                    $"Browser suite '{fixture.Suite}' has more than one fixture.");
            }
        }

        return fixtures;
    }
}
