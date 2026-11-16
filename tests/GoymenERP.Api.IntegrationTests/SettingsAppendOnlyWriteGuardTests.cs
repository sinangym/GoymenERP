using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SettingsAppendOnlyWriteGuardTests
{
    [Fact]
    public async Task Own_company_appends_and_loaded_unchanged_rows_remain_allowed()
    {
        await using var database = Create(TestCompanyIds.CompanyA);
        foreach (var row in Rows(TestCompanyIds.CompanyA)) database.Add(row);
        Assert.Equal(7, await database.SaveChangesAsync());
        Assert.Equal(0, await database.SaveChangesAsync());
        database.Add(new CollateralTypeVersionRow { CompanyId = TestCompanyIds.CompanyA, EntryId = EntryId, Version = 2, Name = "Second", RatePercent = "85", RecordedBy = "synthetic", Reason = "synthetic" });
        Assert.Equal(1, await database.SaveChangesAsync());
    }

    [Fact]
    public async Task Every_new_and_existing_append_only_row_rejects_a_foreign_company_insert()
    {
        foreach (var row in Rows(TestCompanyIds.CompanyB))
        {
            await using var database = Create(TestCompanyIds.CompanyA);
            database.Add(row);
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Persisted_policy_mapping_reference_and_collateral_rows_reject_mutation_and_deletion()
    {
        foreach (var row in Rows(TestCompanyIds.CompanyA))
        {
            await using var database = Create(TestCompanyIds.CompanyA);
            database.Add(row);
            await database.SaveChangesAsync();
            var property = database.Entry(row).Properties.First(value => value.Metadata.Name == (row is ReferenceListEntryRow or CollateralTypeRow ? "CreatedBy" : "Reason"));
            property.CurrentValue = "changed";
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
            database.Entry(row).State = EntityState.Unchanged;
            database.Remove(row);
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task The_resolved_company_is_required_even_for_an_otherwise_valid_append()
    {
        await using var database = new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new CompanyContext());
        database.Add(Rows(TestCompanyIds.CompanyA).First());
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
    }

    private static readonly Guid EntryId = new("50148881-10d8-44a7-80f3-2bb2af3bc5cd");
    private static IEnumerable<object> Rows(Guid company)
    {
        yield return new CompanyPolicyVersionRow { CompanyId = company, PolicyKey = "finance.opening-period", Version = 1, BooleanValue = false, RecordedBy = "synthetic", Reason = "synthetic" };
        yield return new CompanyPolicyVersionRow { CompanyId = company, PolicyKey = "sales.contract-discount-limit", Version = 1, PercentValue = 0, RecordedBy = "synthetic", Reason = "synthetic" };
        yield return new FinanceCreditAccountMappingVersionRow { CompanyId = company, Purpose = 1, Version = 1, LedgerAccountCode = "300", RecordedBy = "synthetic", Reason = "synthetic" };
        yield return new ReferenceListEntryRow { CompanyId = company, EntryId = EntryId, Kind = "ExpenseType", Code = "SYNTHETIC", CreatedBy = "synthetic" };
        yield return new ReferenceListEntryVersionRow { CompanyId = company, EntryId = EntryId, Version = 1, Name = "Synthetic", RecordedBy = "synthetic", Reason = "synthetic" };
        yield return new CollateralTypeRow { CompanyId = company, EntryId = EntryId, Code = "SYNTHETIC", Kind = "Mortgage", CreatedBy = "synthetic" };
        yield return new CollateralTypeVersionRow { CompanyId = company, EntryId = EntryId, Version = 1, Name = "Synthetic", RatePercent = "100", RecordedBy = "synthetic", Reason = "synthetic" };
    }
    private static SettingsDbContext Create(Guid company)
    {
        var context = new CompanyContext(); context.Set(company);
        return new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, context);
    }
}
