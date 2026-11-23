using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// ALS-D126 on real SQL Server, end to end: the real Purchasing service and store, the real Inventory future allocation (created
/// by the Inventory writer) and the host's real Inventory release bridge, Identity's real grants. Only the supplier eligibility
/// port is a synthetic answer (the Cari owner is not under test). The revision's release joins the revision's own SQL transaction,
/// so a failure after the release leaves both undone. SENTETIK.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task ALS_D126_supplier_revision_releases_confirmed_future_allocations_atomically_through_the_real_inventory_owner()
    {
        await using Run run = await Run.StartAsync();
        var (orderId, lineId, version) = await InvoiceCostRateOrderAsync(run,
            PurchaseOrderMoney.Of(InvoiceCostRateCurrency, "TRY", InvoiceCostRateProfile, 1, 2), Day);
        var allocationId = await SeedRevisionAllocationAsync(run, orderId, lineId, version);
        var actor = Guid.Parse(run.Actor);
        await GrantRevisionActorAsync(run, actor, release: false);
        var supplier = Guid.NewGuid(); var account = Guid.NewGuid();
        RevisePurchaseOrderSupplierRequest Request(params PurchaseFutureAllocationSeenRequest[]? seen) =>
            new(version.ToString(System.Globalization.CultureInfo.InvariantCulture), supplier, account, "SENTETIK tedarikçi değişti",
                seen);
        PurchasingCommandMetadata Metadata(string key) => new($"SENTETIK-{key}", run.Correlation, null, AcceptanceTime);

        // (a) Unconfirmed: 409 purchasing_line_has_allocations, nothing written anywhere.
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        {
            var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
                RevisionService(scope.ServiceProvider, actor).ReviseOrderSupplierAsync(orderId, Request(null), Metadata("revise-unconfirmed")));
            Assert.Equal((409, PurchasingProblemCodesV1.LineHasAllocations), (refused.Status, refused.Code));
        }
        await AssertRevisionUntouchedAsync(run, orderId, version, allocationId);

        // (d) A confirmed set without purchasing.future-allocation.release: 403, nothing written.
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        {
            var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
                RevisionService(scope.ServiceProvider, actor).ReviseOrderSupplierAsync(orderId,
                    Request(new PurchaseFutureAllocationSeenRequest(allocationId, "1")), Metadata("revise-no-release")));
            Assert.Equal(403, denied.Status);
        }
        await AssertRevisionUntouchedAsync(run, orderId, version, allocationId);

        await GrantRevisionActorAsync(run, actor, release: true);

        // (b) A confirmed set that is not the active set (stale version, or a missing allocation): 409 set changed, nothing written.
        foreach (var (key, seen) in new[]
                 {
                     ("revise-stale", new[] { new PurchaseFutureAllocationSeenRequest(allocationId, "2") }),
                     ("revise-empty", Array.Empty<PurchaseFutureAllocationSeenRequest>()),
                 })
        {
            await using var scope = run.Factory.Services.CreateAsyncScope();
            var changed = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
                RevisionService(scope.ServiceProvider, actor).ReviseOrderSupplierAsync(orderId, Request(seen), Metadata(key)));
            Assert.Equal((409, PurchasingProblemCodesV1.FutureAllocationSetChanged), (changed.Status, changed.Code));
            await AssertRevisionUntouchedAsync(run, orderId, version, allocationId);
        }

        // Atomicity: the release is written, then the revision's own save fails; the one SQL transaction undoes both.
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        await using (var failing = run.Store(CompanyA, new FailingPurchasingSave()))
        {
            await Assert.ThrowsAnyAsync<Exception>(() =>
                RevisionService(scope.ServiceProvider, actor, failing.Store).ReviseOrderSupplierAsync(orderId,
                    Request(new PurchaseFutureAllocationSeenRequest(allocationId, "1")), Metadata("revise-save-fails")));
        }
        await AssertRevisionUntouchedAsync(run, orderId, version, allocationId);

        // (c) The confirmed active set: the revision is recorded and the allocation released through the Inventory owner
        // (allocation row, audit, outbox) in the same transaction; a replay releases nothing more.
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        {
            var service = RevisionService(scope.ServiceProvider, actor);
            var request = Request(new PurchaseFutureAllocationSeenRequest(allocationId, "1"));
            var revised = await service.ReviseOrderSupplierAsync(orderId, request, Metadata("revise-confirmed"));
            Assert.Equal(("Draft", supplier), (revised.Detail.Order.Status, revised.Detail.Order.SupplierPartyId));
            var replay = await service.ReviseOrderSupplierAsync(orderId, request, Metadata("revise-confirmed"));
            Assert.True(replay.IsReplay);
        }

        Assert.Equal((1, version + 1), (await run.ScalarAsync<int>("SELECT Status FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", orderId)),
            await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", orderId))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=N'purchasing.purchase-order.supplier-revised.v1'",
            ("@id", orderId.ToString("D"))));
        Assert.Equal((3, 0m, 5m, PurchaseFutureAllocationLinkReasonsV1.LinkDropped),
            (await run.ScalarAsync<int>("SELECT Status FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", allocationId)),
                await run.ScalarAsync<decimal>("SELECT RemainingQuantity FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", allocationId)),
                await run.ScalarAsync<decimal>("SELECT ReleasedQuantity FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", allocationId)),
                await run.ScalarAsync<string>("SELECT ReleaseReasonCode FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", allocationId))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=@event AND Reason=N'SENTETIK tedarikçi değişti'",
            ("@id", allocationId.ToString("D")), ("@event", InventoryFutureAllocationReleasedOutboxV1.EventType)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages WHERE EventType=@event",
            ("@event", InventoryFutureAllocationReleasedOutboxV1.EventType)));
    }

    private static async Task AssertRevisionUntouchedAsync(Run run, Guid orderId, long version, Guid allocationId)
    {
        Assert.Equal((2, version), (await run.ScalarAsync<int>("SELECT Status FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", orderId)),
            await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", orderId))));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseOrders WHERE Id=@id AND SupplierRevisionHistoryJson IS NOT NULL", ("@id", orderId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE OrderId=@id AND CommandName=@command",
            ("@id", orderId), ("@command", PurchasingCommands.OrderSupplierReviseV1)));
        Assert.Equal((1, 5m, 1L), (await run.ScalarAsync<int>("SELECT Status FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", allocationId)),
            await run.ScalarAsync<decimal>("SELECT RemainingQuantity FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", allocationId)),
            await run.ScalarAsync<long>("SELECT Version FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", allocationId))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages WHERE EventType=@event",
            ("@event", InventoryFutureAllocationReleasedOutboxV1.EventType)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=@event",
            ("@id", allocationId.ToString("D")), ("@event", InventoryFutureAllocationReleasedOutboxV1.EventType)));
    }

    /// <summary>The real Purchasing service on the real store and the host's real Inventory release bridge; the store may be swapped.</summary>
    private static PurchasingService RevisionService(IServiceProvider services, Guid actor, IPurchasingStore? store = null)
    {
        var company = services.GetRequiredService<ICompanyContext>();
        company.Set(CompanyA);
        var user = new ReturnDisclosureUser(actor);
        var authority = new ErpAuthorizationService(services.GetRequiredService<IdentityDbContext>(), user);
        var correlation = services.GetRequiredService<ICorrelationContext>();
        correlation.Set("SENTETIK-supplier-revision-allocations", null, SourceChannels.Web, "127.0.0.1", "SENTETIK-agent");
        return new PurchasingService(company, user, authority, correlation, new EligibleRevisionSupplier(),
            services.GetService<IProductPurchaseEligibilityPortV1>(), services.GetRequiredService<PurchaseUnitVerifier>(),
            store ?? services.GetRequiredService<IPurchasingStore>(), new PurchasingServiceBindings(), TimeProvider.System,
            allocationLinks: services.GetRequiredService<IPurchaseFutureAllocationLinkParticipantV1>());
    }

    private static async Task GrantRevisionActorAsync(Run run, Guid actor, bool release)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        if (!release) identity.Users.Add(new UserAccount(actor, "SENTETIK supplier revision actor", null));
        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"REVISION-ALLOCATIONS-{roleId:N}", "SENTETIK supplier revision grant", true);
        if (release)
        {
            role.AddPermission(PurchasingPermissionManifestV1.FutureAllocationRelease);
        }
        else
        {
            role.AddPermission(PurchasingPermissionManifestV1.OrdersRead);
            role.AddPermission(PurchasingPermissionManifestV1.OrdersRevise);
            role.AddPermission(PurchasingPermissionManifestV1.OrdersPriceRead);
        }

        identity.Roles.Add(role);
        identity.RoleAssignments.Add(DisclosureAssignment(actor, roleId));
        await identity.SaveChangesAsync();
    }

    /// <summary>A real Inventory future allocation (5 kg) on the real purchase line, written by the Inventory owner's writer.</summary>
    private static async Task<Guid> SeedRevisionAllocationAsync(Run run, Guid orderId, Guid lineId, long orderVersion)
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        var audit = new AuditEventCollector();
        await using var inventory = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(audit, company)).Options, company);
        await inventory.Database.MigrateAsync();
        var salesOrder = Guid.NewGuid(); var salesLine = Guid.NewGuid(); var allocationId = Guid.NewGuid();
        var writer = new InventoryFutureAllocationWriter(inventory, company, audit, new SalesOrderLineSerializationLockHostBinder(company),
            new PurchaseLineSerializationLockHostBinder(company), new RevisionSupply(orderId, lineId, orderVersion),
            new RevisionDemand(salesOrder, salesLine), new RevisionKilograms(), TimeProvider.System);
        var created = await writer.CreateAsync(new(allocationId, salesOrder, salesLine, orderId, lineId, "5", AcceptanceUnit,
                "SENTETIK revizyon öncesi ayırma"),
            new InventoryCommandMetadata(run.Actor, ErpPermissions.InventoryFutureAllocationWrite, ["SENTETIK-INVENTORY"], run.Correlation,
                null, $"revision-allocation-{allocationId:N}", "Web"),
            new RevisionFence());
        Assert.Equal("5", created.Allocation.RemainingQuantity);
        return allocationId;
    }

    private sealed class EligibleRevisionSupplier : IPurchasingPartyAccountPortV1
    {
        public Task<PurchasingPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PurchasingPartyAccountAnswerV1(PurchasingPartyAccountOutcomeV1.Found,
                new PurchasingPartyAccountV1(partyId, companyAccountId, CompanyA, true, true, true, "1", "1")));
    }

    /// <summary>Fails the revision's own save after the participant (the release) ran inside the same transaction.</summary>
    private sealed class FailingPurchasingSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("SENTETIK revision save failure");
    }

    private sealed class RevisionSupply(Guid orderId, Guid lineId, long orderVersion) : IInventoryFutureSupplySourceV1
    {
        public Task<InventoryFutureSupplySourceAnswerV1> GetAllocatableLineAsync(Guid order, Guid line, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryFutureSupplySourceAnswerV1(InventoryFutureSupplySourceOutcomeV1.Allocatable,
                new(orderId, lineId, AcceptanceProduct, "product-v1", AcceptanceUnit, "10", orderVersion)));
    }

    private sealed class RevisionDemand(Guid salesOrder, Guid salesLine) : IInventoryFutureDemandSourceV1
    {
        public Task<InventoryFutureDemandSourceAnswerV1> GetDemandLineAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryFutureDemandSourceAnswerV1(InventoryFutureDemandSourceOutcomeV1.Open,
                new(salesOrder, salesLine, AcceptanceProduct, "product-v1", AcceptanceUnit, "5")));
    }

    private sealed class RevisionKilograms : IInventoryFutureUnitConverterV1
    {
        public Task<InventoryFutureUnitConversionV1> ConvertAsync(Guid productId, string productVersion, decimal quantity,
            Guid fromUnitId, Guid toUnitId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryFutureUnitConversionV1(InventoryFutureUnitConversionOutcomeV1.Converted, quantity));

        public Task<InventoryFutureUnitConversionV1> ConvertToKilogramsAsync(Guid productId, string productVersion, decimal quantity,
            Guid fromUnitId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryFutureUnitConversionV1(InventoryFutureUnitConversionOutcomeV1.Converted, quantity));
    }

    private sealed class RevisionFence : IInventoryFutureAllocationAuthorityFenceV1
    {
        public Task RequireStillAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
