namespace GoymenERP.BrowserSuiteFixtureTool;

/// <summary>
/// One browser suite's seed, written by the suite owner (single writer) under Suites/&lt;suite&gt;/, in the
/// namespace GoymenERP.BrowserSuiteFixtureTool.Suites.&lt;Suite&gt;. It is found by type
/// (<see cref="BrowserSuiteFixtures"/>), so no shared list is edited when a suite adds its seed.
/// <para>
/// A seed reaches the system only through <see cref="BrowserSuiteFixtureContext"/>: the real HTTP
/// endpoints as a declared synthetic actor (authorization, company context, idempotency and audit
/// apply exactly as for the browser), or a real service from <see cref="BrowserSuiteFixtureContext.Services"/>
/// for an owner write port that has no endpoint. It never writes with SQL.
/// </para>
/// </summary>
public interface IBrowserSuiteFixture
{
    /// <summary>The suite id, exactly as in tools/validate_browser_acceptance.py SUITES.</summary>
    string Suite { get; }

    /// <summary>
    /// Seeds the suite's data and returns the values the browser suite reads, exported to it as
    /// BROWSER_E2E_&lt;SUITE&gt;_SEED. Keys come from the suite's closed list in the validator; values are
    /// GUIDs or the suite's actor ids only.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> SeedAsync(
        BrowserSuiteFixtureContext context,
        CancellationToken cancellationToken);
}
