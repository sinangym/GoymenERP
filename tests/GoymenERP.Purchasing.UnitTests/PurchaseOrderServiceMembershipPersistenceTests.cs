using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseOrderServiceMembershipPersistenceTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 28, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Draft_binding_roundtrips_its_recorded_slot_and_exact_prepared_revision_without_reapproving()
    {
        var f = Fixture(); var restored = Restore(f);
        Assert.Equal((3L, PurchaseOrderStatus.Draft, At.AddMinutes(1)), (restored.Version, restored.Status, restored.UpdatedAtUtc));
        Assert.Null(restored.Approval); Assert.Empty(restored.Acceptances);
        var member = Assert.Single(restored.ServiceMemberships);
        Assert.Equal((f.Header.Id, f.Service.OrderId, 1L, f.Header.CompanyAccountId),
            (member.PurchaseOrderId, member.ServiceOrderId, member.PreparedVersion, member.CompanyAccountId));
        Assert.Equal(f.Service.Lines.Select(x => x.LineId), member.LineIds);
        var rows = PurchasingStore.ServiceMembershipRows(restored);
        Assert.Equal(JsonSerializer.Serialize(f.Members), JsonSerializer.Serialize(rows));
        Assert.DoesNotContain("serviceMembership", JsonSerializer.Serialize(restored), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PurchaseOrder.ServiceMembershipUnavailable,
            Assert.Throws<PurchasingRuleException>(() => restored.Approve(3, "checker", At)).Code);
    }

    [Theory]
    [InlineData("missing")] [InlineData("foreign")] [InlineData("version")] [InlineData("approved")]
    [InlineData("supplier")] [InlineData("account")] [InlineData("currency")] [InlineData("line")]
    public void Restore_requires_the_actual_complete_native_Draft_revision(string changed)
    {
        var f = Fixture(); var service = f.Service;
        if (changed == "foreign") service = service with { CompanyId = Guid.NewGuid() };
        if (changed == "version") service = service with { Version = 2 };
        if (changed == "approved") service = PurchaseServiceOrder.Approve(service, 1, "checker", At);
        if (changed == "supplier") service = service with { SupplierPartyId = Guid.NewGuid() };
        if (changed == "account") service = service with { CompanyAccountId = Guid.NewGuid() };
        if (changed == "currency") service = service with { Money = service.Money with { CurrencyId = Guid.NewGuid() } };
        if (changed == "line") service = service with { Lines = [service.Lines[0] with { LineId = Guid.NewGuid() }] };
        f.Prepared.SnapshotJson = JsonSerializer.Serialize(service, PurchaseServiceOrderStoreV1.Json);
        var failure = Record.Exception(() => PurchasingStore.RestoreOrder(f.Header, [f.Line], serviceMemberships: f.Members,
            preparedServices: changed == "missing" ? [] : [f.Prepared]));
        Assert.True(failure is ArgumentException or InvalidOperationException, failure?.ToString());
    }

    [Theory]
    [InlineData("parent")] [InlineData("company")] [InlineData("slot")] [InlineData("memberNo")]
    [InlineData("duplicate")] [InlineData("emptyLines")] [InlineData("lineDuplicate")] [InlineData("noncanonical")]
    [InlineData("offset")] [InlineData("approvedParent")]
    public void Malformed_or_unproved_timeline_never_substitutes_current_parent_state(string changed)
    {
        var f = Fixture(); var row = f.Members[0];
        if (changed == "parent") row.OrderId = Guid.NewGuid();
        if (changed == "company") row.CompanyId = Guid.NewGuid();
        if (changed == "slot") row.OrderVersionAfter = 4;
        if (changed == "memberNo") row.MemberNo = 2;
        if (changed == "emptyLines") row.LineIdsJson = "[]";
        if (changed == "lineDuplicate") row.LineIdsJson = JsonSerializer.Serialize(new[] { f.Service.Lines[0].LineId, f.Service.Lines[0].LineId });
        if (changed == "noncanonical") row.LineIdsJson += " ";
        if (changed == "offset") row.RecordedAtUtc = row.RecordedAtUtc.ToOffset(TimeSpan.FromHours(3));
        if (changed == "approvedParent") f.Header.Status = (int)PurchaseOrderStatus.Approved;
        var failure = Record.Exception(() => PurchasingStore.RestoreOrder(f.Header, [f.Line],
            serviceMemberships: changed == "duplicate" ? [row, row] : f.Members, preparedServices: [f.Prepared]));
        Assert.True(failure is ArgumentException or InvalidOperationException, failure?.ToString());
    }

    [Fact]
    public void Legacy_absence_is_not_a_membership_but_an_unexplained_version_gap_is_corruption()
    {
        var f = Fixture(); f.Header.Version = 2; f.Header.UpdatedAtUtc = At;
        var legacy = PurchasingStore.RestoreOrder(f.Header, [f.Line]);
        Assert.Equal(2L, legacy.Version); Assert.Empty(legacy.ServiceMemberships);
        f.Header.Version = 3;
        Assert.Throws<InvalidOperationException>(() => PurchasingStore.RestoreOrder(f.Header, [f.Line]));
    }

    [Fact]
    public void Model_pins_company_child_once_and_exact_prepared_revision_without_cascade_or_shadow_identity()
    {
        var f = Fixture(); using var db = Context(f.Header.CompanyId);
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PurchaseOrderServiceMembershipRow))!;
        Assert.Equal("PurchaseOrderServiceMemberships", entity.GetTableName()); Assert.Equal("purchasing", entity.GetSchema());
        Assert.Equal(new[] { "CompanyId", "ServiceOrderId" }, entity.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Single(entity.GetDeclaredQueryFilters());
        Assert.Contains(entity.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "CompanyId", "OrderId", "OrderVersionAfter", "MemberNo" }));
        Assert.Contains(entity.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(PurchaseOrderRow)
            && x.Properties.Select(p => p.Name).SequenceEqual(new[] { "CompanyId", "OrderId" }));
        Assert.Contains(entity.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(PurchaseServiceOrderRevisionRow)
            && x.Properties.Select(p => p.Name).SequenceEqual(new[] { "CompanyId", "ServiceOrderId", "PreparedVersion" }));
        Assert.All(entity.GetForeignKeys(), x => Assert.Equal(DeleteBehavior.Restrict, x.DeleteBehavior));
        Assert.Equal(2, entity.GetCheckConstraints().Count());
    }

    [Theory]
    [InlineData("delete")] [InlineData("rebind")] [InlineData("orphan")] [InlineData("approved")]
    public void Invalid_tracked_membership_writes_are_rejected_before_SQL(string changed)
    {
        var f = Fixture(); using var db = Context(f.Header.CompanyId); var member = f.Members[0];
        if (changed is "delete" or "rebind")
        {
            db.Attach(member);
            if (changed == "delete") db.Remove(member); else member.OrderId = Guid.NewGuid();
        }
        else
        {
            db.Attach(f.Prepared);
            if (changed == "approved") { db.Attach(f.Header); f.Header.Status = (int)PurchaseOrderStatus.Approved; }
            db.OrderServiceMemberships.Add(member);
        }
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    private static PurchaseOrder Restore(Facts f) => PurchasingStore.RestoreOrder(f.Header, [f.Line],
        serviceMemberships: f.Members, preparedServices: [f.Prepared]);
    private sealed record Facts(PurchaseOrderRow Header, PurchaseOrderLineRow Line, PurchaseServiceOrderState Service,
        IReadOnlyList<PurchaseOrderServiceMembershipRow> Members, PurchaseServiceOrderRevisionRow Prepared);
    private static Facts Fixture()
    {
        var company = Guid.NewGuid(); var supplier = Guid.NewGuid(); var account = Guid.NewGuid(); var currency = Guid.NewGuid();
        var money = PurchaseOrderMoney.Of(currency, "TRY", Guid.NewGuid(), 1, 2, 4);
        var order = PurchaseOrder.Create(Guid.NewGuid(), company, supplier, money, At, "parent-maker");
        var lineId = Guid.NewGuid(); var product = Guid.NewGuid(); var unit = Guid.NewGuid();
        order.AddLine(1, lineId, product, unit, Quantity.Of(4m, QuantityUnit.Kilogram), At);
        var service = PurchaseServiceOrder.Create(new(company, Guid.NewGuid(), 1, PurchaseServiceOrderKind.ServicePurchase,
            PurchaseServiceOrderStatus.Draft, supplier, account, "1", "1", new(2026, 11, 28), "child-maker", null, At, At,
            new(currency, "TRY", money.ProfileId, 1, 2, 4),
            [new(Guid.NewGuid(), Guid.NewGuid(), 1, "SENTETIK service", Guid.NewGuid(), "kg", Guid.NewGuid(), 1, 3, 4m, 2m, 0m)],
            [], [], [], "SENTETIK-SERVICE-1"));
        order.BindServiceOrders(2, account, [service], [new(service.OrderId, 1, service.Lines.Select(x => x.LineId).ToArray())], At.AddMinutes(1));
        var header = new PurchaseOrderRow { Id = order.Id, CompanyId = company, SupplierPartyId = supplier, CompanyAccountId = account,
            Status = (int)PurchaseOrderStatus.Draft, Version = 3, CreatedAtUtc = At, UpdatedAtUtc = At.AddMinutes(1), CreatedBy = "parent-maker",
            CurrencyId = currency, CurrencyCode = "TRY", MoneyProfileId = money.ProfileId, MoneyProfileVersion = 1, MoneyScale = 2, UnitPriceScale = 4 };
        var line = new PurchaseOrderLineRow { CompanyId = company, OrderId = order.Id, LineId = lineId, LineNo = 1,
            ProductId = product, UnitId = unit, Kind = (int)PurchaseOrderLineKind.General, UnitIsKilogram = true, Ordered = 4m };
        return new(header, line, service, PurchasingStore.ServiceMembershipRows(order), new() { CompanyId = company,
            OrderId = service.OrderId, Version = 1, SnapshotJson = JsonSerializer.Serialize(service, PurchaseServiceOrderStoreV1.Json) });
    }
    private static PurchasingDbContext Context(Guid companyId)
    {
        var company = new CompanyContext(); company.Set(companyId);
        return new(new DbContextOptionsBuilder<PurchasingDbContext>().UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }
}
