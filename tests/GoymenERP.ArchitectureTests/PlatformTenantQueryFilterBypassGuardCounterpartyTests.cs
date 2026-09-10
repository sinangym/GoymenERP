using System.Text.RegularExpressions;

namespace GoymenERP.ArchitectureTests;

public sealed partial class PlatformTenantQueryFilterBypassGuardTests
{
    private const string CounterpartyDbContextPath =
        "src/backend/Modules/Counterparty/"
        + "GoymenERP.Modules.Counterparty.Infrastructure/Persistence/CounterpartyDbContext.cs";
    private const string ReviewedCounterpartyDbContextFingerprint =
        "5e710ed2c08927ef9eb2fbbacd7b122e74089bc051abfcef972c471b255e2db2";

    [Fact]
    public void Counterparty_command_execution_filter_and_no_bypass_surface_are_locked()
    {
        var repositoryRoot = FindRepositoryRoot();
        var dbContextSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            CounterpartyDbContextPath.Replace('/', Path.DirectorySeparatorChar)));

        var filterErrors = ValidateCounterpartyCommandExecutionTenantFilter(dbContextSource);
        Assert.True(filterErrors.Count == 0, string.Join("\n", filterErrors));

        var counterpartyRoot = Path.Combine(
            repositoryRoot,
            ExcludedCounterpartyPrefix.Replace('/', Path.DirectorySeparatorChar));
        var counterpartySources = Directory.EnumerateFiles(
                counterpartyRoot,
                "*.cs",
                SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = NormalizeRepositoryPath(Path.GetRelativePath(repositoryRoot, path)),
                Source = File.ReadAllText(path),
            })
            .Where(file => !IsBuildOutput(file.Path))
            .ToDictionary(file => file.Path, file => file.Source, StringComparer.Ordinal);

        Assert.Empty(FindOccurrences(counterpartySources));
    }

    [Theory]
    [InlineData(
        "x => x.CompanyId == companyContext.CompanyId",
        "x => true")]
    [InlineData(
        "x => x.CompanyId == companyContext.CompanyId",
        "x => x.CompanyId != companyContext.CompanyId")]
    [InlineData(
        ".HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);",
        ";")]
    public void Weakening_counterparty_command_execution_filter_is_rejected(
        string expectedSource,
        string replacement)
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            CounterpartyDbContextPath.Replace('/', Path.DirectorySeparatorChar)));
        var commandExecutionFilterIndex = source.IndexOf(
            "modelBuilder.Entity<CommandExecution>()",
            StringComparison.Ordinal);
        Assert.True(commandExecutionFilterIndex >= 0);
        var prefix = source[..commandExecutionFilterIndex];
        var commandExecutionBlock = source[commandExecutionFilterIndex..];
        Assert.Contains(expectedSource, commandExecutionBlock, StringComparison.Ordinal);
        var mutated = prefix + commandExecutionBlock.Replace(
            expectedSource,
            replacement,
            StringComparison.Ordinal);

        var errors = ValidateCounterpartyCommandExecutionTenantFilter(mutated);

        Assert.Contains(errors, error => error.Contains(
            "CommandExecution CompanyId query filter",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Adding_an_effective_filter_after_a_safe_decoy_is_rejected()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            CounterpartyDbContextPath.Replace('/', Path.DirectorySeparatorChar)));
        const string safeFilter =
            "modelBuilder.Entity<CommandExecution>()\n"
            + "            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);";
        Assert.Contains(safeFilter, source, StringComparison.Ordinal);
        var mutated = source.Replace(
            safeFilter,
            safeFilter
            + "\n        modelBuilder.Entity<CommandExecution>()\n"
            + "            .HasQueryFilter(x => true);",
            StringComparison.Ordinal);

        var errors = ValidateCounterpartyCommandExecutionTenantFilter(mutated);

        Assert.Contains(errors, error => error.Contains(
            "reviewed fingerprint",
            StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> ValidateCounterpartyCommandExecutionTenantFilter(
        string source)
    {
        var errors = new List<string>();
        var searchable = MaskCommentsAndNonInterpolatedLiterals(source);
        var compact = Regex.Replace(searchable, @"\s+", string.Empty);
        const string expectedFilter =
            "modelBuilder.Entity<CommandExecution>()"
            + ".HasQueryFilter(x=>x.CompanyId==companyContext.CompanyId);";
        var occurrenceCount = CountOrdinalOccurrences(compact, expectedFilter);
        if (occurrenceCount != 1)
        {
            errors.Add(
                "CounterpartyDbContext must contain exactly one reviewed "
                + "CommandExecution CompanyId query filter; found "
                + occurrenceCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ".");
        }

        var actualFingerprint = ComputeReviewedSourceFingerprint(source);
        if (!string.Equals(
                actualFingerprint,
                ReviewedCounterpartyDbContextFingerprint,
                StringComparison.Ordinal))
        {
            errors.Add(
                "CounterpartyDbContext does not match the reviewed fingerprint. Expected "
                + ReviewedCounterpartyDbContextFingerprint
                + ", found "
                + actualFingerprint
                + ".");
        }

        return errors;
    }

    private static int CountOrdinalOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
