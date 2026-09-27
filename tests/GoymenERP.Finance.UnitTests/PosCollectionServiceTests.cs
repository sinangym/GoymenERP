using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// DEC-FIN-014 through the real receipt and allocation services, in memory: a customer POS collection is saved,
/// reopened, recorded once, replayed, refused at a stale version, invisible to another company, and then distributed
/// at its GROSS amount to the customer's open item, whose debt closes. SENTETIK / NOT_ENV; not SQL or browser evidence
/// (that is <c>FinanceRecordSqlServerRelationalAcceptanceTests</c> / <c>FinanceCommandApiSqlServerRelationalAcceptanceTests</c>).
/// </summary>
public sealed class PosCollectionServiceTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly Guid Currency = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Bank = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Party = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Account = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Profile = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private const string Operation = "SENTETIK-operation";

    [Fact]
    public async Task A_POS_collection_is_saved_reopened_recorded_once_and_closes_the_customer_debt_at_gross()
    {
        await using var run = new Run();

        CommandResult saved;
        await using (var clerk = run.Receipts("clerk", Company))
        {
            saved = await clerk.Service.DraftSaveAsync(clerk.Context, Key("pos-save"),
                Save("1000.00", new PosCollectionInput("25.50", PosCommissionBearer.Company)), CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.Succeeded, saved.Outcome);
        var draft = saved.Resource!;
        Assert.Equal("1000.00", draft.Source.Amount.Money!.Amount);
        Assert.Equal(PosCommissionBearer.Company, draft.Pos!.CommissionBearer);
        Assert.Equal(("25.50", "974.50"), (draft.Pos.Commission.Money!.Amount, draft.Pos.BankNet.Money!.Amount));

        // Close and reopen: another scope reads exactly the stored facts.
        await using (var reader = run.Receipts("reader", Company))
        {
            var reopened = await reader.Service.DetailAuthorizedAsync(reader.Context,
                new SourceDetailRequest(draft.Source.Id), CancellationToken.None);
            Assert.Equal(ReadOutcome.Found, reopened.Outcome);
            Assert.Equal(draft.Pos, reopened.Resource!.Pos);
            Assert.Equal(SourceStage.Draft, reopened.Resource.Source.Stage);
        }

        // Another company sees nothing of it and cannot record it.
        await using (var foreign = run.Receipts("clerk", OtherCompany))
        {
            Assert.Equal(ReadOutcome.NotFound, (await foreign.Service.DetailAuthorizedAsync(foreign.Context,
                new SourceDetailRequest(draft.Source.Id), CancellationToken.None)).Outcome);
            var refused = await foreign.Service.RecordAsync(foreign.Context, Key("pos-foreign"),
                new RecordRequest(draft.Source.Id, draft.Source.Version), CancellationToken.None);
            Assert.Equal((CommandOutcome.ValidationFailed, "draft_not_found"), (refused.Outcome, refused.Issues.Single().Code));
        }

        var record = new RecordRequest(draft.Source.Id, draft.Source.Version);
        CommandResult recorded;
        await using (var recorder = run.Receipts("recorder", Company))
        {
            recorded = await recorder.Service.RecordAsync(recorder.Context, Key("pos-record"), record, CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.Succeeded, recorded.Outcome);
        Assert.Equal(SourceStage.Recorded, recorded.Resource!.Source.Stage);
        // The customer's credit is the gross: unallocated is measured from 1000.00, never from the net.
        Assert.Equal("1000.00", recorded.Resource.Unallocated.Amount.Money!.Amount);

        await using (var recorder = run.Receipts("recorder", Company))
        {
            var replay = await recorder.Service.RecordAsync(recorder.Context, Key("pos-record"), record, CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, true, recorded.OriginalCorrelationId),
                (replay.Outcome, replay.IsReplay, replay.OriginalCorrelationId));
            Assert.Equal(recorded.Resource.Pos, replay.Resource!.Pos);
            var stale = await recorder.Service.RecordAsync(recorder.Context, Key("pos-record-stale"), record, CancellationToken.None);
            Assert.Equal(CommandOutcome.VersionConflict, stale.Outcome);
        }

        await using (var db = run.Context(Company))
        {
            var moneyEvent = Assert.Single(await db.MoneyEvents.ToListAsync());
            Assert.Equal(("1000.00", "25.50", "974.50", PosCommissionBearers.Company),
                (moneyEvent.AmountText, moneyEvent.PosCommissionAmountText, moneyEvent.PosBankNetAmountText,
                    moneyEvent.PosCommissionBearer));
            var recordAudit = Assert.Single(await db.Set<AuditEvent>()
                .Where(audit => audit.EventType == "finance.manual-receipt.recorded.v1").ToListAsync());
            Assert.Equal(SourceChannels.Web, recordAudit.SourceChannel);
            Assert.Contains("\"channel\":\"Pos\"", recordAudit.AfterJson, StringComparison.Ordinal);
            Assert.DoesNotContain("974.50", recordAudit.AfterJson, StringComparison.Ordinal);
        }

        // The next step: the same record is distributed at its gross to the customer's open item, which closes.
        var openItem = await run.SeedOpenItemAsync("1000.00");
        FinanceAllocationCommandResult allocated;
        await using (var allocator = run.Allocations("allocator"))
        {
            allocated = await allocator.Service.CreateAsync(allocator.Context, Key("pos-allocate"),
                new CreateFinanceAllocationRequest(Guid.NewGuid(), draft.Source.Id, recorded.Resource.Source.Version,
                    openItem, "1", "1000.00", "SENTETIK POS tahsilatı brüt kapama"), CancellationToken.None);
        }
        Assert.Equal((CommandOutcome.Succeeded, "1000.00"), (allocated.Outcome, allocated.Resource!.Amount));

        await using (var db = run.Context(Company))
        {
            var item = await db.OpenItems.SingleAsync(row => row.Id == openItem);
            Assert.Equal((FinanceOpenItemStatuses.Closed, "0.00"), (item.Status, item.OutstandingAmountText));
            Assert.Single(await db.MoneyEvents.ToListAsync());
        }

        await using (var reader = run.Receipts("reader", Company))
        {
            var after = (await reader.Service.DetailAuthorizedAsync(reader.Context,
                new SourceDetailRequest(draft.Source.Id), CancellationToken.None)).Resource!;
            Assert.Equal((AllocationKnowledge.Known, "0.00"), (after.Unallocated.Knowledge, after.Unallocated.Amount.Money!.Amount));
            Assert.Equal(("25.50", "974.50"), (after.Pos!.Commission.Money!.Amount, after.Pos.BankNet.Money!.Amount));
        }
    }

    [Fact]
    public async Task Without_the_amount_grant_the_commission_and_net_are_hidden_like_the_gross()
    {
        await using var run = new Run();
        CommandResult saved;
        await using (var clerk = run.Receipts("clerk", Company))
        {
            saved = await clerk.Service.DraftSaveAsync(clerk.Context, Key("pos-save"),
                Save("1000.00", new PosCollectionInput("25.50", PosCommissionBearer.Customer)), CancellationToken.None);
        }

        await using var reader = run.Receipts("reader", Company, new DenyAmount());
        var detail = (await reader.Service.DetailAuthorizedAsync(reader.Context,
            new SourceDetailRequest(saved.Resource!.Source.Id), CancellationToken.None)).Resource!;
        Assert.Equal(PosCommissionBearer.Customer, detail.Pos!.CommissionBearer);
        Assert.Equal((AmountDisclosure.Redacted, AmountDisclosure.Redacted, AmountDisclosure.Redacted),
            (detail.Source.Amount.Disclosure, detail.Pos.Commission.Disclosure, detail.Pos.BankNet.Disclosure));
        Assert.Null(detail.Pos.Commission.Money);
        Assert.Null(detail.Pos.BankNet.Money);

        // A replay of the save to its own actor, whose amount grant is gone now, discloses no more than that allows.
        await using var clerkWithoutAmount = run.Receipts("clerk", Company, new DenyAmount());
        var replay = await clerkWithoutAmount.Service.DraftSaveAsync(clerkWithoutAmount.Context, Key("pos-save"),
            Save("1000.00", new PosCollectionInput("25.50", PosCommissionBearer.Customer)), CancellationToken.None);
        Assert.Equal((CommandOutcome.Succeeded, true), (replay.Outcome, replay.IsReplay));
        Assert.Equal((AmountDisclosure.Redacted, AmountDisclosure.Redacted),
            (replay.Resource!.Pos!.Commission.Disclosure, replay.Resource.Pos.BankNet.Disclosure));
    }

    [Fact]
    public async Task A_refused_commission_stores_nothing_and_an_edit_back_to_bank_transfer_clears_the_POS_facts()
    {
        await using var run = new Run();
        await using (var clerk = run.Receipts("clerk", Company))
        {
            var refused = await clerk.Service.DraftSaveAsync(clerk.Context, Key("pos-bad"),
                Save("1000.00", new PosCollectionInput("1000.00", PosCommissionBearer.Company)), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Equal(new ValidationIssue(PosCollectionRules.CommissionField, PosCollectionRules.CommissionNotBelowGross),
                Assert.Single(refused.Issues));
        }
        await using (var db = run.Context(Company))
        {
            Assert.Empty(await db.ReceiptDrafts.ToListAsync());
        }

        CommandResult saved;
        await using (var clerk = run.Receipts("clerk", Company))
        {
            saved = await clerk.Service.DraftSaveAsync(clerk.Context, Key("pos-save"),
                Save("1000.00", new PosCollectionInput("10", PosCommissionBearer.Company)), CancellationToken.None);
        }
        await using (var clerk = run.Receipts("clerk", Company))
        {
            var edited = await clerk.Service.DraftSaveAsync(clerk.Context, Key("pos-edit"),
                Save("1000.00", pos: null, saved.Resource!.Source.Id, saved.Resource.Source.Version), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, edited.Outcome);
            Assert.Null(edited.Resource!.Pos);
        }
        await using (var db = run.Context(Company))
        {
            var row = await db.ReceiptDrafts.SingleAsync();
            Assert.Equal((null, null, (int?)null), (row.PosCommissionAmountText, row.PosBankNetAmountText, row.PosCommissionBearer));
        }
    }

    private static CommandMetadata Key(string key) => new(key);

    private static DraftSaveRequest Save(string gross, PosCollectionInput? pos, Guid? draftId = null, string? version = null) =>
        new(draftId, version, new ReceiptPreparation(Party, Account, Bank, new MoneyText(gross, Currency),
            new ProfileReference(Profile.ToString("D"), "1"), new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 28),
            "SENTETIK-POS-SLIP", null, pos));

    private sealed class Run : IAsyncDisposable
    {
        private readonly string databaseName = $"finance-pos-{Guid.NewGuid():N}";

        public FinanceDbContext Context(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName).Options, companyContext);
        }

        public Scope<IManualReceiptService> Receipts(string actor, Guid company, IFinancePermissionPort? permissions = null)
        {
            var (context, companyContext, collector) = Tracked(company);
            var service = new ReceiptDraftService(context, companyContext, new User(actor, company),
                permissions ?? new AllowAll(), new BankReader(), new PartyPort(), new SettingsPort(), collector,
                new FinanceReceiptDraftBinding(Operation), Envelope(FinancePermissionNames.DraftSave), TimeProvider.System,
                Envelope(FinancePermissionNames.Record), new OpenPeriod());
            return new Scope<IManualReceiptService>(context, service, Finance(company, actor));
        }

        public Scope<IFinanceAllocationService> Allocations(string actor)
        {
            var (context, companyContext, collector) = Tracked(Company);
            var service = new FinanceAllocationService(context, companyContext, new User(actor, Company), new AllowAll(),
                collector, Envelope(FinancePermissionNames.AllocationWrite), TimeProvider.System);
            return new Scope<IFinanceAllocationService>(context, service, Finance(Company, actor));
        }

        public async Task<Guid> SeedOpenItemAsync(string amount)
        {
            await using var context = Context(Company);
            var row = new FinanceOpenItemRow
            {
                Id = Guid.NewGuid(), CompanyId = Company, SourceKind = "synthetic.receivable.v1",
                SourceId = Guid.NewGuid(), SourceLineId = Guid.NewGuid(), SourceVersion = "1",
                SourceBatchId = Guid.NewGuid(), SourceBatchVersion = "synthetic-v1", SourceBatchChecksum = new string('f', 64),
                PartyId = Party, CompanyAccountId = Account, CurrencyId = Currency,
                OriginalAmountText = amount, OutstandingAmountText = amount,
                Status = FinanceOpenItemStatuses.Open, Version = 1,
                CreatedBy = "synthetic", CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedBy = "synthetic", UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            context.OpenItems.Add(row);
            await context.SaveChangesAsync();
            return row.Id;
        }

        public async ValueTask DisposeAsync()
        {
            await using var context = Context(Company);
            await context.Database.EnsureDeletedAsync();
        }

        private (FinanceDbContext Context, CompanyContext Company, AuditEventCollector Collector) Tracked(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var context = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName)
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return (context, companyContext, collector);
        }

        private static FinanceContext Finance(Guid company, string actor) =>
            new(company, actor, $"pos-{Guid.NewGuid():N}", null);

        private static FinanceAuditEnvelope Envelope(string permission) =>
            new(permission, ["SENTETIK-FINANCE"], SourceChannels.Web);
    }

    private sealed class Scope<T>(FinanceDbContext db, T service, FinanceContext context) : IAsyncDisposable
    {
        public T Service => service;
        public FinanceContext Context => context;
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class AllowAll : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    private sealed class DenyAmount : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(action == FinanceAction.ReadAmount
            ? PermissionDecision.Denied
            : PermissionDecision.Allowed);
    }

    private sealed class BankReader : IFinanceBankAccountReader
    {
        private static BankAccountView View(Guid company) => new(Bank, company, Currency, "SENTETIK POS banka", true, true, "2");

        public Task<ReadResult<BankAccountPage>> ListAuthorizedAsync(FinanceContext context,
            CancellationToken cancellationToken) => Task.FromResult(new ReadResult<BankAccountPage>(ReadOutcome.Found,
            new BankAccountPage(context.CompanyId, [View(context.CompanyId)])));

        public Task<ReadResult<BankAccountView>> GetAuthorizedAsync(FinanceContext context, Guid accountId,
            CancellationToken cancellationToken) => Task.FromResult(accountId == Bank
            ? new ReadResult<BankAccountView>(ReadOutcome.Found, View(context.CompanyId))
            : new ReadResult<BankAccountView>(ReadOutcome.NotFound, null));
    }

    private sealed class PartyPort : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) => Task.FromResult(
            partyId == Party && companyAccountId == Account
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                    new PartyAccountEligibility(Party, Account, context.CompanyId, true, IsPartyActive: true))
                : new ReadResult<PartyAccountEligibility>(ReadOutcome.NotFound, null));
    }

    /// <summary>SENTETIK scale-2 profile — a test input, not a production scale.</summary>
    private sealed class SettingsPort : ISettingsPrerequisiteProvider
    {
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => Task.FromResult(new MoneyProfileV1(
            Company, Currency, "TRY", operation, new ProfileVersionV1(Profile, 1), 2, true, new DateOnly(2026, 1, 1), null));

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn,
            DateOnly? valueOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>A TEST INPUT that answers "open" for any date; not a period policy.</summary>
    private sealed class OpenPeriod : IFinanceRecordPeriodRule
    {
        public Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
            DateTimeOffset recordedAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(new RecordPeriodAnswer(RecordPeriodOutcome.Open, null));
    }

    private sealed class User(string actor, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
