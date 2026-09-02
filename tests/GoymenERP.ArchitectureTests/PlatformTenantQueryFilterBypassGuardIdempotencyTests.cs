using System.Text.RegularExpressions;

namespace GoymenERP.ArchitectureTests;

public sealed partial class PlatformTenantQueryFilterBypassGuardTests
{
    private const string CommandExecutionDbContextPath =
        "src/backend/Modules/Idempotency/"
        + "GoymenERP.Modules.Idempotency.Infrastructure/Persistence/CommandExecutionDbContext.cs";
    private const string OrganizationDbContextCommandExecutionPath =
        "src/backend/Modules/Organization/"
        + "GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationDbContext.cs";

    [Theory]
    [InlineData(CommandExecutionDbContextPath)]
    [InlineData(OrganizationDbContextCommandExecutionPath)]
    public void Command_execution_context_company_filter_is_semantically_locked(string sourcePath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            sourcePath.Replace('/', Path.DirectorySeparatorChar)));

        var errors = ValidateCommandExecutionTenantFilter(source);

        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Theory]
    [InlineData(
        CommandExecutionDbContextPath,
        "x => x.CompanyId == companyContext.CompanyId",
        "x => true")]
    [InlineData(
        OrganizationDbContextCommandExecutionPath,
        "x => x.CompanyId == companyContext.CompanyId",
        "x => true")]
    [InlineData(
        CommandExecutionDbContextPath,
        "x => x.CompanyId == companyContext.CompanyId",
        "x => x.CompanyId != companyContext.CompanyId")]
    [InlineData(
        OrganizationDbContextCommandExecutionPath,
        "x => x.CompanyId == companyContext.CompanyId",
        "x => x.CompanyId != companyContext.CompanyId")]
    [InlineData(
        CommandExecutionDbContextPath,
        "x => x.CompanyId == companyContext.CompanyId",
        "x => x.CompanyId == Guid.Empty")]
    [InlineData(
        OrganizationDbContextCommandExecutionPath,
        "x => x.CompanyId == companyContext.CompanyId",
        "x => x.CompanyId == Guid.Empty")]
    [InlineData(
        CommandExecutionDbContextPath,
        ".HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);",
        ";")]
    [InlineData(
        OrganizationDbContextCommandExecutionPath,
        ".HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);",
        ";")]
    public void Weakening_command_execution_company_filter_is_rejected(
        string sourcePath,
        string expectedSource,
        string replacement)
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            sourcePath.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains(expectedSource, source, StringComparison.Ordinal);
        var mutated = source.Replace(expectedSource, replacement, StringComparison.Ordinal);

        var errors = ValidateCommandExecutionTenantFilter(mutated);

        Assert.Contains(errors, error => error.Contains(
            "CommandExecution CompanyId query filter",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(CommandExecutionDbContextPath)]
    [InlineData(OrganizationDbContextCommandExecutionPath)]
    public void Adding_a_later_filter_override_after_the_safe_filter_is_rejected(
        string sourcePath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            sourcePath.Replace('/', Path.DirectorySeparatorChar)));
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

        var errors = ValidateCommandExecutionTenantFilter(mutated);

        Assert.Contains(errors, error => error.Contains(
            "exactly one CommandExecution entity configuration",
            StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> ValidateCommandExecutionTenantFilter(string source)
    {
        var errors = new List<string>();
        var searchable = MaskCommentsAndNonInterpolatedLiterals(source);
        var compact = Regex.Replace(searchable, @"\s+", string.Empty);
        const string expectedCompanyContext = "ICompanyContextcompanyContext";
        if (!compact.Contains(expectedCompanyContext, StringComparison.Ordinal))
        {
            errors.Add(
                "A CommandExecution-owning DbContext must receive the request-scoped "
                + "ICompanyContext "
                + "through its constructor.");
        }

        const string expectedFilter =
            "modelBuilder.Entity<CommandExecution>()"
            + ".HasQueryFilter(x=>x.CompanyId==companyContext.CompanyId);";
        var filterCount = CountIdempotencyOccurrences(compact, expectedFilter);
        if (filterCount != 1)
        {
            errors.Add(
                "The reviewed DbContext must contain exactly one "
                + "CommandExecution CompanyId query filter; found "
                + filterCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ".");
        }

        const string entityConfiguration = "modelBuilder.Entity<CommandExecution>()";
        var entityConfigurationCount = CountIdempotencyOccurrences(compact, entityConfiguration);
        if (entityConfigurationCount != 1)
        {
            errors.Add(
                "The reviewed DbContext must contain exactly one CommandExecution entity "
                + "configuration so a later query-filter override cannot hide behind a safe "
                + "declaration; found "
                + entityConfigurationCount.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                + ".");
        }

        return errors;
    }

    private static int CountIdempotencyOccurrences(string source, string value)
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
