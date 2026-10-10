using System.Text.RegularExpressions;

namespace GoymenERP.ArchitectureTests;

public sealed partial class PlatformTenantQueryFilterBypassGuardTests
{
    private const string CounterpartyDbContextPath =
        "src/backend/Modules/Counterparty/"
        + "GoymenERP.Modules.Counterparty.Infrastructure/Persistence/CounterpartyDbContext.cs";
    private const string ReviewedCounterpartyDbContextFingerprint =
        "b11fc999edc6b7f1c93d3c38288e9883c58e54a1b8db9d9565a96c6f76f5d259";
    private const string ReviewedCounterpartyPurchasingRecordPartyDisplaySourcePath =
        "src/backend/Modules/Counterparty/"
        + "GoymenERP.Modules.Counterparty.Infrastructure/Persistence/"
        + "CounterpartyPurchasingRecordPartyDisplaySourceV1.cs";
    private static readonly IReadOnlyDictionary<string, ReviewedRuntimeDatabaseBypassSource>
        ReviewedCounterpartyRuntimeDatabaseBypassSources =
            new Dictionary<string, ReviewedRuntimeDatabaseBypassSource>(StringComparer.Ordinal)
            {
                // PUR-P05: one parameterized, company-filtered record-bound Cari read. Historical same-company
                // Party/account displays survive deactivation without becoming a selection surface. The exact
                // source and database API counts keep this narrower than a general SQL or generic Party read.
                [ReviewedCounterpartyPurchasingRecordPartyDisplaySourcePath] =
                    new(
                        "0c47607b901edaf39127fcf080ab473dd8d17aa8bad1edd56b097326a807b703",
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            ["System.Data.Common"] = 1,
                            ["GetDbConnection"] = 1,
                            ["CreateCommand"] = 1,
                            ["DbCommand"] = 1
                        })
            };

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

    [Fact]
    public void Counterparty_reviewed_direct_SQL_source_matches_exact_fingerprint_and_API_inventory()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            ReviewedCounterpartyPurchasingRecordPartyDisplaySourcePath.Replace(
                '/',
                Path.DirectorySeparatorChar)));
        var exactSources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ReviewedCounterpartyPurchasingRecordPartyDisplaySourcePath] = source
        };

        Assert.Empty(ValidateNoDirectDatabaseBypassCapabilities(
            exactSources,
            requireReviewedSources: true,
            reviewedSources: ReviewedCounterpartyRuntimeDatabaseBypassSources));

        exactSources[ReviewedCounterpartyPurchasingRecordPartyDisplaySourcePath] =
            source + "\n// fingerprint drift";
        Assert.Contains(
            ValidateNoDirectDatabaseBypassCapabilities(
                exactSources,
                requireReviewedSources: true,
                reviewedSources: ReviewedCounterpartyRuntimeDatabaseBypassSources),
            error => error.Contains("does not match the exact fingerprint", StringComparison.Ordinal));
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
