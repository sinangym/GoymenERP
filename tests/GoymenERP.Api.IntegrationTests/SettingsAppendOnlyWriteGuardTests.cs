using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SettingsAppendOnlyWriteGuardTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Every_mapped_Settings_tenant_row_requires_its_resolved_company(bool resolved, bool synchronous)
    {
        await using var model = Create(TestCompanyIds.CompanyA);
        var tenantTypes = model.Model.GetEntityTypes()
            .Where(entity => entity.ClrType.Assembly == typeof(SettingsDbContext).Assembly
                && entity.FindProperty("CompanyId") is not null)
            .Select(entity => entity.ClrType).ToArray();
        Assert.NotEmpty(tenantTypes);
        foreach (var type in tenantTypes)
        {
            var company = new CompanyContext();
            if (resolved) company.Set(TestCompanyIds.CompanyA);
            await using var database = new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, company);
            var row = Activator.CreateInstance(type, nonPublic: true)!;
            type.GetProperty("CompanyId")!.SetValue(row, resolved ? TestCompanyIds.CompanyB : TestCompanyIds.CompanyA);
            database.Add(row);
            var error = synchronous
                ? Assert.Throws<InvalidOperationException>(() => database.SaveChanges())
                : await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
            Assert.Equal("Ayarlar kaydı yalnız etkin şirket bağlamında yazılır.", error.Message);
        }
    }

    [Fact]
    public async Task Added_policy_history_families_allow_appends_but_reject_update_and_delete()
    {
        Type[] immutableTypes =
        [
            typeof(BankCsvMappingVersionRow), typeof(SalesPolicyBaselineTransferRow),
            typeof(SalesDeliveryToleranceVersionRow), typeof(SalesApprovalAmountThresholdVersionRow),
            typeof(SalesQuoteLossReasonListVersionRow), typeof(SalesQuoteLossReasonEntryRow),
            typeof(SalesQuoteDefaultValidityVersionRow), typeof(EInvoiceScenarioCodeListVersionRow),
            typeof(EInvoiceScenarioCodeRow), typeof(EInvoiceScenarioDefaultVersionRow),
            typeof(SegregationOfDutiesPolicyVersionRow), typeof(TaxRateListVersionRow),
            typeof(TaxRateListEntryRow), typeof(TaxRateListWithdrawalRow), typeof(UnitConversionPolicyVersionRow),
            typeof(UnitProfileVersionRow), typeof(UnitProfileBaselineTransferRow), typeof(WorkCenterCapacityVersionRow),
            typeof(LoginRateLimitVersionRow),
        ];
        foreach (var type in immutableTypes)
        {
            await using var database = Create(TestCompanyIds.CompanyA);
            var row = Activator.CreateInstance(type, nonPublic: true)!;
            type.GetProperty("CompanyId")?.SetValue(row, TestCompanyIds.CompanyA);
            database.Add(row);
            Assert.Equal(1, await database.SaveChangesAsync());
            Assert.Equal(0, await database.SaveChangesAsync());
            var entry = database.Entry(row);
            var property = entry.Properties.First(value => value.Metadata.ClrType == typeof(string)
                && !value.Metadata.IsKey());
            property.CurrentValue = "changed";
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
            entry.State = EntityState.Unchanged;
            database.Remove(row);
            Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
        }
    }

    [Fact]
    public async Task Tax_rate_list_header_remains_mutable_within_its_company()
    {
        await using var database = Create(TestCompanyIds.CompanyA);
        var row = new TaxRateListRow { CompanyId = TestCompanyIds.CompanyA, Kind = "Vat", Version = 1,
            LastListVersion = 1, UpdatedBy = "synthetic" };
        database.Add(row);
        await database.SaveChangesAsync();
        row.Version = 2;
        row.LastListVersion = 2;
        Assert.Equal(1, await database.SaveChangesAsync());
    }

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
