using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Label_detail_reads_one_versioned_identity_and_its_positions_without_an_effect()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ST02-DETAIL", 125.5m);
        var before = await run.ScalarAsync<string>(
            "SELECT CONCAT(COUNT(*), ':', SUM(Version)) FROM inventory.Labels");

        await using var context = run.Context(CompanyA);
        var detail = await LabelDetails(context, CompanyA).GetAsync(source.LabelId);

        Assert.Equal((source.LabelId, CompanyA, source.ProductId, "125.5", "125.5", "0"),
            (detail.Identity.LabelId, detail.Identity.CompanyId, detail.Identity.ProductId,
                detail.Balances.PhysicalKg, detail.Balances.FreeKg, detail.Balances.BlockedKg));
        Assert.Equal(source.LabelVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), detail.LabelVersion);
        Assert.Single(detail.Balances.Positions);
        Assert.Equal(before, await run.ScalarAsync<string>(
            "SELECT CONCAT(COUNT(*), ':', SUM(Version)) FROM inventory.Labels"));
    }

    [SqlServerFact]
    public async Task Label_detail_does_not_disclose_another_companys_real_label()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ST02-FOREIGN", 10m);
        await using var companyB = run.Context(CompanyB);

        var exception = await Assert.ThrowsAsync<InventoryLabelDetailException>(() =>
            LabelDetails(companyB, CompanyB).GetAsync(source.LabelId));

        Assert.Equal((404, InventoryLabelDetailCodesV1.NotFound),
            (exception.StatusCode, exception.Code));
    }

    [SqlServerFact]
    public async Task Label_detail_rejects_a_real_version_change_between_related_reads()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ST02-TORN", 10m);
        var interceptor = new LabelDetailAfterBalanceRead(() => TouchLabelAsync(run, source.LabelId));
        await using var context = run.Context(CompanyA, interceptor);

        var exception = await Assert.ThrowsAsync<InventoryLabelDetailException>(() =>
            LabelDetails(context, CompanyA).GetAsync(source.LabelId));

        Assert.True(interceptor.Triggered);
        Assert.Equal((409, InventoryLabelDetailCodesV1.ReadConflict),
            (exception.StatusCode, exception.Code));
    }

    private static InventoryLabelDetailService LabelDetails(
        InventoryDbContext context,
        Guid companyId,
        IInventoryLabelGenealogyReaderV1? genealogy = null)
    {
        var company = new CompanyContext();
        company.Set(companyId);
        return new InventoryLabelDetailService(
            company,
            new LabelDetailUser(companyId),
            new LabelDetailAuthority(),
            new SqlInventoryLabelDetailReader(context),
            genealogy ?? new LabelDetailGenealogyReader(context));
    }

    private static async Task TouchLabelAsync(Run run, Guid labelId)
    {
        await using var scope = run.Store(CompanyA);
        var label = (await scope.Labels.FindLabelAsync(labelId))!;
        label.Print(DateTimeOffset.UtcNow, run.Actor.ToString("D"));
        await scope.Labels.SaveLabelAsync(label, Metadata(run));
    }

    private sealed class LabelDetailAfterBalanceRead(Func<Task> action) : DbCommandInterceptor
    {
        public bool Triggered { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!Triggered && command.CommandText.Contains("[inventory].[Balances]", StringComparison.Ordinal))
            {
                Triggered = true;
                await action();
            }

            return result;
        }
    }

    private sealed class LabelDetailUser(Guid companyId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "label-detail-reader";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid requestedCompanyId) => requestedCompanyId == companyId;
    }

    private sealed class LabelDetailAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(
                permission is ErpPermissions.InventoryLabelsRead or ErpPermissions.InventoryBalancesRead,
                true,
                new HashSet<string>(),
                new HashSet<Guid>(),
                new HashSet<Guid>(),
                new HashSet<Guid>()));
    }

    private sealed class LabelDetailGenealogyReader(InventoryDbContext context) : IInventoryLabelGenealogyReaderV1
    {
        public async Task<InventoryLabelGenealogyV1?> ReadAsync(
            Guid labelId,
            int limit = 100,
            CancellationToken cancellationToken = default)
        {
            var label = await context.Labels.AsNoTracking()
                .Where(row => row.Id == labelId)
                .Select(row => new { row.Id, row.Code })
                .SingleOrDefaultAsync(cancellationToken);
            return label is null ? null : new(label.Id, label.Code, [], false);
        }
    }
}
