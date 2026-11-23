using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Supplier_return_sql_disclosure_uses_fresh_price_permission_for_reads_decision_and_both_replay_shapes()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedReturnInvoiceCostReceiptAsync(run, 5m);
        var maker = Guid.Parse(run.Actor);
        var checker = Guid.NewGuid();
        var priceAssignment = await GrantReturnDisclosureActorAsync(run, maker, price: true);
        await GrantReturnDisclosureActorAsync(run, checker, price: false);
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var service = ReturnDisclosureService(scope.ServiceProvider, maker);
        var request = DisclosureRequest(receipt.Match.AcceptanceId);
        var metadata = new PurchasingCommandMetadata("SENTETIK-disclosure-create", run.Correlation, null, AcceptanceTime);
        var created = await service.CreateSupplierReturnAsync(receipt.Match.OrderId, request, metadata);
        AssertReturnSqlPrice(created.Return, visible: true);
        AssertReturnSqlPrice((await service.GetSupplierReturnAsync(receipt.Match.OrderId, request.ReturnId)).Return, visible: true);
        AssertReturnSqlPrice(Assert.Single((await service.GetSupplierReturnsAsync(receipt.Match.OrderId)).Items), visible: true);

        await using (var revoke = run.Factory.Services.CreateAsyncScope())
        {
            var identity = revoke.ServiceProvider.GetRequiredService<IdentityDbContext>();
            identity.RoleAssignments.Remove(await identity.RoleAssignments.Include(row => row.Scopes)
                .SingleAsync(row => row.Id == priceAssignment));
            await identity.SaveChangesAsync();
        }

        // Reuse the same service scope that already observed a grant: a cached price answer must not survive revocation.
        AssertReturnSqlPrice((await service.GetSupplierReturnAsync(receipt.Match.OrderId, request.ReturnId)).Return, visible: false);
        AssertReturnSqlPrice(Assert.Single((await service.GetSupplierReturnsAsync(receipt.Match.OrderId)).Items), visible: false);
        var replay = await service.CreateSupplierReturnAsync(receipt.Match.OrderId, request, metadata);
        Assert.True(replay.IsReplay);
        AssertReturnSqlPrice(replay.Return, visible: false);

        await using (var decisionScope = run.Factory.Services.CreateAsyncScope())
        {
            var decisionService = ReturnDisclosureService(decisionScope.ServiceProvider, checker);
            var decisionMetadata = new PurchasingCommandMetadata("SENTETIK-disclosure-reject", run.Correlation, null, AcceptanceTime);
            var decision = await decisionService.RejectSupplierReturnCostExceptionAsync(receipt.Match.OrderId, request.ReturnId,
                new("1", "SENTETIK rejected cost"), decisionMetadata);
            AssertReturnSqlPrice(decision.Return, visible: false);
            var decisionReplay = await decisionService.RejectSupplierReturnCostExceptionAsync(receipt.Match.OrderId, request.ReturnId,
                new("1", "SENTETIK rejected cost"), decisionMetadata);
            Assert.True(decisionReplay.IsReplay);
            AssertReturnSqlPrice(decisionReplay.Return, visible: false);
        }
        replay = await service.CreateSupplierReturnAsync(receipt.Match.OrderId, request, metadata);
        Assert.Equal("PendingCostException", replay.Return.Status);
        AssertReturnSqlPrice(replay.Return, visible: false);

        // Simulate a genuine older receipt without a stored response snapshot; the legacy read is fenced as well.
        await run.ExecuteAsync("UPDATE purchasing.PurchaseSupplierReturnAttempts SET ResultJson=NULL WHERE ReturnId=@id AND CommandName=@command",
            ("@id", request.ReturnId), ("@command", PurchaseSupplierReturnCommandsV1.CreateV1));
        var legacy = await service.CreateSupplierReturnAsync(receipt.Match.OrderId, request, metadata);
        Assert.True(legacy.IsReplay);
        Assert.Equal("Rejected", legacy.Return.Status);
        AssertReturnSqlPrice(legacy.Return, visible: false);
        Assert.Equal(12.3456m, await run.ScalarAsync<decimal>("SELECT UnitCostTryPerKg FROM purchasing.PurchaseSupplierReturns WHERE ReturnId=@id",
            ("@id", request.ReturnId)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseSupplierReturnAttempts WHERE ReturnId=@id",
            ("@id", request.ReturnId)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType='Purchasing.SupplierReturn' AND EntityId=@id",
            ("@id", request.ReturnId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Supplier_return_sql_create_without_price_permission_withholds_cost_without_changing_the_stored_amount()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedReturnInvoiceCostReceiptAsync(run, 5m);
        var actor = Guid.Parse(run.Actor);
        await GrantReturnDisclosureActorAsync(run, actor, price: false);
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var service = ReturnDisclosureService(scope.ServiceProvider, actor);
        var request = DisclosureRequest(receipt.Match.AcceptanceId);
        var metadata = new PurchasingCommandMetadata("SENTETIK-hidden-create", run.Correlation, null, AcceptanceTime);
        var response = await service.CreateSupplierReturnAsync(receipt.Match.OrderId, request, metadata);
        Assert.False(response.IsReplay);
        AssertReturnSqlPrice(response.Return, visible: false);
        Assert.Equal(12.3456m, await run.ScalarAsync<decimal>("SELECT UnitCostTryPerKg FROM purchasing.PurchaseSupplierReturns WHERE ReturnId=@id",
            ("@id", request.ReturnId)));
        AssertReturnSqlPrice((await service.CreateSupplierReturnAsync(receipt.Match.OrderId, request, metadata)).Return, visible: false);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseSupplierReturnAttempts WHERE ReturnId=@id",
            ("@id", request.ReturnId)));
    }

    private static CreatePurchaseSupplierReturnV1 DisclosureRequest(Guid acceptance) => new(Guid.NewGuid(), Guid.NewGuid(), acceptance,
        Guid.NewGuid(), "Free", "1", "12.3456", false, "SENTETIK return disclosure", AcceptanceTime);

    private static void AssertReturnSqlPrice(PurchaseSupplierReturnViewV1 view, bool visible)
    {
        Assert.Equal(visible ? PurchasingPriceDisclosureV1.Visible : PurchasingPriceDisclosureV1.Withheld, view.PriceDisclosure);
        Assert.Equal(visible ? "12.3456" : null, view.UnitCostTryPerKg);
    }

    private static PurchasingService ReturnDisclosureService(IServiceProvider services, Guid actor,
        IPurchaseSupplierReturnCostSourceReadPortV1? sources = null)
    {
        var company = services.GetRequiredService<ICompanyContext>();
        company.Set(CompanyA);
        var user = new ReturnDisclosureUser(actor);
        var authority = new ErpAuthorizationService(services.GetRequiredService<IdentityDbContext>(), user);
        var correlation = services.GetRequiredService<ICorrelationContext>();
        correlation.Set("SENTETIK-return-disclosure", null, SourceChannels.Web, "127.0.0.1", "SENTETIK-agent");
        // Genuine module stores, audit interceptor and Identity SQL. No invoice exists in this fixture, so the existing
        // manual-cost pending path is used; no stock exit host or guessed invoice cost is supplied by this test.
        return new PurchasingService(company, user, authority, correlation, services.GetRequiredService<IPurchasingPartyAccountPortV1>(),
            services.GetService<IProductPurchaseEligibilityPortV1>(), services.GetRequiredService<PurchaseUnitVerifier>(),
            services.GetRequiredService<IPurchasingStore>(), new PurchasingServiceBindings(), TimeProvider.System,
            returnStore: services.GetRequiredService<IPurchaseSupplierReturnStore>(), returnCostSources: sources);
    }

    private static async Task<Guid?> GrantReturnDisclosureActorAsync(Run run, Guid actor, bool price)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        identity.Users.Add(new UserAccount(actor, "SENTETIK return disclosure actor", null));
        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"RETURN-DISCLOSURE-{roleId:N}", "SENTETIK return disclosure commands", true);
        role.AddPermission(PurchasingPermissionManifestV1.OrdersRead);
        role.AddPermission(PurchasingPermissionManifestV1.SupplierReturnsRecord);
        role.AddPermission(PurchasingPermissionManifestV1.OrdersApprove);
        identity.Roles.Add(role);
        identity.RoleAssignments.Add(DisclosureAssignment(actor, roleId));
        Guid? priceAssignmentId = null;
        if (price)
        {
            var priceRoleId = Guid.NewGuid();
            var priceRole = new RoleDefinition(priceRoleId, $"RETURN-PRICE-{priceRoleId:N}", "SENTETIK separate return price grant", true);
            priceRole.AddPermission(PurchasingPermissionManifestV1.OrdersPriceRead);
            var priceAssignment = DisclosureAssignment(actor, priceRoleId);
            priceAssignmentId = priceAssignment.Id;
            identity.Roles.Add(priceRole);
            identity.RoleAssignments.Add(priceAssignment);
        }
        await identity.SaveChangesAsync();
        return priceAssignmentId;
    }

    private static RoleAssignment DisclosureAssignment(Guid actor, Guid roleId)
    {
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, CompanyA, DateTimeOffset.UtcNow.AddMinutes(-1), null, null);
        assignment.AddScope(AccessScopeType.Company, CompanyA);
        return assignment;
    }

    private sealed class ReturnDisclosureUser(Guid actor) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([CompanyA]);
        public bool CanAccessCompany(Guid company) => AllowedCompanyIds.Contains(company);
    }
}
