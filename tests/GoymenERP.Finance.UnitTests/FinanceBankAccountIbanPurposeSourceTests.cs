using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

// Synthetic owner read facts only; actual native receipt provenance is covered by the SQL partial source.
public sealed class FinanceBankAccountIbanPurposeSourceTests
{
    private static readonly Guid Company = Guid.NewGuid(), Other = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
    private const string Actor = "SENTETIK-checker", Maker = "SENTETIK-original-maker", Iban = "TR060006100519786457841327";

    [Fact]
    public async Task Pending_human_source_contains_actual_values_but_snapshot_and_inbox_wire_never_serialize_them()
    {
        await using var run = new Rig(); var id = await run.Seed();
        var snapshot = await run.Source.ReadDecisionAsync(run.Context, id, default);
        Assert.NotNull(snapshot); Assert.True(snapshot.Pending); Assert.Equal("9", snapshot.SourceVersion);
        var facts = snapshot.BankAccountIbanChangeProof!.HumanFacts!;
        Assert.Equal((Iban, "SENTETIK current reason"), (facts.ProposedIban, facts.Reason));
        var wire = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(Iban, wire, StringComparison.Ordinal); Assert.DoesNotContain(facts.Reason, wire, StringComparison.Ordinal);
        var inbox = await run.Source.ListPendingAsync(run.Context, 10, default);
        Assert.DoesNotContain(Iban, JsonSerializer.Serialize(inbox), StringComparison.Ordinal); Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Original_version_uses_only_exact_actor_payload_receipt_even_after_more_than_128_unrelated_later_versions()
    {
        await using var run = new Rig(); var id = await run.Seed();
        await run.Receipt(id, "3", Actor);
        for (var version = 10; version < 145; version++) await run.Receipt(id, version.ToString(), Actor);
        await run.Receipt(id, "3", "SENTETIK-other-checker");
        var selected = await run.Source.ReadDecisionVersionAsync(run.Context, id, "3", default);
        Assert.NotNull(selected); Assert.False(selected.Pending); Assert.Equal("3", selected.SourceVersion);
        Assert.Equal(Maker, selected.Item.PreparedBy); Assert.Null(selected.BankAccountIbanChangeProof!.HumanFacts);
        Assert.NotEqual((await run.Source.ReadDecisionAsync(run.Context, id, default))!.Item.PreparedBy, selected.Item.PreparedBy);
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData("legacy")] [InlineData("foreign-company")] [InlineData("foreign-actor")] [InlineData("ambiguous")]
    public async Task Missing_unbound_or_duplicate_original_receipts_never_fall_back_to_the_current_pending_maker(string fault)
    {
        await using var run = new Rig(); var id = await run.Seed();
        if (fault != "legacy") await run.Receipt(id, "3", fault == "foreign-actor" ? "SENTETIK-other" : Actor, fault == "foreign-company" ? Other : Company);
        if (fault == "ambiguous") await run.Receipt(id, "3", Actor);
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.Source.ReadDecisionVersionAsync(run.Context, id, "3", default));
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Companywide_scope_and_post_owner_read_release_are_checked_without_a_bank_card_read_cogrant()
    {
        await using var run = new Rig(); var id = await run.Seed();
        run.Authority.DenyAt = 2;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => run.Source.ReadDecisionAsync(run.Context, id, default));
        Assert.All(run.Authority.Permissions, permission => Assert.Equal(ErpPermissions.FinanceBankAccountApprove, permission));
        run.Authority.Calls = 0; run.Authority.DenyAt = int.MaxValue; run.Authority.CompanyWide = false;
        Assert.Null(await run.Source.ReadDecisionAsync(run.Context, id, default));
        Assert.Null(await run.Source.ReadDecisionAsync(run.Context with { CompanyId = Other }, id, default));
    }

    private sealed class Rig : IAsyncDisposable
    {
        public readonly FinanceDbContext Db; public readonly Authority Authority = new();
        private readonly DbContextOptions<FinanceDbContext> options;
        public readonly FinanceBankAccountIbanChangeWorkInboxSourceV1 Source;
        public readonly WorkInboxSourceContextV1 Context = new(Company, Actor, "SENTETIK", null);
        public Rig()
        {
            var company = new CompanyContext(); company.Set(Company);
            options = new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase("SENTETIK-iban-purpose-" + Guid.NewGuid()).Options;
            Db = new(options, company);
            Source = new(Db, new(company, new FinancePartyCreditLimitWorkInboxSourceV1Tests.FixedUser(Actor, [Company]), Authority));
        }
        public async Task<Guid> Seed()
        {
            var row = FinanceBankAccountWorkInboxSourceV1Tests.Account(Company, At, "SENTETIK-card-maker", FinanceBankAccountService.Approved);
            row.Version = 9; row.PendingIban = Iban; row.PendingIbanProposedBy = "SENTETIK-new-maker";
            row.PendingIbanProposedAtUtc = At.AddDays(1); row.PendingIbanReason = "SENTETIK current reason";
            Db.BankAccounts.Add(row); await Db.SaveChangesAsync(); Db.ChangeTracker.Clear(); return row.Id;
        }
        public async Task Receipt(Guid id, string version, string actor, Guid? company = null)
        {
            var expected = long.Parse(version); var ownerCompany = company ?? Company;
            var hash = FinanceBankAccountService.PayloadHash(FinanceBankAccountService.ApproveIbanChangeCommand, new BankAccountIbanChangeApproveRequest(id, version));
            var proof = new FinanceBankAccountService.IbanChangeReceiptProof(FinanceBankAccountService.ApproveIbanChangeCommand, ownerCompany, id,
                Maker, At, expected, expected + 1, hash, new string('a', 64));
            var answer = new BankAccountCommandResult(CommandOutcome.Succeeded, new(id, ownerCompany, Guid.NewGuid(), "SENTETIK original", true, false,
                (expected + 1).ToString()), false, "SENTETIK-original", []);
            var row = new FinanceCommandRow { CompanyId = ownerCompany, CommandName = proof.CommandName, BankAccountId = id,
                IdempotencyKeyHash = Guid.NewGuid().ToString("N").PadRight(64, '0'), PayloadHash = hash, ActorId = actor,
                CorrelationId = "SENTETIK-original", RecordedAtUtc = At, ResultJson = FinanceBankAccountService.SerializeIbanChangeReceipt(answer, proof) };
            // The foreign-company case is a synthetic read-store row; a separate matching-company context respects tenant guards.
            if (ownerCompany != Company)
            {
                var foreignCompany = new CompanyContext(); foreignCompany.Set(ownerCompany);
                await using var foreign = new FinanceDbContext(options, foreignCompany);
                foreign.Commands.Add(row); await foreign.SaveChangesAsync(); return;
            }
            Db.Commands.Add(row);
            await Db.SaveChangesAsync(); Db.ChangeTracker.Clear();
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public int Calls, DenyAt = int.MaxValue; public bool CompanyWide = true; public List<string> Permissions = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        { Calls++; Permissions.Add(permission); return Task.FromResult(new PermissionScope(Calls < DenyAt, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
}
