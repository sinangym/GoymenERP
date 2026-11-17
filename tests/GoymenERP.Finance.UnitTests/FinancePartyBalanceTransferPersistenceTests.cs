using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinancePartyBalanceTransferPersistenceTests
{
    [Fact]
    public void Positions_and_vouchers_are_company_scoped_and_positions_have_a_restricted_same_company_voucher_reference()
    {
        using var db = Context(); var model = db.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(FinancePartyBalanceTransferRow), typeof(FinancePartyBalanceAssignmentRow), typeof(FinancePartyBalanceTransferCommandRow) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.Equal("CompanyId", entity.FindPrimaryKey()!.Properties[0].Name);
            Assert.Single(entity.GetDeclaredQueryFilters());
        }
        var position = model.FindEntityType(typeof(FinancePartyBalanceAssignmentRow))!;
        Assert.Equal(["CompanyId", "Kind", "SourceId", "CurrentAccountId"], position.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.True(position.FindProperty("Version")!.IsConcurrencyToken);
        Assert.Equal("varchar(max)", position.FindProperty("AvailableAmountText")!.GetColumnType());
        var foreign = Assert.Single(position.GetForeignKeys());
        Assert.Equal(["CompanyId", "LastTransferId"], foreign.Properties.Select(x => x.Name));
        Assert.Equal(DeleteBehavior.Restrict, foreign.DeleteBehavior);
        foreach (var type in new[] { typeof(FinanceAllocationRow), typeof(FinancePaymentAllocationRow) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.True(entity.FindProperty("AllocationPartyId")!.IsNullable);
            Assert.True(entity.FindProperty("AllocationAccountId")!.IsNullable);
            Assert.Contains(entity.GetCheckConstraints(), x => x.Name is { } name && name.EndsWith("_Beneficiary", StringComparison.Ordinal)
                && x.Sql.Contains("[AllocationPartyId] IS NULL AND [AllocationAccountId] IS NULL", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Migration_contains_no_money_data_rewrite_and_refuses_loss_before_dropping_anything()
    {
        var migration = new AddFinancePartyBalanceTransfers();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Equal(3, migration.UpOperations.OfType<CreateTableOperation>().Count());
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51984", guard, StringComparison.Ordinal);
        foreach (var name in new[] { "PartyBalanceTransfers", "PartyBalanceAssignments", "PartyBalanceTransferCommands", "Allocations", "PaymentAllocations" })
            Assert.Contains(name, guard, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Voucher_facts_and_assignment_source_identity_cannot_be_overwritten()
    {
        using (var db = Context())
        {
            var row = new FinancePartyBalanceTransferRow { CompanyId = Company, Id = Guid.NewGuid(),
                OwnerProofJson = "{}", EvidenceJson = "{}", CurrenciesJson = "[]", AssignmentSnapshotJson = "[]",
                SnapshotVersion = new string('a', 64), Reason = "SENTETIK", RecordedBy = "SENTETIK" };
            db.Attach(row); row.Reason = "Changed";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        using (var db = Context())
        {
            var row = new FinancePartyBalanceAssignmentRow { CompanyId = Company, Kind = 1, SourceId = Guid.NewGuid(),
                CurrentAccountId = Guid.NewGuid(), AvailableAmountText = "10.00", Version = 1 };
            db.Attach(row); row.OriginalAccountId = Guid.NewGuid(); row.Version++;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public void Legacy_allocation_payload_remains_identical_when_optional_beneficiary_is_absent()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var id = Guid.NewGuid(); var source = Guid.NewGuid(); var item = Guid.NewGuid();
        var receipt = JsonSerializer.Serialize(new CreateFinanceAllocationRequest(id, source, "2", item, "3", "10.00", "SENTETIK"), options);
        var oldReceipt = JsonSerializer.Serialize(new { allocationId = id, sourceId = source, expectedSourceVersion = "2", openItemId = item,
            expectedOpenItemVersion = "3", amount = "10.00", reason = "SENTETIK" }, options);
        Assert.Equal(oldReceipt, receipt);
        var payment = JsonSerializer.Serialize(new CreateFinancePaymentAllocationRequest(id, source, "2", item, "3", "10.00", "SENTETIK"), options);
        Assert.DoesNotContain("allocationAccountId", payment, StringComparison.Ordinal);
        Assert.DoesNotContain("allocationPartyId", payment, StringComparison.Ordinal);
        var supplied = JsonSerializer.Serialize(new CreateFinancePaymentAllocationRequest(id, source, "2", item, "3", "10.00", "SENTETIK", Guid.NewGuid(), Guid.NewGuid()), options);
        Assert.Contains("allocationAccountId", supplied, StringComparison.Ordinal);
        Assert.Contains("allocationPartyId", supplied, StringComparison.Ordinal);
    }

    private static readonly Guid Company = Guid.Parse("f3840010-0000-4000-8000-000000000001");
    private static FinanceDbContext Context()
    { var company = new CompanyContext(); company.Set(Company); return new(new DbContextOptionsBuilder<FinanceDbContext>()
        .UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;TrustServerCertificate=True").Options, company); }
}
