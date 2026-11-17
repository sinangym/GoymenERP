using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SettingsFinancePolicyPersistenceModelTests
{
    [Fact]
    public void Finance_policy_values_are_typed_company_keyed_filtered_versioned_and_never_defaulted()
    {
        using var db = Database(); var model = db.GetService<IDesignTimeModel>().Model;
        foreach (var name in new[] { "FinanceApprovalPolicies", "FinanceApprovalPolicyRules", "CashCountDifferencePolicies" })
        {
            var entity = Assert.Single(model.GetEntityTypes(), e => e.GetTableName() == name);
            Assert.Equal("settings", entity.GetSchema()); Assert.Equal("CompanyId", entity.FindPrimaryKey()!.Properties[0].Name);
            Assert.Single(entity.GetDeclaredQueryFilters()); Assert.DoesNotContain(entity.GetProperties(), p => p.Name == "ValueJson");
            if (name != "FinanceApprovalPolicyRules") Assert.True(entity.FindProperty("StateVersion")!.IsConcurrencyToken);
            if (entity.FindProperty("AmountThreshold") is { } threshold)
            { Assert.Null(threshold.FindAnnotation("Relational:DefaultValue")); Assert.Null(threshold.FindAnnotation("Relational:DefaultValueSql")); }
        }
    }

    [Fact]
    public void Prepared_values_and_published_history_cannot_be_rewritten_or_deleted_even_before_sql_is_opened()
    {
        using var db = Database(); var row = new FinanceApprovalPolicyRow { CompanyId = TestCompanyIds.CompanyA, PolicyId = Guid.NewGuid(),
            State = "Published", StateVersion = 2, Version = 1, PreparedBy = "Synthetic", PreparedAtUtc = DateTimeOffset.UtcNow,
            PublishedBy = "Synthetic", PublishedAtUtc = DateTimeOffset.UtcNow, Reason = "Synthetic", LastReason = "Synthetic", ValidFrom = new DateOnly(2026, 11, 18) };
        db.Attach(row); row.ValidFrom = row.ValidFrom.AddDays(1);
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges()); db.ChangeTracker.Clear();
        db.Attach(row); db.Remove(row); Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }
    private static SettingsDbContext Database()
    { var company = new CompanyContext(); company.Set(TestCompanyIds.CompanyA); return new(new DbContextOptionsBuilder<SettingsDbContext>().UseSqlServer("Server=invalid;Database=Synthetic;Integrated Security=true;TrustServerCertificate=true").Options, company); }
}
