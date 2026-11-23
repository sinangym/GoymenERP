using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Counterparty.UnitTests;

/// <summary>
/// FNS-D121 SENTETIK / NOT_ENV service evidence (in-memory provider): the filtered unique index, checks and audit
/// atomicity are proven by the real SQL facts.
/// </summary>
public sealed class CounterpartyPartyIbanTests
{
    private const string Iban = "TR130006200000000000000002";
    private const string OtherIban = "TR550011100000000000000003";

    [Theory]
    [InlineData("TR13 0006 2000 0000 0000 0000 02", true)]
    [InlineData("tr130006200000000000000002", true)]
    [InlineData("TR130006200000000000000003", false)]
    [InlineData("TR13000620000000000000000", false)]
    [InlineData("DE89370400440532013000", false)]
    [InlineData("TR1300062000000000000000A2", false)]
    public void Only_a_turkish_iban_with_valid_mod_97_check_digits_is_accepted(string text, bool valid) =>
        Assert.Equal(valid, CounterpartyIbanV1.IsValidTurkish(CounterpartyIbanV1.Normalize(text)));

    [Fact]
    public async Task Add_lists_replays_and_refuses_another_payload_a_second_active_copy_and_an_invalid_iban()
    {
        await using var run = new Run();
        var request = new AddCounterpartyPartyIbanV1(Guid.NewGuid(), "TR13 0006 2000 0000 0000 0000 02", " SENTETIK Banka ", "Ana hesap");
        var added = await run.AddAsync("SENTETIK-a", request);
        Assert.Equal(CounterpartyPartyIbanOutcomeV1.Succeeded, added.Outcome);
        Assert.Equal((Iban, "SENTETIK Banka", "Ana hesap", true, "1"),
            (added.Resource!.Iban, added.Resource.BankName, added.Resource.Description, added.Resource.IsActive, added.Resource.Version));
        Assert.True((await run.AddAsync("SENTETIK-a", request)).Replayed);
        Assert.Equal(CounterpartyPartyIbanOutcomeV1.IdempotencyConflict,
            (await run.AddAsync("SENTETIK-a", request with { BankName = "Baska" })).Outcome);
        var copy = await run.AddAsync("SENTETIK-b", request with { IbanId = Guid.NewGuid() });
        Assert.Equal((CounterpartyPartyIbanOutcomeV1.VersionConflict, CounterpartyPartyIbanCodesV1.AlreadyListed), (copy.Outcome, copy.Code));
        Assert.Equal(CounterpartyPartyIbanCodesV1.IbanInvalid,
            (await run.AddAsync("SENTETIK-c", request with { IbanId = Guid.NewGuid(), Iban = "TR130006200000000000000003" })).Code);
        Assert.Equal(CounterpartyPartyIbanCodesV1.BankNameRequired,
            (await run.AddAsync("SENTETIK-d", request with { IbanId = Guid.NewGuid(), Iban = OtherIban, BankName = " " })).Code);

        var list = (await run.Service.ListAsync(run.Party, default)).List!;
        Assert.Equal([Iban], list.Ibans.Select(x => x.Iban).ToArray());
        Assert.Single(run.Audit.Events);
        Assert.Equal(("counterparty.party-iban.added.v1", CounterpartyPartyIbanPermissionsV1.Manage),
            (run.Audit.Events[0].EventType, run.Audit.Events[0].PermissionCode));
    }

