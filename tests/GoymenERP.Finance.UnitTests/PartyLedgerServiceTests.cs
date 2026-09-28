using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// The first-release party ledger (cari hesap ekstresi) over facts written by the REAL receipt, reversal, portfolio and
/// allocation services, in memory: an admitted open item, a recorded receipt (its Record replayed), its FIN-T06
/// reversal, a recorded payment's outgoing event, a cheque collection distributed by F-03 to the open item, a second
/// currency, a draft, another party and another company. Only recorded facts are rows, each once; the reversal is its
/// own row; the allocation is no row; the balance runs from zero (OPEN-018) per currency; amounts are redacted without
/// the amount grant. SENTETIK / NOT_ENV; the SQL evidence is <c>FinancePartyLedgerSqlServerRelationalAcceptanceTests</c>.
/// </summary>
public sealed class PartyLedgerServiceTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly Guid Currency = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Usd = Guid.Parse("23232323-2323-4232-8232-232323232323");
    private static readonly Guid Bank = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Party = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Account = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Profile = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private const string Operation = "SENTETIK-operation";
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 30);

    [Fact]
    public async Task Only_recorded_facts_are_rows_each_once_with_the_reversal_as_its_own_row_and_a_balance_from_zero()
    {
        await using var run = new Run();
        var facts = await run.SeedStoryAsync();

        PartyLedgerResult result;
        await using (var reader = run.Ledger("reader", Company))
        {
            result = await reader.Service.ReadAuthorizedAsync(reader.Context, Request(), CancellationToken.None);
        }

        Assert.Equal(PartyLedgerOutcome.Found, result.Outcome);
        var ledger = result.Ledger!;
        Assert.Equal((Company, Party, Account, From, To, PartyLedgerCarryForward.NoneOpen018),
            (ledger.CompanyId, ledger.PartyId, ledger.CompanyAccountId, ledger.From, ledger.To, ledger.CarryForward));
        Assert.Equal([Currency, Usd], ledger.Currencies.Select(currency => currency.CurrencyId).Order().ToArray());

        var tl = ledger.Currencies.Single(currency => currency.CurrencyId == Currency);
        Assert.Equal(
            [
                (PartyLedgerEntryKind.OpenItem, new DateOnly(2026, 9, 10), PartyLedgerSide.Debit, "1000.00", PartyLedgerBalanceSide.Debit, "1000.00", facts.OpenItem),
                (PartyLedgerEntryKind.ManualReceipt, new DateOnly(2026, 9, 15), PartyLedgerSide.Credit, "400.00", PartyLedgerBalanceSide.Debit, "600.00", facts.Receipt),
                (PartyLedgerEntryKind.ManualReceiptReversal, new DateOnly(2026, 9, 20), PartyLedgerSide.Debit, "400.00", PartyLedgerBalanceSide.Debit, "1000.00", facts.Reversal),
                (PartyLedgerEntryKind.ManualBankPayment, new DateOnly(2026, 9, 22), PartyLedgerSide.Debit, "50.00", PartyLedgerBalanceSide.Debit, "1050.00", facts.Payment),
                (PartyLedgerEntryKind.CustomerInstrumentCollection, new DateOnly(2026, 9, 25), PartyLedgerSide.Credit, "1200.00", PartyLedgerBalanceSide.Credit, "150.00", facts.Collection),
            ],
            tl.Entries.Select(entry => (entry.Kind, entry.OccurredOn, entry.Side, entry.Amount.Money!.Amount,
                entry.RunningBalance.Side!.Value, entry.RunningBalance.Amount.Money!.Amount, entry.SourceId)).ToArray());
        Assert.Equal(("1450.00", "1600.00", PartyLedgerBalanceSide.Credit, "150.00"),
            (tl.DebitTotal.Money!.Amount, tl.CreditTotal.Money!.Amount, tl.ClosingBalance.Side!.Value,
                tl.ClosingBalance.Amount.Money!.Amount));

        // Each row links to what opens it: the reversal to its original, the collection to its cheque, the open item to
        // its official document and due date.
        var reversal = tl.Entries.Single(entry => entry.Kind == PartyLedgerEntryKind.ManualReceiptReversal);
        Assert.Equal(facts.Receipt, reversal.ReversesSourceId);
        Assert.Equal(facts.Instrument, tl.Entries.Single(entry => entry.Kind == PartyLedgerEntryKind.CustomerInstrumentCollection).InstrumentId);
        var openItem = tl.Entries.Single(entry => entry.Kind == PartyLedgerEntryKind.OpenItem);
        Assert.Equal((facts.Document, (DateOnly?)new DateOnly(2026, 10, 10)), (openItem.DocumentId, openItem.DueOn));
        Assert.All(tl.Entries, entry => Assert.Equal(entry.Amount.Money!.CurrencyId, Currency));

        // OPEN-012: the second currency has its own rows and balance; nothing is converted or added across.
        var usd = ledger.Currencies.Single(currency => currency.CurrencyId == Usd);
        var usdRow = Assert.Single(usd.Entries);
        Assert.Equal((PartyLedgerEntryKind.ManualReceipt, "75.5", PartyLedgerBalanceSide.Credit, "75.5"),
            (usdRow.Kind, usdRow.Amount.Money!.Amount, usd.ClosingBalance.Side!.Value, usd.ClosingBalance.Amount.Money!.Amount));
        Assert.Equal(("0.0", "75.5"), (usd.DebitTotal.Money!.Amount, usd.CreditTotal.Money!.Amount));
    }

    [Fact]
    public async Task The_range_starts_from_zero_with_nothing_carried_in_and_reopening_answers_the_same()
    {
        await using var run = new Run();
        await run.SeedStoryAsync();

        PartyLedgerView first;
        PartyLedgerView reopened;
        await using (var reader = run.Ledger("reader", Company))
        {
            first = (await reader.Service.ReadAuthorizedAsync(reader.Context,
                Request(new DateOnly(2026, 9, 16), new DateOnly(2026, 9, 24)), CancellationToken.None)).Ledger!;
        }

        await using (var again = run.Ledger("reader", Company))
        {
            reopened = (await again.Service.ReadAuthorizedAsync(again.Context,
                Request(new DateOnly(2026, 9, 16), new DateOnly(2026, 9, 24)), CancellationToken.None)).Ledger!;
        }

        Assert.Equal(Json(first), Json(reopened));
        var tl = Assert.Single(first.Currencies);
        // The open item (09-10) and the receipt (09-15) are before the range: nothing of them is carried in (OPEN-018).
        Assert.Equal(
            [(PartyLedgerEntryKind.ManualReceiptReversal, "400.00"), (PartyLedgerEntryKind.ManualBankPayment, "450.00")],
            tl.Entries.Select(entry => (entry.Kind, entry.RunningBalance.Amount.Money!.Amount)).ToArray());
        Assert.Equal((PartyLedgerBalanceSide.Debit, "450.00"), (tl.ClosingBalance.Side!.Value, tl.ClosingBalance.Amount.Money!.Amount));

        // An empty range is an empty ledger, not a zero row.
        await using var empty = run.Ledger("reader", Company);
        var none = (await empty.Service.ReadAuthorizedAsync(empty.Context,
            Request(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)), CancellationToken.None)).Ledger!;
        Assert.Empty(none.Currencies);
    }

    [Fact]
    public async Task Without_the_amount_grant_every_amount_total_and_balance_side_is_withheld()
    {
        await using var run = new Run();
        await run.SeedStoryAsync();

        await using var blind = run.Ledger("reader", Company, new DenyAmount());
        var result = await blind.Service.ReadAuthorizedAsync(blind.Context, Request(), CancellationToken.None);
        Assert.Equal(PartyLedgerOutcome.Found, result.Outcome);
        using var document = JsonDocument.Parse(Json(result.Ledger!));
        var views = AmountViews(document.RootElement).ToList();
        // TRY: 5 rows × (amount + balance) + debit + credit + closing; USD: 1 × 2 + 3.
        Assert.Equal(5 * 2 + 3 + 1 * 2 + 3, views.Count);
        Assert.All(views, view =>
        {
            Assert.Equal("Redacted", view.GetProperty("disclosure").GetString());
            Assert.Equal(JsonValueKind.Null, view.GetProperty("money").ValueKind);
        });
        var balances = BalanceViews(document.RootElement).ToList();
        Assert.Equal(5 + 1 + 1 + 1, balances.Count);
        Assert.All(balances, balance => Assert.Equal(JsonValueKind.Null, balance.GetProperty("side").ValueKind));
        string[] stored = ["1000.00", "400.00", "50.00", "1200.00", "600.00", "1050.00", "150.00", "1450.00", "1600.00", "75.5"];
        Assert.DoesNotContain(ScalarTokens(document.RootElement), token => stored.Contains(token, StringComparer.Ordinal));
        // The rows themselves stay: kind, date, column and link are not amounts.
        Assert.Equal(5, result.Ledger!.Currencies.Single(currency => currency.CurrencyId == Currency).Entries.Count);
    }

    [Fact]
    public async Task Another_company_reads_nothing_and_refusals_answer_before_any_fact()
    {
        await using var run = new Run();
        await run.SeedStoryAsync();

        await using (var foreign = run.Ledger("reader", OtherCompany))
        {
            var ledger = (await foreign.Service.ReadAuthorizedAsync(foreign.Context, Request(), CancellationToken.None)).Ledger!;
            // Company B's own seeded fact for the same ids is its own; company A's facts never appear.
            var only = Assert.Single(Assert.Single(ledger.Currencies).Entries);
            Assert.Equal((OtherCompany, "999.00"), (ledger.CompanyId, only.Amount.Money!.Amount));
        }

        await using (var denied = run.Ledger("reader", Company, new DenyAll()))
        {
            Assert.Equal(new PartyLedgerResult(PartyLedgerOutcome.Denied, null),
                await denied.Service.ReadAuthorizedAsync(denied.Context, Request(), CancellationToken.None));
        }

        await using (var unavailable = run.Ledger("reader", Company, new UnavailableAll()))
        {
            Assert.Equal(new PartyLedgerResult(PartyLedgerOutcome.Unavailable, null),
                await unavailable.Service.ReadAuthorizedAsync(unavailable.Context, Request(), CancellationToken.None));
        }

        // Revoked between the reads: nothing is answered.
        await using (var revoked = run.Ledger("reader", Company, new RevokedAfterFirstRead()))
        {
            Assert.Equal(new PartyLedgerResult(PartyLedgerOutcome.Denied, null),
                await revoked.Service.ReadAuthorizedAsync(revoked.Context, Request(), CancellationToken.None));
        }

        // A context that is not the request's own company is refused before the permission is even asked.
        await using (var mismatched = run.Ledger("reader", Company))
        {
            var foreignContext = mismatched.Context with { CompanyId = OtherCompany };
            Assert.Equal(PartyLedgerOutcome.Denied,
                (await mismatched.Service.ReadAuthorizedAsync(foreignContext, Request(), CancellationToken.None)).Outcome);
        }
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", "55555555-5555-4555-8555-555555555555", "2026-09-01", "2026-09-30")]
    [InlineData("44444444-4444-4444-8444-444444444444", "00000000-0000-0000-0000-000000000000", "2026-09-01", "2026-09-30")]
    [InlineData("44444444-4444-4444-8444-444444444444", "55555555-5555-4555-8555-555555555555", "2026-09-30", "2026-09-01")]
    [InlineData("44444444-4444-4444-8444-444444444444", "55555555-5555-4555-8555-555555555555", "2026-09-01", "9999-12-31")]
    public async Task A_malformed_request_is_invalid(string partyId, string accountId, string from, string to)
    {
        await using var run = new Run();
        await using var reader = run.Ledger("reader", Company);
        var result = await reader.Service.ReadAuthorizedAsync(reader.Context,
            new PartyLedgerRequest(Guid.Parse(partyId), Guid.Parse(accountId), DateOnly.Parse(from), DateOnly.Parse(to)),
            CancellationToken.None);
        Assert.Equal(new PartyLedgerResult(PartyLedgerOutcome.Invalid, null), result);
    }

    [Fact]
    public async Task More_rows_than_one_answer_carries_are_refused_rather_than_cut()
    {
        await using var run = new Run();
        await using (var db = run.Context(Company))
        {
            for (var index = 0; index <= PartyLedgerRules.MaximumEntries; index++)
            {
                db.MoneyEvents.Add(Run.Event(Company, Guid.NewGuid(), MoneyEventKinds.ManualBankReceiptRecorded, Currency,
                    "1.00", new DateOnly(2026, 9, 1 + index % 28)));
            }

            await db.SaveChangesAsync();
        }

        await using var reader = run.Ledger("reader", Company);
        Assert.Equal(new PartyLedgerResult(PartyLedgerOutcome.TooManyEntries, null),
            await reader.Service.ReadAuthorizedAsync(reader.Context, Request(), CancellationToken.None));
        Assert.Equal(PartyLedgerOutcome.Found, (await reader.Service.ReadAuthorizedAsync(reader.Context,
            Request(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1)), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task A_money_event_kind_the_ledger_does_not_know_fails_the_read_instead_of_hiding_a_row()
    {
        await using var run = new Run();
        await using (var db = run.Context(Company))
        {
            db.MoneyEvents.Add(Run.Event(Company, Guid.NewGuid(), 99, Currency, "1.00", new DateOnly(2026, 9, 3)));
            await db.SaveChangesAsync();
        }

        await using var reader = run.Ledger("reader", Company);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reader.Service.ReadAuthorizedAsync(reader.Context, Request(), CancellationToken.None));
    }

    private static PartyLedgerRequest Request(DateOnly? from = null, DateOnly? to = null) =>
        new(Party, Account, from ?? From, to ?? To);

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, FinanceJson.CreateOptions());

    private static IEnumerable<JsonElement> AmountViews(JsonElement element) =>
        Walk(element).Where(item => item.TryGetProperty("disclosure", out _) && item.TryGetProperty("money", out _));

    private static IEnumerable<JsonElement> BalanceViews(JsonElement element) =>
        Walk(element).Where(item => item.TryGetProperty("side", out _) && item.TryGetProperty("amount", out _)
            && !item.TryGetProperty("kind", out _));

    private static IEnumerable<JsonElement> Walk(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
            foreach (var nested in element.EnumerateObject().SelectMany(property => Walk(property.Value)))
            {
                yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var nested in element.EnumerateArray().SelectMany(Walk))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Every string and number value, whole tokens only (no substring matching: ids contain digits too).</summary>
    private static IEnumerable<string> ScalarTokens(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => ScalarTokens(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(ScalarTokens),
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.Number => [element.GetRawText()],
        _ => [],
    };

    private static CommandMetadata Key(string key) => new(key);

    private sealed record Story(Guid OpenItem, Guid Document, Guid Receipt, Guid Reversal, Guid Payment, Guid Collection,
        Guid Instrument);

    private sealed class Run : IAsyncDisposable
    {
        private readonly string databaseName = $"finance-ledger-{Guid.NewGuid():N}";

        public FinanceDbContext Context(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName).Options, companyContext);
        }

        public Scope<IPartyLedgerService> Ledger(string actor, Guid company, IFinancePermissionPort? permissions = null)
        {
            var (context, companyContext, _) = Tracked(company);
            var service = new PartyLedgerService(context, companyContext, new User(actor, company),
                permissions ?? new AllowAll());
            return new Scope<IPartyLedgerService>(context, service, Finance(company, actor));
        }

        /// <summary>
        /// The ledger's story through the real services: an admitted open item (09-10, 1000.00, due 10-10); a receipt
        /// recorded on 09-15 (400.00) and its Record replayed; its reversal dated 09-20; a recorded payment's outgoing
        /// event on 09-22 (50.00); a cheque collected on 09-25 (1200.00) and distributed by F-03 to the open item. Also,
        /// none of which may appear: a USD receipt kept for its own currency (75.5), a draft, another party's receipt
        /// and company B's own fact.
        /// </summary>
        public async Task<Story> SeedStoryAsync()
        {
            var document = Guid.NewGuid();
            var openItem = await SeedOpenItemAsync(document);

            Guid receipt;
            await using (var clerk = Receipts("clerk"))
            {
                var saved = await clerk.Service.DraftSaveAsync(clerk.Context, Key("save"), Save("400.00", new DateOnly(2026, 9, 15)),
                    CancellationToken.None);
                Assert.Equal(CommandOutcome.Succeeded, saved.Outcome);
                receipt = saved.Resource!.Source.Id;
                var record = new RecordRequest(receipt, saved.Resource.Source.Version);
                Assert.Equal(CommandOutcome.Succeeded, (await clerk.Service.RecordAsync(clerk.Context, Key("record"), record,
                    CancellationToken.None)).Outcome);
                var replay = await clerk.Service.RecordAsync(clerk.Context, Key("record"), record, CancellationToken.None);
                Assert.True(replay.IsReplay);
                // A draft that is never recorded: no money event, so never a row.
                Assert.Equal(CommandOutcome.Succeeded, (await clerk.Service.DraftSaveAsync(clerk.Context, Key("draft-only"),
                    Save("333.00", new DateOnly(2026, 9, 16)), CancellationToken.None)).Outcome);
            }

            Guid reversal;
            await using (var maker = Reversals("maker"))
            {
                var proposed = await maker.Service.ProposeAsync(maker.Context, Key("propose"),
                    new ProposeReversalRequest(receipt, "2", new DateOnly(2026, 9, 20), "SENTETIK ters kayıt"),
                    CancellationToken.None);
                Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);
                await using var checker = Reversals("checker");
                var approved = await checker.Service.ApproveAsync(checker.Context, Key("approve"),
                    new ApproveReversalRequest(proposed.Resource!.Id, "1"), CancellationToken.None);
                Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
                reversal = approved.Resource!.ReversalSourceId!.Value;
            }

            var payment = Guid.NewGuid();
            Guid instrument;
            Guid collection;
            await using (var clerk = Instruments("clerk"))
            {
                var received = await clerk.Service.ReceiveAsync(clerk.Context, Key("receive"),
                    new CustomerInstrumentReceiveRequest(CustomerInstrumentKind.Cheque, Party, Account, "SENTETIK-EKSTRE-1",
                        "SENTETIK Keşideci", "SENTETIK Banka", null, new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 21),
                        new MoneyText("1200.00", Currency), null), CancellationToken.None);
                Assert.Equal(CommandOutcome.Succeeded, received.Outcome);
                instrument = received.Resource!.Id;
                var collected = await clerk.Service.CollectAsync(clerk.Context, Key("collect"),
                    new CustomerInstrumentCollectRequest(instrument, "1", Bank, new DateOnly(2026, 9, 25), null),
                    CancellationToken.None);
                Assert.Equal(CommandOutcome.Succeeded, collected.Outcome);
                collection = collected.Resource!.CollectionSourceId!.Value;
            }

            await using (var allocator = Allocations("allocator"))
            {
                var allocated = await allocator.Service.CreateAsync(allocator.Context, Key("allocate"),
                    new CreateFinanceAllocationRequest(Guid.NewGuid(), collection, "1", openItem, "1", "1000.00",
                        "SENTETIK ekstre kapama"), CancellationToken.None);
                Assert.Equal(CommandOutcome.Succeeded, allocated.Outcome);
            }

            await using (var db = Context(Company))
            {
                db.OutgoingMoneyEvents.Add(new OutgoingMoneyEventRow
                {
                    Id = Guid.NewGuid(), CompanyId = Company, PaymentId = payment, PartyId = Party, CompanyAccountId = Account,
                    TreasuryAccountId = Bank, CurrencyId = Currency, AmountText = "50.00", ProfileId = Profile.ToString("D"),
                    ProfileVersion = "1", OccurredOn = new DateOnly(2026, 9, 22), RecordedAtUtc = DateTimeOffset.UtcNow,
                    RecordedBy = "SENTETIK-payer", CorrelationId = "SENTETIK-payment",
                });
                db.MoneyEvents.Add(Event(Company, Guid.NewGuid(), MoneyEventKinds.ManualBankReceiptRecorded, Usd, "75.5",
                    new DateOnly(2026, 9, 12)));
                db.MoneyEvents.Add(Event(Company, Guid.NewGuid(), MoneyEventKinds.ManualBankReceiptRecorded, Currency, "888.00",
                    new DateOnly(2026, 9, 12), party: Guid.NewGuid(), account: Guid.NewGuid()));
                await db.SaveChangesAsync();
            }

            await using (var foreign = Context(OtherCompany))
            {
                foreign.MoneyEvents.Add(Event(OtherCompany, Guid.NewGuid(), MoneyEventKinds.ManualBankReceiptRecorded, Currency,
                    "999.00", new DateOnly(2026, 9, 12)));
                await foreign.SaveChangesAsync();
            }

            await using (var db = Context(Company))
            {
                // Replay, reversal and collection each stored exactly one money event.
                Assert.Equal(4, await db.MoneyEvents.CountAsync(row => row.CurrencyId == Currency));
                Assert.Single(await db.Allocations.ToListAsync());
            }

            return new Story(openItem, document, receipt, reversal, payment, collection, instrument);
        }

        public static MoneyEventRow Event(Guid company, Guid sourceId, int kind, Guid currency, string amount,
            DateOnly occurredOn, Guid? party = null, Guid? account = null) =>
            new()
            {
                Id = Guid.NewGuid(), CompanyId = company, SourceId = sourceId, Kind = kind, PartyId = party ?? Party,
                CompanyAccountId = account ?? Account, TreasuryAccountId = Bank, CurrencyId = currency, AmountText = amount,
                ProfileId = Profile.ToString("D"), ProfileVersion = "1", OccurredOn = occurredOn,
                RecordedAtUtc = DateTimeOffset.UtcNow, RecordedBy = "SENTETIK-seed", CorrelationId = "SENTETIK-seed",
            };

        public async ValueTask DisposeAsync()
        {
            await using var context = Context(Company);
            await context.Database.EnsureDeletedAsync();
        }

        private async Task<Guid> SeedOpenItemAsync(Guid document)
        {
            await using var context = Context(Company);
            var admitted = new DateTimeOffset(2026, 9, 10, 23, 30, 0, TimeSpan.Zero);
            var row = new FinanceOpenItemRow
            {
                Id = Guid.NewGuid(), CompanyId = Company, SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
                SourceId = document, SourceLineId = Guid.NewGuid(), SourceVersion = "1",
                SourceBatchId = Guid.NewGuid(), SourceBatchVersion = "synthetic-v1", SourceBatchChecksum = new string('f', 64),
                PartyId = Party, CompanyAccountId = Account, CurrencyId = Currency,
                OriginalAmountText = "1000.00", OutstandingAmountText = "1000.00", DueOn = new DateOnly(2026, 10, 10),
                Status = FinanceOpenItemStatuses.Open, Version = 1,
                CreatedBy = "synthetic", CreatedAtUtc = admitted, UpdatedBy = "synthetic", UpdatedAtUtc = admitted,
            };
            context.OpenItems.Add(row);
            await context.SaveChangesAsync();
            return row.Id;
        }

        private Scope<IManualReceiptService> Receipts(string actor)
        {
            var (context, companyContext, collector) = Tracked(Company);
            var service = new ReceiptDraftService(context, companyContext, new User(actor, Company), new AllowAll(),
                new BankReader(), new PartyPort(), new SettingsPort(), collector, new FinanceReceiptDraftBinding(Operation),
                Envelope(FinancePermissionNames.DraftSave), TimeProvider.System, Envelope(FinancePermissionNames.Record),
                new OpenPeriod());
            return new Scope<IManualReceiptService>(context, service, Finance(Company, actor));
        }

        private Scope<IReversalProposalService> Reversals(string actor)
        {
            var (context, companyContext, collector) = Tracked(Company);
            var user = new User(actor, Company);
            var service = new ReversalProposalService(context, companyContext, user, new AllowAll(), new OpenPeriod(),
                new FinanceAllocationReversalGate(context, companyContext, user), collector,
                new FinanceReversalEnvelopes(Envelope(FinancePermissionNames.ReversePropose),
                    Envelope(FinancePermissionNames.ReverseApprove)), TimeProvider.System);
            return new Scope<IReversalProposalService>(context, service, Finance(Company, actor));
        }

        private Scope<ICustomerInstrumentService> Instruments(string actor)
        {
            var (context, companyContext, collector) = Tracked(Company);
            var service = new CustomerInstrumentService(context, companyContext, new User(actor, Company), new AllowAll(),
                new BankReader(), new PartyPort(), new SettingsPort(), new OpenPeriod(), collector,
                new FinanceReceiptDraftBinding(Operation),
                new CustomerInstrumentAuditEnvelopes(Envelope(FinancePermissionNames.CustomerInstrumentReceive),
                    Envelope(FinancePermissionNames.CustomerInstrumentStatusChange),
                    Envelope(FinancePermissionNames.CustomerInstrumentCollect)),
                TimeProvider.System);
            return new Scope<ICustomerInstrumentService>(context, service, Finance(Company, actor));
        }

        private Scope<IFinanceAllocationService> Allocations(string actor)
        {
            var (context, companyContext, collector) = Tracked(Company);
            var service = new FinanceAllocationService(context, companyContext, new User(actor, Company), new AllowAll(),
                collector, Envelope(FinancePermissionNames.AllocationWrite), TimeProvider.System);
            return new Scope<IFinanceAllocationService>(context, service, Finance(Company, actor));
        }

        private static DraftSaveRequest Save(string amount, DateOnly occurredOn) =>
            new(null, null, new ReceiptPreparation(Party, Account, Bank, new MoneyText(amount, Currency),
                new ProfileReference(Profile.ToString("D"), "1"), occurredOn, null, "SENTETIK-EKSTRE", null));

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
            new(company, actor, $"ledger-{Guid.NewGuid():N}", null);

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

    private sealed class DenyAll : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Denied);
    }

    private sealed class UnavailableAll : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Unavailable);
    }

    private sealed class DenyAmount : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(action == FinanceAction.ReadPartyLedgerAmount
            ? PermissionDecision.Denied
            : PermissionDecision.Allowed);
    }

    /// <summary>Allows the first ledger read and denies every later measurement (a grant revoked mid-request).</summary>
    private sealed class RevokedAfterFirstRead : IFinancePermissionPort
    {
        private int reads;

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(
            action == FinanceAction.ReadPartyLedger && Interlocked.Increment(ref reads) == 1
                ? PermissionDecision.Allowed
                : PermissionDecision.Denied);
    }

    private sealed class BankReader : IFinanceBankAccountReader
    {
        private static BankAccountView View(Guid company) =>
            new(Bank, company, Currency, "SENTETIK ekstre bankası", true, true, "2");

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

    /// <summary>SENTETIK scale-2 profile for whatever company asks — a test input, not a production scale.</summary>
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
