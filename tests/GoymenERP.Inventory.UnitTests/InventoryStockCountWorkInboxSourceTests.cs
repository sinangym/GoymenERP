using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.Shell;
using GoymenERP.Modules.Inventory.Infrastructure.WorkInbox;
using Microsoft.Extensions.DependencyInjection;
using F = GoymenERP.Inventory.UnitTests.InventoryWorkInboxTestFixture;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryStockCountWorkInboxSourceTests
{
    [Fact]
    public void Registration_is_single_and_purpose_is_staged_approve_only_without_browse_or_price_grants()
    {
        var services = new ServiceCollection().AddInventoryTransfers().AddInventoryStockCounts();
        Assert.Single(services, row => row.ServiceType == typeof(IWorkInboxSourceV1)
            && row.ImplementationType == typeof(InventoryStockCountWorkInboxSourceV1));
        Assert.Single(services, row => row.ServiceType == typeof(InventoryWorkInboxFence));
        var declarations = new InventoryShellCapabilityProvider().GetDeclarations();
        var purpose = Assert.Single(declarations, row => row.CapabilityId == "inventory.stock-count.work-decision");
        Assert.False(purpose.ImplementationAvailable); Assert.Null(purpose.ParentCapabilityId);
        Assert.Equal("/onaylarim/inventory.stock-count.v1/:approvalId", purpose.RoutePath);
        Assert.Equal([InventoryPermissionManifestV1.StockCountApprove], purpose.RequiredPermissions);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, purpose.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, purpose.ScopeRequirement);
        var native = Assert.Single(declarations, row => row.CapabilityId == InventoryShellCapabilityProvider.StockCountApproveCapabilityId);
        Assert.Equal(InventoryShellCapabilityProvider.StockPositionsCapabilityId, native.ParentCapabilityId);
        Assert.Contains(ErpPermissions.InventoryLabelsRead, native.RequiredPermissions);
        Assert.Contains(ErpPermissions.InventoryBalancesRead, native.RequiredPermissions);
        Assert.True(native.ImplementationAvailable);
    }

    [Fact]
    public async Task Queue_is_oldest_pending_nonzero_company_count_not_maker_and_keeps_more_without_private_reason()
    {
        var f = new F(); var row = Add(f, 1); Add(f, 2, maker: F.Approver);
        Add(f, 3, company: F.CompanyB); var zero = Add(f, 4); zero.CountedKg = zero.BookKg;
        var decided = Add(f, 5); Decide(f, decided); Add(f, 6);
        var source = new InventoryStockCountWorkInboxSourceV1(f.Rows, f.Fence);
        var answer = await source.ListPendingAsync(f.Context(), 1, default);
        Assert.Equal(row.Id, Assert.Single(answer.Items!).ItemId); Assert.True(answer.HasMore);
        Assert.Equal("/onaylarim/inventory.stock-count.v1/" + row.Id.ToString("D"), answer.Items![0].Link);
        Assert.Equal("SENTETIK-LABEL", answer.Items[0].DocumentLabel);
        Assert.Null(answer.Items[0].Scope);
        Assert.Equal("inventory-label", answer.Items[0].References[0].Role); Assert.Null(answer.Items[0].References[0].Link);
        Assert.DoesNotContain(row.Reason, JsonSerializer.Serialize(answer));
        Assert.DoesNotContain("averageCost", JsonSerializer.Serialize(answer), StringComparison.OrdinalIgnoreCase);
        Assert.All(f.Authority.Calls, call => Assert.Equal(InventoryPermissionManifestV1.StockCountApprove, call.Permission));
    }

    [Fact]
    public async Task Purpose_keeps_immutable_count_and_terminal_logical_version_without_reading_current_label()
    {
        var f = new F(); var row = Add(f, 1); var source = new InventoryStockCountWorkInboxSourceV1(f.Rows, f.Fence);
        var pending = (await source.ReadDecisionAsync(f.Context(), row.Id, default))!;
        Assert.Equal("1", pending.SourceVersion); Assert.True(pending.Pending);
        Assert.Equal("7", pending.InventoryStockCountProof!.Count.LabelVersion);
        Decide(f, row);
        Assert.Single(f.Rows.LabelRows).Version = 999;
        var terminal = (await source.ReadDecisionAsync(f.Context(), row.Id, default))!;
        Assert.Equal("2", terminal.SourceVersion); Assert.False(terminal.Pending);
        Assert.Equal(pending.InventoryStockCountProof.Count, terminal.InventoryStockCountProof!.Count);
        Assert.Equal("Approved", terminal.InventoryStockCountProof.Status);
        Assert.DoesNotContain(row.Reason, JsonSerializer.Serialize(terminal));
        Assert.DoesNotContain("inventoryStockCountProof", JsonSerializer.Serialize(terminal, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Theory]
    [InlineData("denied")] [InlineData("site-only")] [InlineData("company")] [InlineData("actor")]
    public async Task Denied_scope_reads_no_owner_rows(string fault)
    {
        var f = new F(); Add(f, 1);
        if (fault == "denied") f.Authority.Answer = (_, _) => InventoryWorkInboxTestAuthority.CompanyWide with { IsGranted = false };
        if (fault == "site-only") f.Authority.Answer = (_, _) => InventoryWorkInboxTestAuthority.SiteOnly;
        if (fault == "company") f.Company.Set(F.CompanyB);
        if (fault == "actor") f.User.UserId = "other";
        var source = new InventoryStockCountWorkInboxSourceV1(f.Rows, f.Fence);
        var answer = await source.ListPendingAsync(f.Context(), 10, default);
        Assert.Null(answer.Items); Assert.Equal(0, f.Rows.Reads);
        Assert.Null(await source.ReadDecisionAsync(f.Context(), F.Id(1), default)); Assert.Equal(0, f.Rows.Reads);
    }

    [Fact]
    public async Task Live_permission_loss_after_read_and_malformed_owner_evidence_never_return_a_partial_success()
    {
        var f = new F(); var row = Add(f, 1); var source = new InventoryStockCountWorkInboxSourceV1(f.Rows, f.Fence);
        f.Authority.Answer = (_, _) => f.Rows.Reads == 0 ? InventoryWorkInboxTestAuthority.CompanyWide
            : InventoryWorkInboxTestAuthority.CompanyWide with { IsGranted = false };
        Assert.Null((await source.ListPendingAsync(f.Context(), 10, default)).Items);
        f.Authority.Answer = (_, _) => InventoryWorkInboxTestAuthority.CompanyWide;
        row.LabelVersion = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(f.Context(), row.Id, default));
    }

    [Fact]
    public async Task Pending_query_translates_the_company_filter_and_unique_decision_absence_with_the_real_SQL_model()
    {
        var f = new F(); using var rows = new InventorySqlTranslatingWorkInboxRows(F.CompanyA);
        await new InventoryStockCountWorkInboxSourceV1(rows, f.Fence).ListPendingAsync(f.Context(), 10, default);
        var sql = Assert.Single(rows.Sql);
        Assert.Contains("[inventory].[StockCounts]", sql); Assert.Contains("[inventory].[StockCountDecisions]", sql);
        Assert.Contains("NOT EXISTS", sql); Assert.Contains("[CompanyId]", sql);
    }

    [Fact]
    public async Task Scoped_context_is_never_promoted_to_company_wide_owner_read()
    {
        var f = new F(); var row = Add(f, 1); var source = new InventoryStockCountWorkInboxSourceV1(f.Rows, f.Fence);
        var context = f.Context() with { GrantScope = new(new HashSet<Guid>(), new HashSet<Guid> { row.LocationId }, new HashSet<Guid>()) };
        Assert.Null((await source.ListPendingAsync(context, 10, default)).Items);
        Assert.Null(await source.ReadDecisionAsync(context, row.Id, default)); Assert.Equal(0, f.Rows.Reads);
    }

    private static StockCountRow Add(F f, int id, Guid? company = null, string? maker = null)
    {
        var row = new StockCountRow { Id = F.Id(id), CompanyId = company ?? F.CompanyA, LabelId = F.Id(100), LabelVersion = 7,
            OwnerKind = 1, OwnerId = F.CompanyA, CustodianKind = 1, CustodianId = F.CompanyA,
            LocationId = F.SourceLocation, ProductId = F.Id(101), Block = 1, BookKg = 100, CountedKg = 101,
            CountedAtUtc = F.T0.ToUniversalTime(), Reason = "SENTETİK özel sayım gerekçesi", RecordedBy = maker ?? F.Requester,
            RecordedAtUtc = F.T0.AddMinutes(id).ToUniversalTime(), PayloadHash = new string('a', 64), CorrelationId = "SENTETIK" };
        if (!f.Rows.LabelRows.Any(label => label.Id == row.LabelId && label.CompanyId == row.CompanyId))
            f.Rows.LabelRows.Add(new() { Id = row.LabelId, CompanyId = row.CompanyId, Code = "SENTETIK-LABEL", Version = 7, ProductId = row.ProductId });
        f.Rows.StockCountRows.Add(row); return row;
    }
    private static void Decide(F f, StockCountRow row) => f.Rows.StockCountDecisionRows.Add(new() { CompanyId = row.CompanyId,
        CountId = row.Id, Decision = 1, Reason = "Doğrulandı", MovementId = Guid.NewGuid(), DecidedBy = F.Approver,
        DecidedAtUtc = F.T0.AddHours(1).ToUniversalTime(), CorrelationId = "SENTETIK", PayloadHash = new string('b', 64) });
}