    [Fact]
    public async Task Deactivate_needs_the_version_and_a_reason_keeps_the_row_and_frees_the_iban_for_a_new_row()
    {
        await using var run = new Run();
        var added = (await run.AddAsync("SENTETIK-a", new(Guid.NewGuid(), Iban, "SENTETIK Banka", null))).Resource!;
        Assert.Equal(CounterpartyPartyIbanCodesV1.ReasonRequired,
            (await run.DeactivateAsync("SENTETIK-x", added.Id, new("1", " "))).Code);
        Assert.Equal(CounterpartyPartyIbanOutcomeV1.VersionConflict,
            (await run.DeactivateAsync("SENTETIK-y", added.Id, new("2", "SENTETIK yanlis"))).Outcome);
        var deactivated = await run.DeactivateAsync("SENTETIK-z", added.Id, new("1", "SENTETIK hesap kapandi"));
        Assert.Equal((false, "2", "SENTETIK hesap kapandi"),
            (deactivated.Resource!.IsActive, deactivated.Resource.Version, deactivated.Resource.DeactivationReason));
        Assert.Equal(CounterpartyPartyIbanOutcomeV1.VersionConflict,
            (await run.DeactivateAsync("SENTETIK-w", added.Id, new("2", "SENTETIK tekrar"))).Outcome);
        Assert.Equal(CounterpartyPartyIbanOutcomeV1.Succeeded, (await run.AddAsync("SENTETIK-b", new(Guid.NewGuid(), Iban, "Yeni", null))).Outcome);
        Assert.Equal(2, (await run.Service.ListAsync(run.Party, default)).List!.Ibans.Count);
        Assert.Equal("counterparty.party-iban.deactivated.v1", run.Audit.Events[1].EventType);
        Assert.Equal(CounterpartyPartyIbanOutcomeV1.NotFound,
            (await run.DeactivateAsync("SENTETIK-n", Guid.NewGuid(), new("1", "SENTETIK"))).Outcome);
    }

    [Fact]
    public async Task A_row_is_never_deleted_or_edited_in_place()
    {
        await using var run = new Run();
        var added = (await run.AddAsync("SENTETIK-a", new(Guid.NewGuid(), Iban, "SENTETIK Banka", null))).Resource!;
        var row = await run.Db.PartyIbans.SingleAsync(x => x.Id == added.Id);
        row.BankName = "Degisti";
        Assert.Throws<InvalidOperationException>(() => run.Db.SaveChanges());
        run.Db.ChangeTracker.Clear();
        run.Db.PartyIbans.Remove(await run.Db.PartyIbans.SingleAsync(x => x.Id == added.Id));
        Assert.Throws<InvalidOperationException>(() => run.Db.SaveChanges());
    }

    [Fact]
    public async Task A_party_without_an_account_in_the_company_is_not_found_and_permissions_fence_both_sides()
    {
        await using var run = new Run();
        Assert.Equal(CounterpartyPartyIbanOutcomeV1.NotFound,
            (await run.Service.AddAsync(Guid.NewGuid(), new(Guid.NewGuid(), Iban, "SENTETIK", null), "SENTETIK-k", "SENTETIK-c",
                null, "Web", null, null, default)).Outcome);
        run.Authority.Denied.Add(CounterpartyPartyIbanPermissionsV1.Manage);
        Assert.Equal(CounterpartyPartyIbanOutcomeV1.Denied, (await run.AddAsync("SENTETIK-a", new(Guid.NewGuid(), Iban, "SENTETIK", null))).Outcome);
        Assert.Equal(CounterpartyPartyIbanOutcomeV1.Succeeded, (await run.Service.ListAsync(run.Party, default)).Outcome);
        run.Authority.Denied.Add(CounterpartyPartyIbanPermissionsV1.Read);
        Assert.Equal(CounterpartyPartyIbanOutcomeV1.Denied, (await run.Service.ListAsync(run.Party, default)).Outcome);
        Assert.Empty(await run.Db.PartyIbans.ToListAsync());
    }

    [Fact]
    public async Task The_owner_source_answers_only_active_rows_of_the_context_company()
    {
        await using var run = new Run();
        var first = (await run.AddAsync("SENTETIK-a", new(Guid.NewGuid(), Iban, "SENTETIK", null))).Resource!;
        await run.AddAsync("SENTETIK-b", new(Guid.NewGuid(), OtherIban, "SENTETIK", null));
        await run.DeactivateAsync("SENTETIK-c", first.Id, new("1", "SENTETIK kapandi"));
        var source = new CounterpartyPartyIbanSourceV1(run.Db, run.Company);
        var found = (await source.FindPartiesAsync([Iban, OtherIban, "TR130006200000000000000003"], default))!;
        Assert.Equal([OtherIban], found.Keys.ToArray());
        Assert.Equal([run.Party], found[OtherIban]);
        run.Company.Set(Guid.NewGuid());
        Assert.Empty((await source.FindPartiesAsync([OtherIban], default))!);
    }

