using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SettingsCollateralTypeSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Collateral_catalog_refuses_a_late_company_actor_or_membership_change_after_reading_real_versions()
    {
        await using var run = await HostRun.StartAsync();
        using var editor = run.Client(await run.GrantAsync(TestUsers.CompanyA, CompanyA, Manage), CompanyA);
        await run.CommandAsync(editor, Root, "read-scope", new CreateSettingsCollateralTypeRequestV1(
            "READ", "Synthetic scope source", SettingsCollateralKindV1.Dbs, "Synthetic source"));

        foreach (var change in new[] { "company", "actor", "membership", "authentication" })
        {
            var company = new ReboundCatalogCompany(CompanyA);
            var actor = new ReboundCatalogActor();
            var observer = new CatalogReadBoundary("[CollateralTypeVersions]", () =>
            {
                switch (change)
                {
                    case "company": company.CompanyId = CompanyB; break;
                    case "actor": actor.UserId = Guid.NewGuid().ToString("D"); break;
                    case "membership": actor.HasAccess = false; break;
                    case "authentication": actor.IsAuthenticated = false; break;
                    default: throw new InvalidOperationException("Synthetic change is unknown.");
                }
            });
            await using var database = new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
                .UseSqlServer(run.Connection).AddInterceptors(observer).Options, company);
            var catalog = new SettingsCollateralTypeCatalogV1(database, company, actor);

            var refused = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => catalog.ListAsync(CompanyA));

            Assert.Equal(SettingsCollateralTypeProblemCodesV1.AccessDenied, refused.Code);
            Assert.Equal(1, observer.Calls);
            Assert.False(database.ChangeTracker.HasChanges());
        }

        // The read fence never alters or conceals the stored source for the original authorized owner.
        var saved = Assert.Single((await run.ReadAsync(editor, Root)).Entries);
        Assert.Equal(("100", "1"), (saved.RatePercent, saved.Version));
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[CollateralTypeVersions]"));
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Settings.CollateralType'"));
    }

    [SqlServerFact]
    public async Task An_empty_collateral_catalog_still_refuses_lost_membership_instead_of_returning_an_available_empty_answer()
    {
        await using var run = await HostRun.StartAsync();
        var company = new ReboundCatalogCompany(CompanyA);
        var actor = new ReboundCatalogActor();
        var observer = new CatalogReadBoundary("[CollateralTypes]", () => actor.HasAccess = false);
        await using var database = new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer(run.Connection).AddInterceptors(observer).Options, company);
        var catalog = new SettingsCollateralTypeCatalogV1(database, company, actor);

        var refused = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => catalog.ListAsync(CompanyA));

        Assert.Equal(SettingsCollateralTypeProblemCodesV1.AccessDenied, refused.Code);
        Assert.Equal(1, observer.Calls);
        Assert.False(database.ChangeTracker.HasChanges());
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [settings].[CollateralTypes]"));
        actor.HasAccess = true;
        Assert.Empty((await catalog.ListAsync(CompanyA)).Entries);
    }

    private sealed class ReboundCatalogCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; set; } = companyId;
    }

    private sealed class ReboundCatalogActor : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId { get; set; } = Guid.NewGuid().ToString("D");
        public bool HasAccess { get; set; } = true;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA, CompanyB };
        public bool CanAccessCompany(Guid companyId) => HasAccess && AllowedCompanyIds.Contains(companyId);
    }

    private sealed class CatalogReadBoundary(string table, Action change) : DbCommandInterceptor
    {
        public int Calls { get; private set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Calls == 0 && command.CommandText.Contains(table, StringComparison.Ordinal))
            {
                Calls++;
                change();
            }

            return ValueTask.FromResult(result);
        }
    }
}
