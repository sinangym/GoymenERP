using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryLabelDetailTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Detail_returns_one_versioned_identity_and_its_current_positions()
    {
        var world = new World();
        var result = await world.Service().GetAsync(world.Reader.Record!.Label.Id);

        Assert.Equal("3", result.LabelVersion);
        Assert.Equal(world.Reader.Record.Label.Id, result.Identity.LabelId);
        Assert.Equal(world.Reader.Record.Label.ProductId, result.Identity.ProductId);
        Assert.Equal("10.125", result.Balances.PhysicalKg);
        Assert.Single(result.Balances.Positions);
        Assert.Equal(world.Reader.Record.Label.Id, result.Genealogy.LabelId);
        Assert.Empty(result.Genealogy.Edges);
        Assert.Equal([
            ErpPermissions.InventoryLabelsRead,
            ErpPermissions.InventoryBalancesRead,
            ErpPermissions.InventoryLabelsRead,
            ErpPermissions.InventoryBalancesRead,
            ErpPermissions.InventoryLabelsRead,
            ErpPermissions.InventoryBalancesRead,
            ErpPermissions.InventoryLabelsRead,
            ErpPermissions.InventoryBalancesRead,
        ], world.Authority.Calls);
        Assert.Equal(2, world.Reader.Reads);
        Assert.Equal(1, world.Genealogy.Reads);
    }

    [Fact]
    public async Task Missing_or_foreign_label_has_one_non_disclosing_not_found_result()
    {
        var world = new World();
        world.Reader.Record = null;
        await Refused(world.Service().GetAsync(Guid.NewGuid()), 404, InventoryLabelDetailCodesV1.NotFound);

        world = new World { Reader = { Record = Record(company: Guid.NewGuid()) } };
        await Refused(world.Service().GetAsync(world.Reader.Record!.Label.Id), 503,
            InventoryLabelDetailCodesV1.Unavailable);
    }

    [Fact]
    public async Task Both_existing_read_permissions_are_company_wide_and_fresh_before_and_after_read()
    {
        var denied = new World();
        denied.Authority.Denied = ErpPermissions.InventoryBalancesRead;
        await Refused(denied.Service().GetAsync(denied.Reader.Record!.Label.Id), 403,
            InventoryLabelDetailCodesV1.Forbidden);
        Assert.Equal(0, denied.Reader.Reads);

        var revoked = new World();
        revoked.Reader.During = () => revoked.Authority.Denied = ErpPermissions.InventoryLabelsRead;
        await Refused(revoked.Service().GetAsync(revoked.Reader.Record!.Label.Id), 403,
            InventoryLabelDetailCodesV1.Forbidden);
        Assert.Equal(1, revoked.Reader.Reads);

        var genealogyRevoked = new World();
        genealogyRevoked.Genealogy.During = () =>
            genealogyRevoked.Authority.Denied = ErpPermissions.InventoryBalancesRead;
        await Refused(genealogyRevoked.Service().GetAsync(genealogyRevoked.Reader.Record!.Label.Id), 403,
            InventoryLabelDetailCodesV1.Forbidden);
        Assert.Equal(1, genealogyRevoked.Reader.Reads);
        Assert.Equal(1, genealogyRevoked.Genealogy.Reads);

        var scoped = new World();
        scoped.Authority.CompanyWide = false;
        await Refused(scoped.Service().GetAsync(scoped.Reader.Record!.Label.Id), 403,
            InventoryLabelDetailCodesV1.Forbidden);
    }

    [Fact]
    public async Task Actor_or_company_scope_change_during_read_is_refused()
    {
        var actor = new World();
        actor.Reader.During = () => actor.User.UserId = Guid.NewGuid().ToString();
        await Refused(actor.Service().GetAsync(actor.Reader.Record!.Label.Id), 403,
            InventoryLabelDetailCodesV1.Forbidden);

        var company = new World();
        company.Reader.During = () => company.Context.Set(Guid.NewGuid());
        await Refused(company.Service().GetAsync(company.Reader.Record!.Label.Id), 403,
            InventoryLabelDetailCodesV1.Forbidden);

        var genealogyCompany = new World();
        genealogyCompany.Genealogy.During = () => genealogyCompany.Context.Set(Guid.NewGuid());
        await Refused(genealogyCompany.Service().GetAsync(genealogyCompany.Reader.Record!.Label.Id), 403,
            InventoryLabelDetailCodesV1.Forbidden);
    }

    [Fact]
    public async Task Missing_or_failing_provider_has_no_positive_fallback()
    {
        var world = new World();
        await Refused(new InventoryLabelDetailService(world.Context, world.User, world.Authority, null, world.Genealogy)
            .GetAsync(Guid.NewGuid()), 503, InventoryLabelDetailCodesV1.Unavailable);

        await Refused(new InventoryLabelDetailService(world.Context, world.User, world.Authority, world.Reader, null)
            .GetAsync(Guid.NewGuid()), 503, InventoryLabelDetailCodesV1.Unavailable);

        world.Reader.Fail = true;
        await Refused(world.Service().GetAsync(world.Reader.Record!.Label.Id), 503,
            InventoryLabelDetailCodesV1.Unavailable);

        world = new World();
        world.Genealogy.Fail = true;
        await Refused(world.Service().GetAsync(world.Reader.Record!.Label.Id), 503,
            InventoryLabelDetailCodesV1.Unavailable);
    }

    [Fact]
    public async Task Torn_or_internally_inconsistent_capture_is_fail_closed()
    {
        var conflict = new World();
        conflict.Reader.Error = new InventoryLabelDetailException(409, InventoryLabelDetailCodesV1.ReadConflict);
        await Refused(conflict.Service().GetAsync(conflict.Reader.Record!.Label.Id), 409,
            InventoryLabelDetailCodesV1.ReadConflict);

        var invalid = new World();
        invalid.Reader.Record = invalid.Reader.Record! with { Version = 0 };
        await Refused(invalid.Service().GetAsync(invalid.Reader.Record.Label.Id), 503,
            InventoryLabelDetailCodesV1.Unavailable);

        invalid = new World();
        invalid.Genealogy.Result = invalid.Genealogy.Result! with { LabelId = Guid.NewGuid() };
        await Refused(invalid.Service().GetAsync(invalid.Reader.Record!.Label.Id), 503,
            InventoryLabelDetailCodesV1.Unavailable);

        invalid = new World();
        invalid.Genealogy.Result = invalid.Genealogy.Result! with
        {
            Edges = [
                Edge(invalid.Reader.Record!.Label.Id, Guid.NewGuid()),
                Edge(Guid.NewGuid(), Guid.NewGuid()),
            ],
        };
        await Refused(invalid.Service().GetAsync(invalid.Reader.Record.Label.Id), 503,
            InventoryLabelDetailCodesV1.Unavailable);

        var changed = new World();
        changed.Genealogy.During = () => changed.Reader.Record = changed.Reader.Record! with { Version = 4 };
        await Refused(changed.Service().GetAsync(changed.Reader.Record!.Label.Id), 409,
            InventoryLabelDetailCodesV1.ReadConflict);
    }

    private static async Task Refused(Task task, int status, string code)
    {
        var error = await Assert.ThrowsAsync<InventoryLabelDetailException>(() => task);
        Assert.Equal((status, code), (error.StatusCode, error.Code));
    }

    private static InventoryLabelDetailRecord Record(Guid? company = null)
    {
        var companyId = company ?? Company;
        var lot = new MaterialLot(Guid.NewGuid(), companyId, Guid.NewGuid(), heatNumber: "HEAT-1");
        var label = new MaterialLabel(Guid.NewGuid(), companyId, "LBL-DETAIL-1", lot.ProductId, lot.Id);
        var position = new StockPosition(
            companyId,
            new StockOwner(StockPartyKind.Company, companyId),
            new StockCustodian(StockPartyKind.Company, companyId),
            Guid.NewGuid(),
            label,
            BlockState.Free);
        return new(3, label, lot, [new StockBalance(position, Kilograms.Positive(10.125m))]);
    }

    private static InventoryLabelGenealogyEdgeV1 Edge(Guid source, Guid output) => new(
        Guid.NewGuid(),
        nameof(InventoryLabelTransformationKind.Split),
        DateTimeOffset.UtcNow,
        source,
        $"LBL-{source:N}",
        "1",
        "1",
        output,
        $"LBL-{output:N}",
        "1",
        "1");

    private sealed class World
    {
        public Context Context { get; } = new();
        public User User { get; } = new();
        public Authority Authority { get; } = new();
        public Reader Reader { get; } = new();
        public GenealogyReader Genealogy { get; } = new();
        public World()
        {
            Genealogy.Result = new InventoryLabelGenealogyV1(
                Reader.Record!.Label.Id,
                Reader.Record.Label.Code,
                [],
                false);
        }

        public InventoryLabelDetailService Service() => new(Context, User, Authority, Reader, Genealogy);
    }

    private sealed class Context : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = Company;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId { get; set; } = Guid.NewGuid().ToString();
        public bool Access { get; set; } = true;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => Access && AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public string? Denied { get; set; }
        public bool CompanyWide { get; set; } = true;
        public List<string> Calls { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(permission);
            return Task.FromResult(new PermissionScope(
                permission != Denied,
                CompanyWide,
                new HashSet<string>(),
                new HashSet<Guid>(),
                new HashSet<Guid>(),
                new HashSet<Guid>()));
        }
    }

    private sealed class Reader : IInventoryLabelDetailReader
    {
        public InventoryLabelDetailRecord? Record { get; set; } = InventoryLabelDetailTests.Record();
        public Action? During { get; set; }
        public Exception? Error { get; set; }
        public bool Fail { get; set; }
        public int Reads { get; private set; }

        public async Task<InventoryLabelDetailRecord?> ReadAsync(
            Guid companyId,
            Guid labelId,
            CancellationToken cancellationToken)
        {
            Reads++;
            await Task.Yield();
            During?.Invoke();
            if (Error is not null) throw Error;
            if (Fail) throw new InvalidOperationException("Provider failure");
            return Record;
        }
    }

    private sealed class GenealogyReader : IInventoryLabelGenealogyReaderV1
    {
        public InventoryLabelGenealogyV1? Result { get; set; }
        public bool Fail { get; set; }
        public Action? During { get; set; }
        public int Reads { get; private set; }

        public async Task<InventoryLabelGenealogyV1?> ReadAsync(
            Guid labelId,
            int limit = 100,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            await Task.Yield();
            During?.Invoke();
            if (Fail) throw new InvalidOperationException("Provider failure");
            return Result;
        }
    }
}