    [Fact]
    public void The_list_is_company_bound_one_active_copy_per_party_and_its_down_refuses_while_rows_exist()
    {
        using var db = new CounterpartyDbContext(new DbContextOptionsBuilder<CounterpartyDbContext>()
            .UseSqlServer("Server=invalid;Database=Synthetic;Integrated Security=true;TrustServerCertificate=true").Options,
            new MutableCompany(Guid.NewGuid()));
        var model = db.GetService<IDesignTimeModel>().Model;
        var iban = model.FindEntityType(typeof(CounterpartyPartyIbanRow))!;
        Assert.Equal(("counterparty", "PartyIbans"), (iban.GetSchema(), iban.GetTableName()));
        Assert.NotEmpty(iban.GetDeclaredQueryFilters());
        Assert.Equal(CounterpartyDbContext.PartyIbanContentCheck,
            Assert.Single(iban.GetCheckConstraints(), x => x.Name == "CK_PartyIbans_Content").Sql);
        Assert.Equal(CounterpartyDbContext.PartyIbanStateCheck,
            Assert.Single(iban.GetCheckConstraints(), x => x.Name == "CK_PartyIbans_State").Sql);
        var unique = Assert.Single(iban.GetIndexes(), x => x.IsUnique);
        Assert.Equal(new[] { "CompanyId", "PartyId", "Iban" }, unique.Properties.Select(x => x.Name).ToArray());
        Assert.Equal("[IsActive] = 1", unique.GetFilter());
        Assert.True(iban.FindProperty(nameof(CounterpartyPartyIbanRow.Version))!.IsConcurrencyToken);

        var migration = new AddPartyIbans();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 52252", guard);
        Assert.Contains("[counterparty].[PartyIbans]", guard);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private sealed class Run : IAsyncDisposable
    {
        public Guid CompanyId { get; } = Guid.NewGuid();
        public Guid Party { get; } = Guid.NewGuid();
        public MutableCompany Company { get; }
        public CounterpartyDbContext Db { get; }
        public Authority Authority { get; } = new();
        public Collector Audit { get; } = new();
        public CounterpartyPartyIbanService Service { get; }

        public Run()
        {
            Company = new MutableCompany(CompanyId);
            Db = new CounterpartyDbContext(new DbContextOptionsBuilder<CounterpartyDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Company);
            Db.CompanyPartyAccounts.Add(new CompanyPartyAccount(Guid.NewGuid(), CompanyId, Party, PartyRole.Customer));
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
            Service = new CounterpartyPartyIbanService(Db, new User(CompanyId), Company, Authority, Audit, TimeProvider.System);
        }

        public Task<CounterpartyPartyIbanCommandResultV1> AddAsync(string key, AddCounterpartyPartyIbanV1 request) =>
            Service.AddAsync(Party, request, key, "SENTETIK-correlation", null, "Web", null, null, default);

        public Task<CounterpartyPartyIbanCommandResultV1> DeactivateAsync(string key, Guid id, DeactivateCounterpartyPartyIbanV1 request) =>
            Service.DeactivateAsync(Party, id, request, key, "SENTETIK-correlation", null, "Web", null, null, default);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public HashSet<string> Denied { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Denied.Contains(permission)
                ? PermissionScope.Denied
                : new PermissionScope(true, true, new HashSet<string> { "SENTETIK-IBAN" }, new HashSet<Guid>(), new HashSet<Guid>(),
                    new HashSet<Guid>()));
    }

    private sealed class User(Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "SENTETIK-actor";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => companyId == company;
    }

    internal sealed class MutableCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved { get; private set; } = true;
        public Guid CompanyId { get; private set; } = companyId;

        public void Set(Guid value)
        {
            CompanyId = value;
            IsResolved = value != Guid.Empty;
        }
    }

    private sealed class Collector : IAuditEventCollector
    {
        public List<AuditEventDraft> Events { get; } = [];
        public void Enqueue(AuditEventDraft item) => Events.Add(item);
    }
}
