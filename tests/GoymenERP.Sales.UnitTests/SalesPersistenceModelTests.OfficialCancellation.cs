using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
namespace GoymenERP.Sales.UnitTests;
public sealed partial class SalesPersistenceModelTests
{
    [Fact]
    public void Official_cancellation_migration_guards_all_history_before_drop_and_keeps_original_finalization_immutable()
    {
        var migration = new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddSalesOfficialInvoiceCancellation();
        var up = migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>().Select(x => x.Sql).ToArray();
        Assert.Equal(7, up.Length);
        Assert.Contains(up, sql => sql.Contains("THROW 51916,", StringComparison.Ordinal));
        Assert.Contains(up, sql => sql.Contains("THROW 51917,", StringComparison.Ordinal));
        Assert.Contains(up, sql => sql.Contains("THROW 51918,", StringComparison.Ordinal));
        Assert.Contains(up, sql => sql.Contains("TR_InvoiceFinalizations_Guard", StringComparison.Ordinal)
            && sql.Contains("i.[State] = 6", StringComparison.Ordinal));
        var first = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51919,", first.Sql, StringComparison.Ordinal);
        Assert.True(first.Sql.IndexOf("THROW 51919,", StringComparison.Ordinal) < first.Sql.IndexOf("DROP TRIGGER", StringComparison.Ordinal));
        Assert.Contains("[OfficialInvoiceCancellations]", first.Sql, StringComparison.Ordinal);
        Assert.Contains("[OfficialInvoiceCancellationAttempts]", first.Sql, StringComparison.Ordinal);
        Assert.Contains("[OfficialInvoiceCancellationDeliveries]", first.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Equal(3, migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropTableOperation>().Count());
    }

    [Fact]
    public void Official_cancellation_has_company_bound_foreign_keys_single_invoice_identity_and_append_only_delivery_and_attempt_ledgers()
    {
        using var db=Context();var model=db.GetService<IDesignTimeModel>().Model;
        foreach(var type in new[]{typeof(OfficialInvoiceCancellationRow),typeof(OfficialInvoiceCancellationAttemptRow),typeof(OfficialInvoiceCancellationDeliveryRow)})
        {var entity=model.FindEntityType(type)!;Assert.Equal("sales",entity.GetSchema());Assert.Single(entity.GetDeclaredQueryFilters());Assert.Equal("CompanyId",entity.FindPrimaryKey()!.Properties[0].Name);Assert.Contains(entity.GetDeclaredTriggers(),x=>x.GetDatabaseName()!.EndsWith("_Immutable",StringComparison.Ordinal));}
        var facts=model.FindEntityType(typeof(OfficialInvoiceCancellationRow))!;
        Assert.Contains(facts.GetIndexes(),x=>x.IsUnique&&x.Properties.Select(p=>p.Name).SequenceEqual(new[]{"CompanyId","Kind","InvoiceId"}));
        Assert.Equal(2,facts.GetForeignKeys().Count());
        Assert.Contains("7",SalesDbContext.InvoiceStateConstraint);Assert.Contains("7",SalesDbContext.SubcontractServiceInvoiceStateConstraint);
    }
    [Theory]
    [InlineData("modify")] [InlineData("delete")] [InlineData("foreign")]
    public void Official_cancellation_history_rejects_edit_delete_and_foreign_company_before_provider(string action)
    {
        using var db=Context();var row=new OfficialInvoiceCancellationRow{CompanyId=action=="foreign"?CompanyB:CompanyA,CancellationId=Guid.NewGuid(),InvoiceId=Guid.NewGuid(),Kind=1,FinalizedVersion=4,InvoiceVersion=5,AdmissionId=Guid.NewGuid(),ActorId="SENTETIK",CorrelationId="SENTETIK",ViewJson="{}",AdmissionJson="{}"};
        if(action=="foreign")db.Add(row);else{db.Attach(row);if(action=="delete")db.Remove(row);else row.ViewJson="{\"changed\":true}";}
        Assert.Throws<InvalidOperationException>(()=>db.SaveChanges());
    }
}
