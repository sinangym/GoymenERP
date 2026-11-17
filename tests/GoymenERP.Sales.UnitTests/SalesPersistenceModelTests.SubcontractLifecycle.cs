using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesPersistenceModelTests
{
    [Fact]
    public void Subcontract_lifecycle_migration_preserves_existing_claims_and_guards_rollback_before_every_drop()
    {
        var migration = new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddSubcontractInvoiceLifecycle();
        var sql = migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>().Select(item => item.Sql).ToArray();
        Assert.Equal(5, sql.Length);
        Assert.Contains("FROM [sales].[InvoiceMatches]", sql[0], StringComparison.Ordinal);
        Assert.All(sql.Skip(1), item => Assert.Contains("THROW 51997,", item, StringComparison.Ordinal));
        var down = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51994,", down, StringComparison.Ordinal);
        Assert.True(down.IndexOf("THROW 51994,", StringComparison.Ordinal) < down.IndexOf("DROP TRIGGER", StringComparison.Ordinal));
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
    }

    [Fact]
    public void Subcontract_lifecycle_claim_and_attempt_models_have_company_keys_filters_and_immutable_history()
    {
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(SubcontractInvoiceLifecycleRow), typeof(OfficialInvoiceClaimRow), typeof(SubcontractInvoiceCommandAttemptRow) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.Equal("CompanyId", entity.FindPrimaryKey()!.Properties[0].Name);
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.Equal("sales", entity.GetSchema());
        }
        var claim = model.FindEntityType(typeof(OfficialInvoiceClaimRow))!;
        Assert.Equal(new[] { "CompanyId", "Ettn" }, claim.FindPrimaryKey()!.Properties.Select(item => item.Name));
        Assert.Contains(claim.GetIndexes(), index => index.IsUnique && index.Properties.Select(item => item.Name)
            .SequenceEqual(new[] { "CompanyId", "OfficialInvoiceNo" }));
        var lifecycle = model.FindEntityType(typeof(SubcontractInvoiceLifecycleRow))!;
        Assert.Equal(new[] { "CompanyId", "InvoiceId", "Sequence" }, lifecycle.FindPrimaryKey()!.Properties.Select(item => item.Name));
        Assert.Contains(lifecycle.GetCheckConstraints(), item => item.Sql == SalesDbContext.SubcontractLifecycleIdentityCheck);
        var header = model.FindEntityType(typeof(SubcontractServiceInvoiceRow))!;
        Assert.Contains(header.GetIndexes(), index => index.GetDatabaseName() == "UX_SubcontractServiceInvoices_Weighing"
            && index.IsUnique && index.GetFilter() == "[State] IN (1,2,3,6)");
    }

    [Theory]
    [InlineData("modify")]
    [InlineData("delete")]
    [InlineData("foreign")]
    public void Subcontract_lifecycle_evidence_cannot_be_rewritten_deleted_or_recorded_under_another_company(string action)
    {
        using var context = Context();
        var row = new SubcontractInvoiceLifecycleRow
        {
            CompanyId = CompanyA, InvoiceId = Guid.NewGuid(), Sequence = 1, InvoiceVersion = 1,
            State = 1, FactsJson = "{}", CorrelationId = "SENTETIK", RecordedAtUtc = At,
        };
        if (action == "foreign") { row.CompanyId = CompanyB; context.Add(row); }
        else
        {
            context.Attach(row);
            if (action == "delete") context.Remove(row);
            else row.FactsJson = "{\"changed\":true}";
        }
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }
}
