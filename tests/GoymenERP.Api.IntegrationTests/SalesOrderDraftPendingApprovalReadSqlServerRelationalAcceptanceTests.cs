using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// D-05's Sales owner adapter against REAL SQL Server. The setup uses isolated synthetic owner
/// snapshots and in-process authorities; it creates no Identity grant and proves no sale, price,
/// stock, shipment, or financial effect.
/// </summary>
public sealed class SalesOrderDraftPendingApprovalReadSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 10, 9);
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Currency = Guid.Parse("6a1d2c3b-4a59-4687-9a0b-1c2d3e4f5a91");
    private static readonly Guid MoneyProfile = Guid.Parse("6a1d2c3b-4a59-4687-9a0b-1c2d3e4f5a92");
    private static readonly Guid KgUnit = Guid.Parse("6a1d2c3b-4a59-4687-9a0b-1c2d3e4f5a93");
    private static readonly Guid KgProfile = Guid.Parse("6a1d2c3b-4a59-4687-9a0b-1c2d3e4f5a94");
    private static readonly Guid Product = Guid.Parse("6a1d2c3b-4a59-4687-9a0b-1c2d3e4f5a95");
    private static readonly SalesDraftStateAudit Submitted = new(
        "sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gonderildi");
    private static readonly SalesDraftStateAudit Withdrawn = new(
        "sales.order-draft.withdrawn.v1", "Withdraw", "SENTETIK geri cekildi");
    private static readonly SalesDraftStateAudit Approved = new(
        "sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandi");
    private static readonly SalesDraftStateAudit Rejected = new(
        "sales.order-draft.rejected.v1", "Reject", "SENTETIK reddedildi");

    [SqlServerFact]
    public async Task True_pending_approvals_are_company_scoped_authority_fenced_and_persona_exact_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var preparer = "SENTETIK-other-preparer";
        var supportCreator = "SENTETIK-sales-support";
        var regularCreator = "SENTETIK-regular-creator";
        var approver = "SENTETIK-approver";

        SalesOrderDraft preparing;
        SalesOrderDraft pendingOther;
        SalesOrderDraft approved;
        SalesOrderDraft rejected;
        SalesOrderDraft withdrawn;
        SalesOrderDraft supportOwn;
        SalesOrderDraft regularOwn;
        SalesOrderDraft refreshWithdrawn;
        await using (var companyA = run.Store(CompanyA))
        {
            preparing = await CreateAsync(companyA.Store, run, CompanyA, "preparing", preparer);
            pendingOther = await CreateAndSubmitAsync(companyA.Store, run, CompanyA, "pending-other", preparer);
            approved = await CreateAndSubmitAsync(companyA.Store, run, CompanyA, "approved", preparer);
            await companyA.Store.UpdateDraftStateAsync(
                run.Envelope("approve", approver), Hash("approve"), SalesDraftCommands.ApproveV1, approved.Id,
                (draft, now) => draft.Approve(3, approver, now), Approved);
            rejected = await CreateAndSubmitAsync(companyA.Store, run, CompanyA, "rejected", preparer);
            await companyA.Store.UpdateDraftStateAsync(
                run.Envelope("reject", approver), Hash("reject"), SalesDraftCommands.RejectV1, rejected.Id,
                (draft, now) => draft.Reject(3, approver, "SENTETIK reason", now), Rejected);
            withdrawn = await CreateAsync(companyA.Store, run, CompanyA, "withdrawn", preparer);
            await companyA.Store.UpdateDraftStateAsync(
                run.Envelope("withdraw-preparing", preparer), Hash("withdraw-preparing"), SalesDraftCommands.WithdrawV1, withdrawn.Id,
                (draft, now) => draft.Withdraw(2, preparer, null, now), Withdrawn);
            supportOwn = await CreateAndSubmitAsync(companyA.Store, run, CompanyA, "support-own", supportCreator);
            regularOwn = await CreateAndSubmitAsync(companyA.Store, run, CompanyA, "regular-own", regularCreator);
            refreshWithdrawn = await CreateAndSubmitAsync(companyA.Store, run, CompanyA, "refresh-withdrawn", preparer);
        }

        SalesOrderDraft foreignPending;
        await using (var companyB = run.Store(CompanyB))
        {
            foreignPending = await CreateAndSubmitAsync(companyB.Store, run, CompanyB, "foreign", preparer);
        }

        await using (var readScope = run.Store(CompanyA))
        {
            var reader = run.Reader(readScope, approver);
            var result = await reader.Owner.ListAsync(new(CompanyA, approver), null, default);

            Assert.Equal(SalesOrderDraftPendingApprovalReadOutcomeV1.Found, result.Outcome);
            Assert.Equal(CompanyA, result.Resource!.CompanyId);
            Assert.Null(result.Resource.NextCursor);
            Assert.Equal(
                new[] { pendingOther.Id, supportOwn.Id, regularOwn.Id, refreshWithdrawn.Id }.Order(),
                result.Resource.Items.Select(item => item.DraftId).Order());
            Assert.DoesNotContain(result.Resource.Items, item => item.DraftId == preparing.Id);
            Assert.DoesNotContain(result.Resource.Items, item => item.DraftId == approved.Id);
            Assert.DoesNotContain(result.Resource.Items, item => item.DraftId == rejected.Id);
            Assert.DoesNotContain(result.Resource.Items, item => item.DraftId == withdrawn.Id);
            Assert.DoesNotContain(result.Resource.Items, item => item.DraftId == foreignPending.Id);

            var exact = Assert.Single(result.Resource.Items, item => item.DraftId == pendingOther.Id);
            Assert.Equal((CompanyA, "3", "Rev.0", preparer, T0),
                (exact.CompanyId, exact.Version, exact.Revision, exact.SubmittedBy, exact.SubmittedAtUtc));
            Assert.False(exact.IsOwnDraft);
        }

        await using (var supportScope = run.Store(CompanyA))
        {
            var support = run.Reader(supportScope, supportCreator, ErpPersonaRoleCodes.SalesSupport);
            var hidden = await support.Owner.GetAsync(new(CompanyA, supportCreator), supportOwn.Id, default);
            Assert.Equal(SalesOrderDraftPendingApprovalReadOutcomeV1.NotFound, hidden.Outcome);
            Assert.Null(hidden.Resource);
        }

        await using (var regularScope = run.Store(CompanyA))
        {
            var regular = run.Reader(regularScope, regularCreator);
            var admitted = await regular.Owner.GetAsync(new(CompanyA, regularCreator), regularOwn.Id, default);
            Assert.Equal(SalesOrderDraftPendingApprovalReadOutcomeV1.Found, admitted.Outcome);
            Assert.True(admitted.Resource!.Item.IsOwnDraft);
            Assert.Equal((regularCreator, "3", T0),
                (admitted.Resource.Item.SubmittedBy, admitted.Resource.Item.Version, admitted.Resource.Item.SubmittedAtUtc));
        }

        await using (var transitionScope = run.Store(CompanyA))
        {
            await transitionScope.Store.UpdateDraftStateAsync(
                run.Envelope("refresh-approve", approver), Hash("refresh-approve"), SalesDraftCommands.ApproveV1, pendingOther.Id,
                (draft, now) => draft.Approve(3, approver, now), Approved);
            await transitionScope.Store.UpdateDraftStateAsync(
                run.Envelope("refresh-withdraw", preparer), Hash("refresh-withdraw"), SalesDraftCommands.WithdrawV1, refreshWithdrawn.Id,
                (draft, now) => draft.Withdraw(3, preparer, null, now), Withdrawn);
        }

        await using (var refreshedScope = run.Store(CompanyA))
        {
            var refreshed = await run.Reader(refreshedScope, approver).Owner.ListAsync(
                new(CompanyA, approver), null, default);
            Assert.Equal(SalesOrderDraftPendingApprovalReadOutcomeV1.Found, refreshed.Outcome);
            Assert.Equal(new[] { supportOwn.Id, regularOwn.Id }.Order(),
                refreshed.Resource!.Items.Select(item => item.DraftId).Order());
        }

        await using (var revokedScope = run.Store(CompanyA))
        {
            var outerCompany = new MutableCompanyContext(CompanyA);
            var outerAuthority = new MutableAuthority();
            var inner = run.DraftReader(revokedScope, approver);
            var proxy = AfterReadProxy.Create(inner, () => outerAuthority.Revoke(SalesPermissionManifestV1.OrdersApprove));
            var revoked = new SalesOrderDraftPendingApprovalReadServiceV1(
                proxy, outerCompany, new FixedUser(approver, CompanyA), outerAuthority);

            var result = await revoked.ListAsync(new(CompanyA, approver), null, default);

            Assert.Equal(SalesOrderDraftPendingApprovalReadOutcomeV1.Denied, result.Outcome);
            Assert.Null(result.Resource);
            Assert.Contains(outerAuthority.Measured, item =>
                item.Permission == SalesPermissionManifestV1.OrdersApprove && !item.Granted);
        }

        await using (var driftScope = run.Store(CompanyA))
        {
            var outerCompany = new MutableCompanyContext(CompanyA);
            var inner = run.DraftReader(driftScope, approver);
            var proxy = AfterReadProxy.Create(inner, () => outerCompany.Set(CompanyB));
            var drifting = new SalesOrderDraftPendingApprovalReadServiceV1(
                proxy, outerCompany, new FixedUser(approver, CompanyA), new MutableAuthority());

            var result = await drifting.ListAsync(new(CompanyA, approver), null, default);

            Assert.Equal(SalesOrderDraftPendingApprovalReadOutcomeV1.Denied, result.Outcome);
            Assert.Null(result.Resource);
        }
    }

    private static async Task<SalesOrderDraft> CreateAsync(
        ISalesOrderDraftStore store,
        Run run,
        Guid company,
        string key,
        string actor)
    {
        var owners = Owners.For(company);
        return (await store.StoreDraftAsync(
            run.Envelope($"create-{key}", actor),
            Hash($"create-{key}"),
            documentNo => Draft(owners, documentNo))).Draft;
    }

    private static async Task<SalesOrderDraft> CreateAndSubmitAsync(
        ISalesOrderDraftStore store,
        Run run,
        Guid company,
        string key,
        string actor)
    {
        var draft = await CreateAsync(store, run, company, key, actor);
        return (await store.UpdateDraftStateAsync(
            run.Envelope($"submit-{key}", actor),
            Hash($"submit-{key}"),
            SalesDraftCommands.SubmitV1,
            draft.Id,
            (current, now) => current.Submit(2, actor, now),
            Submitted))!.Draft;
    }

    private static SalesOrderDraft Draft(Owners owners, string documentNo)
    {
        var header = new SalesDraftHeader(owners.Party, owners.Account, Currency, Day);
        var money = new MoneyProfileSnapshot(
            owners.Company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), owners.Company, documentNo, header, money, T0);
        var unit = new UnitProfileSnapshot(
            owners.Company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(owners.Company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-D05", "Sentetik urun", Day, binding, binding, binding);
        draft.AddLine(draft.Version,
            new SalesDraftLineInput(Guid.NewGuid(), product, unit, "1.25", 1.25m), T0);
        return draft;
    }

    private static string Hash(string seed) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    private sealed record Owners(Guid Company, Guid Party, Guid Account)
    {
        public static Owners For(Guid company) => new(company, Guid.NewGuid(), Guid.NewGuid());
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        private SqlServerRelationalApiFactory Factory { get; }

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            await using var context = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, companyContext);
            await context.Database.MigrateAsync();
            return run;
        }

        public SalesDraftWriteEnvelope Envelope(string key, string actor) =>
            new(actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], $"d05-{key}", null, key,
                "Web", "127.0.0.1", "SENTETIK-agent");

        public StoreScope Store(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var options = new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext))
                .Options;
            var dbContext = new SalesDbContext(options, companyContext);
            var store = new SalesStore(dbContext, companyContext, collector, new FixedTimeProvider(T0));
            return new StoreScope(dbContext, store);
        }

        public ReaderBundle Reader(StoreScope scope, string actor, params string[] roles)
        {
            var company = new CompanyContext();
            company.Set(CompanyA);
            var user = new FixedUser(actor, CompanyA);
            var authority = new MutableAuthority();
            var draftReader = DraftReader(scope, actor, roles);
            return new ReaderBundle(
                new SalesOrderDraftPendingApprovalReadServiceV1(draftReader, company, user, authority));
        }

        public ISalesDraftService DraftReader(StoreScope scope, string actor, params string[] roles)
        {
            var company = new CompanyContext();
            company.Set(CompanyA);
            var owners = new NoOwners();
            return new SalesDraftService(
                company,
                new FixedUser(actor, CompanyA),
                new MutableAuthority(),
                new FixedActiveRoleCodeReader(roles),
                new FixedCorrelation("d05-read"),
                owners,
                owners,
                null,
                scope.Store,
                new SalesDraftServiceBindings(),
                new FixedTimeProvider(T0));
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed record ReaderBundle(SalesOrderDraftPendingApprovalReadServiceV1 Owner);

    private sealed class StoreScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;
        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class MutableCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class FixedUser(string actorId, params Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actorId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companies.ToHashSet();
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class MutableAuthority : IErpAuthorityRevalidationService
    {
        private readonly HashSet<string> granted =
        [
            SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersApprove,
        ];

        public List<(string Permission, Guid Company, bool Granted)> Measured { get; } = [];

        public void Revoke(string permission) => granted.Remove(permission);

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isGranted = companyId == CompanyA && granted.Contains(permission);
            Measured.Add((permission, companyId, isGranted));
            return Task.FromResult(isGranted
                ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(),
                    new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
        }
    }

    private sealed class FixedActiveRoleCodeReader(params string[] roles) : IActiveRoleCodeReader
    {
        private readonly IReadOnlySet<string> roleCodes = new HashSet<string>(roles, StringComparer.Ordinal);

        public Task<IReadOnlySet<string>> GetActiveRoleCodesAsync(
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(companyId == CompanyA
                ? roleCodes
                : (IReadOnlySet<string>)new HashSet<string>(StringComparer.Ordinal));
        }
    }

    private sealed class FixedCorrelation(string correlationId) : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => correlationId;
        public string? CausationId => null;
        public string SourceChannel => "Web";
        public string? ClientIpAddress => "127.0.0.1";
        public string? UserAgent => "SENTETIK-agent";
        public void Set(string correlationId, string? causationId, string sourceChannel,
            string? clientIpAddress, string? userAgent)
        {
        }
    }

    private sealed class NoOwners : ISalesPartyAccountPortV1, ISettingsPrerequisiteProvider
    {
        public Task<SalesPartyAccountAnswerV1> GetAuthorizedAsync(
            Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A pending-approval read asks no owner source.");

        public Task<UnitProfileV1> GetUnitProfileAsync(
            Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A pending-approval read asks no unit source.");

        public Task<MoneyProfileV1> GetMoneyProfileAsync(
            Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A pending-approval read asks no money source.");

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(
            string operation, DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A pending-approval read asks no period source.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    public class AfterReadProxy : DispatchProxy
    {
        private ISalesDraftService inner = null!;
        private Action afterRead = null!;

        public static ISalesDraftService Create(ISalesDraftService inner, Action afterRead)
        {
            var service = DispatchProxy.Create<ISalesDraftService, AfterReadProxy>();
            var proxy = (AfterReadProxy)(object)service;
            proxy.inner = inner;
            proxy.afterRead = afterRead;
            return service;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ISalesDraftService.GetDraftListAsync))
            {
                return ListAsync((SalesOrderDraftListQuery)args![0]!, (CancellationToken)args[1]!);
            }

            if (targetMethod?.Name == nameof(ISalesDraftService.GetDraftDetailAsync))
            {
                return DetailAsync((Guid)args![0]!, (CancellationToken)args[1]!);
            }

            throw new NotSupportedException(targetMethod?.Name);
        }

        private async Task<SalesOrderDraftListResponse> ListAsync(
            SalesOrderDraftListQuery query,
            CancellationToken cancellationToken)
        {
            var result = await inner.GetDraftListAsync(query, cancellationToken);
            afterRead();
            return result;
        }

        private async Task<SalesOrderDraftDetailView> DetailAsync(
            Guid draftId,
            CancellationToken cancellationToken)
        {
            var result = await inner.GetDraftDetailAsync(draftId, cancellationToken);
            afterRead();
            return result;
        }
    }
}
