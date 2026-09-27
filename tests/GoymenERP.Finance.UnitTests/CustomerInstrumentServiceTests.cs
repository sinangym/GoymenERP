using System.Text.Json;
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
/// DEC-FIN-016 first release through the real portfolio, receipt, allocation and reversal services, in memory: a
/// customer cheque is received (no money effect), reopened, deposited, collected ONCE (one recorded receipt source and
/// one money event at the face value), replayed and retried stale without a second credit, distributed by F-03 to the
/// customer's open item, and refused by FIN-T06 reversal. Company fence, audit channel and amount redaction included.
/// SENTETIK / NOT_ENV; not SQL or browser evidence (that is <c>FinanceCustomerInstrumentSqlServerRelationalAcceptanceTests</c>).
/// </summary>
public sealed class CustomerInstrumentServiceTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly Guid Currency = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Bank = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid OtherBank = Guid.Parse("34343434-3434-4343-8343-343434343434");
    private static readonly Guid Party = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Account = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Profile = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private const string Operation = "SENTETIK-operation";

    [Fact]
    public async Task A_cheque_is_received_reopened_deposited_and_collected_once_crediting_the_customer_at_face_value()
    {
        await using var run = new Run();

        CustomerInstrumentCommandResult received;
        await using (var clerk = run.Instruments("clerk", Company))
        {
            received = await clerk.Service.ReceiveAsync(clerk.Context, Key("receive"), Receive(), CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.Succeeded, received.Outcome);
        var instrument = received.Resource!;
        Assert.Equal((CustomerInstrumentStatus.InPortfolio, "1", "1000.00"),
            (instrument.Status, instrument.Version, instrument.Amount.Money!.Amount));
        Assert.Null(instrument.CollectionSourceId);

        // Receiving has no money effect: no source, no money event.
        await using (var db = run.Context(Company))
        {
            Assert.Empty(await db.ReceiptDrafts.ToListAsync());
            Assert.Empty(await db.MoneyEvents.ToListAsync());
        }

        // Close and reopen: another scope lists and reads exactly the stored instrument, with the next steps offered.
        await using (var reader = run.Instruments("reader", Company))
        {
            var page = await reader.Service.ListAuthorizedAsync(reader.Context, null, CancellationToken.None);
            Assert.Equal(instrument, Assert.Single(page.Resource!.Items));
            var detail = (await reader.Service.DetailAuthorizedAsync(reader.Context, instrument.Id, CancellationToken.None)).Resource!;
            Assert.Equal(instrument, detail.Instrument);
            var first = Assert.Single(detail.Movements);
            Assert.Equal((null, CustomerInstrumentStatus.InPortfolio, "1"), (first.FromStatus, first.ToStatus, first.Version));
            Assert.Equal(CustomerInstrumentRules.StateActions(CustomerInstrumentKind.Cheque, CustomerInstrumentStatus.InPortfolio),
                detail.AvailableActions);
        }

        // Another company sees nothing and cannot move it.
        await using (var foreign = run.Instruments("clerk", OtherCompany))
        {
            Assert.Equal(ReadOutcome.NotFound,
                (await foreign.Service.DetailAuthorizedAsync(foreign.Context, instrument.Id, CancellationToken.None)).Outcome);
            Assert.Empty((await foreign.Service.ListAuthorizedAsync(foreign.Context, null, CancellationToken.None)).Resource!.Items);
            var refused = await foreign.Service.CollectAsync(foreign.Context, Key("foreign-collect"),
                Collect(instrument.Id, "1"), CancellationToken.None);
            Assert.Equal((CommandOutcome.ValidationFailed, CustomerInstrumentRules.NotFound),
                (refused.Outcome, refused.Issues.Single().Code));
        }

        CustomerInstrumentCommandResult deposited;
        await using (var clerk = run.Instruments("clerk", Company))
        {
            deposited = await clerk.Service.ChangeStatusAsync(clerk.Context, Key("deposit"),
                Change(instrument.Id, "1", CustomerInstrumentTransition.DepositForCollection, Bank), CancellationToken.None);
        }
        Assert.Equal((CommandOutcome.Succeeded, CustomerInstrumentStatus.AtBankForCollection, "2", (Guid?)Bank),
            (deposited.Outcome, deposited.Resource!.Status, deposited.Resource.Version, deposited.Resource.TreasuryAccountId));

        // Another bank than the one it was given to cannot collect it.
        await using (var clerk = run.Instruments("clerk", Company))
        {
            var mismatch = await clerk.Service.CollectAsync(clerk.Context, Key("collect-other-bank"),
                Collect(instrument.Id, "2", OtherBank), CancellationToken.None);
            Assert.Equal((CommandOutcome.ValidationFailed, CustomerInstrumentRules.BankMismatch),
                (mismatch.Outcome, mismatch.Issues.Single().Code));
        }

        var collect = Collect(instrument.Id, "2");
        CustomerInstrumentCommandResult collected;
        await using (var collector = run.Instruments("collector", Company))
        {
            collected = await collector.Service.CollectAsync(collector.Context, Key("collect"), collect, CancellationToken.None);
        }
        Assert.Equal(CommandOutcome.Succeeded, collected.Outcome);
        Assert.Equal((CustomerInstrumentStatus.Collected, "3"), (collected.Resource!.Status, collected.Resource.Version));
        var sourceId = collected.Resource.CollectionSourceId!.Value;

        // The same key and request replays; a fresh key at the old version is stale; neither credits again.
        await using (var collector = run.Instruments("collector", Company))
        {
            var replay = await collector.Service.CollectAsync(collector.Context, Key("collect"), collect, CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, true, collected.OriginalCorrelationId, (Guid?)sourceId),
                (replay.Outcome, replay.IsReplay, replay.OriginalCorrelationId, replay.Resource!.CollectionSourceId));
            var stale = await collector.Service.CollectAsync(collector.Context, Key("collect-stale"), collect, CancellationToken.None);
            Assert.Equal(CommandOutcome.VersionConflict, stale.Outcome);
            var again = await collector.Service.CollectAsync(collector.Context, Key("collect-again"),
                Collect(instrument.Id, "3"), CancellationToken.None);
            Assert.Equal((CommandOutcome.ValidationFailed, CustomerInstrumentRules.TransitionNotAllowed),
                (again.Outcome, again.Issues.Single().Code));
            var otherActor = await run.Instruments("clerk", Company).UseAsync(scope =>
                scope.Service.CollectAsync(scope.Context, Key("collect"), collect, CancellationToken.None));
            Assert.Equal(CommandOutcome.IdempotencyConflict, otherActor.Outcome);
            Assert.Null(otherActor.Resource);
        }

        await using (var db = run.Context(Company))
        {
            var moneyEvent = Assert.Single(await db.MoneyEvents.ToListAsync());
            Assert.Equal((sourceId, MoneyEventKinds.CustomerInstrumentCollected, "1000.00", Party, Account, Bank),
                (moneyEvent.SourceId, moneyEvent.Kind, moneyEvent.AmountText, moneyEvent.PartyId, moneyEvent.CompanyAccountId,
                    moneyEvent.TreasuryAccountId));
            Assert.Equal(new DateOnly(2026, 10, 16), moneyEvent.OccurredOn);
            var source = Assert.Single(await db.ReceiptDrafts.ToListAsync());
            Assert.Equal((sourceId, ReceiptStages.Recorded, "1000.00", "SENTETIK-CEK-0001"),
                (source.Id, source.Stage, source.AmountText, source.BankReference));
            Assert.Null(source.PosCommissionBearer);
            Assert.Equal(3, await db.CustomerInstrumentMovements.CountAsync());

            var audits = await db.Set<AuditEvent>().ToListAsync();
            Assert.All(audits, audit => Assert.Equal(SourceChannels.Web, audit.SourceChannel));
            Assert.Contains(audits, audit => audit.EventType == "finance.customer-instrument.received.v1");
            Assert.Contains(audits, audit => audit.EventType == "finance.customer-instrument.status-changed.v1");
            var collectedAudit = Assert.Single(audits, audit => audit.EventType == "finance.customer-instrument.collected.v1");
            Assert.Equal(FinancePermissionNames.CustomerInstrumentCollect, collectedAudit.PermissionCode);
            var receiptAudit = Assert.Single(audits, audit => audit.EventType == "finance.manual-receipt.recorded.v1");
            Assert.Equal(sourceId.ToString("D"), receiptAudit.EntityId);
            Assert.All(audits, audit => Assert.DoesNotContain("1000", audit.AfterJson + audit.BeforeJson, StringComparison.Ordinal));
        }

        // The collection is an ordinary recorded receipt to F-02 and F-03.
        await using (var reader = run.Receipts("reader"))
        {
            var detail = (await reader.Service.DetailAuthorizedAsync(reader.Context, new SourceDetailRequest(sourceId),
                CancellationToken.None)).Resource!;
            Assert.Equal((SourceStage.Recorded, "1000.00"), (detail.Source.Stage, detail.Unallocated.Amount.Money!.Amount));
        }

        var openItem = await run.SeedOpenItemAsync("1000.00");
        await using (var allocator = run.Allocations("allocator"))
        {
            var allocated = await allocator.Service.CreateAsync(allocator.Context, Key("allocate"),
                new CreateFinanceAllocationRequest(Guid.NewGuid(), sourceId, "1", openItem, "1", "1000.00",
                    "SENTETIK çek tahsili kapama"), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, "1000.00"), (allocated.Outcome, allocated.Resource!.Amount));
        }

        await using (var db = run.Context(Company))
        {
            var item = await db.OpenItems.SingleAsync(row => row.Id == openItem);
            Assert.Equal((FinanceOpenItemStatuses.Closed, "0.00"), (item.Status, item.OutstandingAmountText));
            Assert.Single(await db.MoneyEvents.ToListAsync());
        }

        // The reverse flow after collection is not decided: FIN-T06 refuses the collection source, and writes nothing.
        await using (var maker = run.Reversals("maker"))
        {
            var proposal = await maker.Service.ProposeAsync(maker.Context, Key("reverse"),
                new ProposeReversalRequest(sourceId, "1", new DateOnly(2026, 10, 17), "SENTETIK ters kayıt"),
                CancellationToken.None);
            Assert.Equal((CommandOutcome.ValidationFailed, "finance_reversal_source_not_original_receipt"),
                (proposal.Outcome, proposal.Issues.Single().Code));
        }

        await using (var reader = run.Instruments("reader", Company))
        {
            var detail = (await reader.Service.DetailAuthorizedAsync(reader.Context, instrument.Id, CancellationToken.None)).Resource!;
            Assert.Equal((CustomerInstrumentStatus.Collected, (Guid?)sourceId), (detail.Instrument.Status, detail.Instrument.CollectionSourceId));
            Assert.Empty(detail.AvailableActions);
            Assert.Equal([null, CustomerInstrumentStatus.InPortfolio, CustomerInstrumentStatus.AtBankForCollection],
                detail.Movements.Select(movement => movement.FromStatus));
        }
    }

    [Fact]
    public async Task A_note_is_protested_then_returned_without_any_money_effect_and_a_cheque_bounces()
    {
        await using var run = new Run();
        var note = await run.ReceiveAsync(Receive() with
        {
            Kind = CustomerInstrumentKind.PromissoryNote, BankName = null, BranchName = null, SerialNumber = "SENTETIK-SNT-1",
        });
        await using (var clerk = run.Instruments("clerk", Company))
        {
            var protested = await clerk.Service.ChangeStatusAsync(clerk.Context, Key("protest"),
                Change(note.Id, "1", CustomerInstrumentTransition.Dishonour, null, "SENTETIK protesto"), CancellationToken.None);
            Assert.Equal(CustomerInstrumentStatus.Protested, protested.Resource!.Status);
            var cannotCollect = await clerk.Service.CollectAsync(clerk.Context, Key("collect-protested"),
                Collect(note.Id, "2"), CancellationToken.None);
            Assert.Equal(CustomerInstrumentRules.TransitionNotAllowed, cannotCollect.Issues.Single().Code);
            var returned = await clerk.Service.ChangeStatusAsync(clerk.Context, Key("return"),
                Change(note.Id, "2", CustomerInstrumentTransition.ReturnToCustomer, null), CancellationToken.None);
            Assert.Equal((CustomerInstrumentStatus.ReturnedToCustomer, "3"), (returned.Resource!.Status, returned.Resource.Version));
        }

        var cheque = await run.ReceiveAsync(Receive() with { SerialNumber = "SENTETIK-CEK-0002" });
        await using (var clerk = run.Instruments("clerk", Company))
        {
            var bounced = await clerk.Service.ChangeStatusAsync(clerk.Context, Key("bounce"),
                Change(cheque.Id, "1", CustomerInstrumentTransition.Dishonour, null), CancellationToken.None);
            Assert.Equal(CustomerInstrumentStatus.Bounced, bounced.Resource!.Status);
        }

        await using var db = run.Context(Company);
        Assert.Empty(await db.ReceiptDrafts.ToListAsync());
        Assert.Empty(await db.MoneyEvents.ToListAsync());
    }

    [Fact]
    public async Task Refusals_store_nothing_and_a_stale_status_change_is_the_keys_answer()
    {
        await using var run = new Run();
        var cheque = await run.ReceiveAsync(Receive());
        await using (var clerk = run.Instruments("clerk", Company))
        {
            var duplicate = await clerk.Service.ReceiveAsync(clerk.Context, Key("receive-duplicate"), Receive(),
                CancellationToken.None);
            Assert.Equal((CommandOutcome.ValidationFailed, CustomerInstrumentRules.Duplicate),
                (duplicate.Outcome, duplicate.Issues.Single().Code));
            var withdraw = await clerk.Service.ChangeStatusAsync(clerk.Context, Key("withdraw"),
                Change(cheque.Id, "1", CustomerInstrumentTransition.WithdrawFromBank, null), CancellationToken.None);
            Assert.Equal(CustomerInstrumentRules.TransitionNotAllowed, withdraw.Issues.Single().Code);
            var noBank = await clerk.Service.ChangeStatusAsync(clerk.Context, Key("deposit-no-bank"),
                Change(cheque.Id, "1", CustomerInstrumentTransition.DepositForCollection, null), CancellationToken.None);
            Assert.Equal(CustomerInstrumentRules.TreasuryRequired, noBank.Issues.Single().Code);
            var bankOnReturn = await clerk.Service.ChangeStatusAsync(clerk.Context, Key("return-bank"),
                Change(cheque.Id, "1", CustomerInstrumentTransition.ReturnToCustomer, Bank), CancellationToken.None);
            Assert.Equal(CustomerInstrumentRules.TreasuryNotApplicable, bankOnReturn.Issues.Single().Code);
            var early = await clerk.Service.ChangeStatusAsync(clerk.Context, Key("early"),
                Change(cheque.Id, "1", CustomerInstrumentTransition.Dishonour, null) with { OccurredOn = new DateOnly(2026, 9, 1) },
                CancellationToken.None);
            Assert.Equal(CustomerInstrumentRules.DateBeforePrevious, early.Issues.Single().Code);
            var ineligibleBank = await clerk.Service.ChangeStatusAsync(clerk.Context, Key("deposit-unknown-bank"),
                Change(cheque.Id, "1", CustomerInstrumentTransition.DepositForCollection, Guid.NewGuid()), CancellationToken.None);
            Assert.Equal("bank_account_ineligible", ineligibleBank.Issues.Single().Code);

            var stale = Change(cheque.Id, "7", CustomerInstrumentTransition.Dishonour, null);
            var first = await clerk.Service.ChangeStatusAsync(clerk.Context, Key("stale"), stale, CancellationToken.None);
            Assert.Equal(CommandOutcome.VersionConflict, first.Outcome);
            var replayed = await clerk.Service.ChangeStatusAsync(clerk.Context, Key("stale"), stale, CancellationToken.None);
            Assert.Equal((CommandOutcome.VersionConflict, true), (replayed.Outcome, replayed.IsReplay));
        }

        await using (var db = run.Context(Company))
        {
            var row = await db.CustomerInstruments.SingleAsync();
            Assert.Equal(((int)CustomerInstrumentStatus.InPortfolio, 1L), (row.Status, row.Version));
            Assert.Single(await db.CustomerInstrumentMovements.ToListAsync());
        }

        // A closed period refuses the collection before any write (K1: the collection date decides).
        await using (var collector = run.Instruments("collector", Company, period: new ClosedPeriod()))
        {
            var closed = await collector.Service.CollectAsync(collector.Context, Key("collect-closed"),
                Collect(cheque.Id, "1"), CancellationToken.None);
            Assert.Equal((CommandOutcome.ValidationFailed, "collectedOn", RecordPeriodAnswer.PeriodNotOpenCode),
                (closed.Outcome, closed.Issues.Single().Field, closed.Issues.Single().Code));
        }

        await using (var db = run.Context(Company))
        {
            Assert.Empty(await db.MoneyEvents.ToListAsync());
            Assert.Empty(await db.ReceiptDrafts.ToListAsync());
        }
    }

    /// <summary>
    /// #497 P2-1's lesson: an actor without the amount grant gets NO amount from the receive, its replay, the
    /// collection, the list or the detail. Checked by parsing the JSON — every money member is null and every amount
    /// view is Redacted — not by searching for a substring.
    /// </summary>
    [Fact]
    public async Task Without_the_amount_grant_no_answer_carries_an_amount()
    {
        await using var run = new Run();
        var answers = new List<object>();
        await using (var clerk = run.Instruments("clerk", Company, new DenyAmount()))
        {
            var received = await clerk.Service.ReceiveAsync(clerk.Context, Key("receive"), Receive(), CancellationToken.None);
            answers.Add(received);
            answers.Add(await clerk.Service.ReceiveAsync(clerk.Context, Key("receive"), Receive(), CancellationToken.None));
            var id = received.Resource!.Id;
            answers.Add(await clerk.Service.CollectAsync(clerk.Context, Key("collect"), Collect(id, "1"), CancellationToken.None));
            answers.Add(await clerk.Service.CollectAsync(clerk.Context, Key("collect"), Collect(id, "1"), CancellationToken.None));
            answers.Add((await clerk.Service.ListAuthorizedAsync(clerk.Context, null, CancellationToken.None)).Resource!);
            answers.Add((await clerk.Service.DetailAuthorizedAsync(clerk.Context, id, CancellationToken.None)).Resource!);
        }

        foreach (var answer in answers)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(answer, answer.GetType(), FinanceJson.CreateOptions()));
            var amounts = AmountViews(document.RootElement).ToList();
            Assert.NotEmpty(amounts);
            Assert.All(amounts, view =>
            {
                Assert.Equal("Redacted", view.GetProperty("disclosure").GetString());
                Assert.Equal(JsonValueKind.Null, view.GetProperty("money").ValueKind);
            });
        }

        // The stored facts themselves are whole.
        await using var db = run.Context(Company);
        Assert.Equal("1000.00", (await db.CustomerInstruments.SingleAsync()).AmountText);
        Assert.Equal("1000.00", (await db.MoneyEvents.SingleAsync()).AmountText);
    }

    [Fact]
    public async Task Without_a_grant_nothing_is_read_or_written_and_an_unregistered_name_is_unavailable()
    {
        await using var run = new Run();
        await using (var denied = run.Instruments("clerk", Company, new DenyAll()))
        {
            Assert.Equal(CommandOutcome.Denied,
                (await denied.Service.ReceiveAsync(denied.Context, Key("r"), Receive(), CancellationToken.None)).Outcome);
            Assert.Equal(ReadOutcome.Denied,
                (await denied.Service.ListAuthorizedAsync(denied.Context, null, CancellationToken.None)).Outcome);
        }

        await using (var unavailable = run.Instruments("clerk", Company, new UnavailableAll()))
        {
            var answer = await unavailable.Service.ReceiveAsync(unavailable.Context, Key("r"), Receive(), CancellationToken.None);
            Assert.Equal((CommandOutcome.Unavailable, "finance_permission_mapping_unavailable"),
                (answer.Outcome, answer.Issues.Single().Code));
            Assert.Equal(ReadOutcome.Unavailable,
                (await unavailable.Service.DetailAuthorizedAsync(unavailable.Context, Guid.NewGuid(), CancellationToken.None)).Outcome);
        }

        // A reader who may not change or collect is offered no next step.
        var cheque = await run.ReceiveAsync(Receive());
        await using (var readOnly = run.Instruments("reader", Company, new ReadOnly()))
        {
            var detail = (await readOnly.Service.DetailAuthorizedAsync(readOnly.Context, cheque.Id, CancellationToken.None)).Resource!;
            Assert.Empty(detail.AvailableActions);
        }

        await using var db = run.Context(Company);
        Assert.Single(await db.CustomerInstruments.ToListAsync());
    }

    /// <summary>
    /// User decision 2026-09-27: the customer's debt closes at collection, and the risk is tracked separately. The view
    /// moves with the instrument: received → open exposure (not yet due), dishonoured → still open exposure, collected →
    /// out of the exposure; one customer's scope, the company total, a half-named scope and another company.
    /// </summary>
    [Fact]
    public async Task The_risk_view_follows_receipt_dishonour_and_collection_and_stays_in_its_company()
    {
        await using var run = new Run();
        // After every step below: receipts 2026-09-20, dishonour 2026-09-25, collection 2026-10-16.
        var asOf = new DateOnly(2026, 12, 1);
        var customer = new CustomerInstrumentRiskRequest(asOf, Party, Account);
        var beforeDue = customer with { AsOf = new DateOnly(2026, 10, 1) };

        async Task<CustomerInstrumentRiskView> RiskAsync(CustomerInstrumentRiskRequest request, Guid? company = null)
        {
            await using var reader = run.Instruments("reader", company ?? Company);
            var read = await reader.Service.RiskAuthorizedAsync(reader.Context, request, CancellationToken.None);
            Assert.Equal(ReadOutcome.Found, read.Outcome);
            return read.Resource!;
        }

        var empty = await RiskAsync(customer);
        Assert.Empty(empty.Customers);
        Assert.Empty(empty.Totals);

        var first = await run.ReceiveAsync(Receive());
        var second = await run.ReceiveAsync(Receive() with { SerialNumber = "SENTETIK-CEK-0002", Money = new MoneyText("700.00", Currency) });
        var received = (await RiskAsync(beforeDue)).Customers.Single().Figures;
        Assert.Equal((2, "1700.00"), (received.InPortfolioNotDue.Count, received.InPortfolioNotDue.Amount.Money!.Amount));
        Assert.Equal((2, "1700.00"), (received.OpenExposure.Count, received.OpenExposure.Amount.Money!.Amount));

        await using (var clerk = run.Instruments("clerk", Company))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await clerk.Service.ChangeStatusAsync(clerk.Context, Key("bounce"),
                Change(second.Id, "1", CustomerInstrumentTransition.Dishonour, null), CancellationToken.None)).Outcome);
            Assert.Equal(CommandOutcome.Succeeded, (await clerk.Service.CollectAsync(clerk.Context, Key("collect"),
                Collect(first.Id, "1"), CancellationToken.None)).Outcome);
        }

        var view = await RiskAsync(customer);
        var after = Assert.Single(view.Customers).Figures;
        // P3-1: an empty bucket is a zero at the currency scale.
        Assert.Equal((0, "0.00"), (after.InPortfolioNotDue.Count, after.InPortfolioNotDue.Amount.Money!.Amount));
        Assert.Equal((1, "700.00"), (after.Dishonoured.Count, after.Dishonoured.Amount.Money!.Amount));
        Assert.Equal((1, "1000.00"), (after.Collected.Count, after.Collected.Amount.Money!.Amount));
        Assert.Equal((1, "700.00"), (after.OpenExposure.Count, after.OpenExposure.Amount.Money!.Amount));
        Assert.Equal(after, Assert.Single(view.Totals).Figures);

        // A later date after the last step changes nothing.
        Assert.Equal(after, (await RiskAsync(customer with { AsOf = new DateOnly(2027, 1, 1) })).Customers.Single().Figures);

        // POINT IN TIME (K3 5332370492 P2-1): the picture ON the day, from the dated history.
        Assert.Empty((await RiskAsync(customer with { AsOf = new DateOnly(2026, 9, 19) })).Customers);
        var onReceipt = (await RiskAsync(customer with { AsOf = new DateOnly(2026, 9, 22) })).Customers.Single().Figures;
        Assert.Equal((2, "1700.00"), (onReceipt.InPortfolioNotDue.Count, onReceipt.InPortfolioNotDue.Amount.Money!.Amount));
        Assert.Equal((0, "0.00"), (onReceipt.Dishonoured.Count, onReceipt.Dishonoured.Amount.Money!.Amount));
        // Collected on 2026-10-16: on 2026-10-01 it was still in the portfolio, not yet due — and open exposure.
        var beforeCollection = (await RiskAsync(beforeDue)).Customers.Single().Figures;
        Assert.Equal((1, "1000.00"), (beforeCollection.InPortfolioNotDue.Count,
            beforeCollection.InPortfolioNotDue.Amount.Money!.Amount));
        Assert.Equal((1, "700.00"), (beforeCollection.Dishonoured.Count, beforeCollection.Dishonoured.Amount.Money!.Amount));
        Assert.Equal((0, "0.00"), (beforeCollection.Collected.Count, beforeCollection.Collected.Amount.Money!.Amount));
        Assert.Equal((2, "1700.00"), (beforeCollection.OpenExposure.Count,
            beforeCollection.OpenExposure.Amount.Money!.Amount));

        // Company total (no scope) = this company's instruments only; another company sees none.
        Assert.Equal(after, Assert.Single((await RiskAsync(new CustomerInstrumentRiskRequest(asOf, null, null))).Totals).Figures);
        Assert.Empty((await RiskAsync(new CustomerInstrumentRiskRequest(asOf, null, null), OtherCompany)).Customers);

        await using (var reader = run.Instruments("reader", Company))
        {
            Assert.Equal(ReadOutcome.NotFound, (await reader.Service.RiskAuthorizedAsync(reader.Context,
                new CustomerInstrumentRiskRequest(asOf, Party, null), CancellationToken.None)).Outcome);
        }

        await using (var denied = run.Instruments("reader", Company, new DenyAll()))
        {
            Assert.Equal(ReadOutcome.Denied, (await denied.Service.RiskAuthorizedAsync(denied.Context, customer,
                CancellationToken.None)).Outcome);
        }

        // Without the amount grant: counts only; every amount view is Redacted with no money (parsed JSON).
        await using (var blind = run.Instruments("reader", Company, new DenyAmount()))
        {
            var redacted = (await blind.Service.RiskAuthorizedAsync(blind.Context, new CustomerInstrumentRiskRequest(asOf, null, null),
                CancellationToken.None)).Resource!;
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(redacted, FinanceJson.CreateOptions()));
            var views = AmountViews(document.RootElement).ToList();
            Assert.Equal(14, views.Count);
            Assert.All(views, amountView =>
            {
                Assert.Equal("Redacted", amountView.GetProperty("disclosure").GetString());
                Assert.Equal(JsonValueKind.Null, amountView.GetProperty("money").ValueKind);
            });
            Assert.Equal(1, redacted.Customers.Single().Figures.OpenExposure.Count);
        }

        // Reading the risk has no money effect.
        await using var db = run.Context(Company);
        Assert.Single(await db.MoneyEvents.ToListAsync());
    }

    /// <summary>
    /// K3 5332370492 P2-1, with a past date: an instrument received after the day is not counted; one collected,
    /// dishonoured or returned after the day is still in the portfolio (open exposure) on that day; and the day's own
    /// due split holds (due before the day = overdue, due on it = not yet due).
    /// </summary>
    [Fact]
    public async Task The_risk_view_is_the_picture_on_a_past_day()
    {
        await using var run = new Run();
        var dueEarly = await run.ReceiveAsync(Receive() with
        {
            SerialNumber = "SENTETIK-PIT-1", DueOn = new DateOnly(2026, 9, 24), Money = new MoneyText("100.00", Currency),
        });
        var dueOnDay = await run.ReceiveAsync(Receive() with
        {
            SerialNumber = "SENTETIK-PIT-2", DueOn = new DateOnly(2026, 9, 26), Money = new MoneyText("200.00", Currency),
        });
        var returnedLater = await run.ReceiveAsync(Receive() with
        {
            SerialNumber = "SENTETIK-PIT-3", DueOn = new DateOnly(2026, 11, 1), Money = new MoneyText("300.00", Currency),
        });
        await run.ReceiveAsync(Receive() with
        {
            SerialNumber = "SENTETIK-PIT-4", ReceivedOn = new DateOnly(2026, 9, 27), Money = new MoneyText("400.00", Currency),
        });
        await using (var clerk = run.Instruments("clerk", Company))
        {
            var day = new DateOnly(2026, 9, 28);
            Assert.Equal(CommandOutcome.Succeeded, (await clerk.Service.ChangeStatusAsync(clerk.Context, Key("pit-bounce"),
                Change(dueEarly.Id, "1", CustomerInstrumentTransition.Dishonour, null) with { OccurredOn = day },
                CancellationToken.None)).Outcome);
            Assert.Equal(CommandOutcome.Succeeded, (await clerk.Service.CollectAsync(clerk.Context, Key("pit-collect"),
                Collect(dueOnDay.Id, "1") with { CollectedOn = day }, CancellationToken.None)).Outcome);
            Assert.Equal(CommandOutcome.Succeeded, (await clerk.Service.ChangeStatusAsync(clerk.Context, Key("pit-return"),
                Change(returnedLater.Id, "1", CustomerInstrumentTransition.ReturnToCustomer, null) with { OccurredOn = day },
                CancellationToken.None)).Outcome);
        }

        await using var reader = run.Instruments("reader", Company);
        var past = (await reader.Service.RiskAuthorizedAsync(reader.Context,
            new CustomerInstrumentRiskRequest(new DateOnly(2026, 9, 26), null, null), CancellationToken.None)).Resource!;
        var figures = Assert.Single(past.Totals).Figures;
        // Received on 2026-09-27: not there yet. Dishonoured, collected and returned on 2026-09-28: still held.
        Assert.Equal((1, "100.00"), (figures.InPortfolioOverdue.Count, figures.InPortfolioOverdue.Amount.Money!.Amount));
        Assert.Equal((2, "500.00"), (figures.InPortfolioNotDue.Count, figures.InPortfolioNotDue.Amount.Money!.Amount));
        Assert.Equal((0, "0.00"), (figures.Dishonoured.Count, figures.Dishonoured.Amount.Money!.Amount));
        Assert.Equal((0, "0.00"), (figures.Collected.Count, figures.Collected.Amount.Money!.Amount));
        Assert.Equal((0, "0.00"), (figures.ReturnedToCustomer.Count, figures.ReturnedToCustomer.Amount.Money!.Amount));
        Assert.Equal((3, "600.00"), (figures.OpenExposure.Count, figures.OpenExposure.Amount.Money!.Amount));

        var now = Assert.Single((await reader.Service.RiskAuthorizedAsync(reader.Context,
            new CustomerInstrumentRiskRequest(new DateOnly(2026, 9, 28), null, null), CancellationToken.None)).Resource!.Totals)
            .Figures;
        Assert.Equal((1, "100.00"), (now.Dishonoured.Count, now.Dishonoured.Amount.Money!.Amount));
        Assert.Equal((1, "200.00"), (now.Collected.Count, now.Collected.Amount.Money!.Amount));
        Assert.Equal((1, "300.00"), (now.ReturnedToCustomer.Count, now.ReturnedToCustomer.Amount.Money!.Amount));
        Assert.Equal((1, "400.00"), (now.InPortfolioNotDue.Count, now.InPortfolioNotDue.Amount.Money!.Amount));
        Assert.Equal((2, "500.00"), (now.OpenExposure.Count, now.OpenExposure.Amount.Money!.Amount));
    }

    private static IEnumerable<JsonElement> AmountViews(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("disclosure", out _) && element.TryGetProperty("money", out _))
            {
                yield return element;
            }

            foreach (var property in element.EnumerateObject())
            {
                foreach (var nested in AmountViews(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in AmountViews(item))
                {
                    yield return nested;
                }
            }
        }
    }

    private static CommandMetadata Key(string key) => new(key);

    private static CustomerInstrumentReceiveRequest Receive() => new(
        CustomerInstrumentKind.Cheque, Party, Account, "SENTETIK-CEK-0001", "SENTETIK Keşideci A.Ş.", "SENTETIK Banka",
        "SENTETIK Şube", new DateOnly(2026, 10, 15), new DateOnly(2026, 9, 20), new MoneyText("1000.00", Currency),
        "SENTETIK müşteri çeki");

    private static CustomerInstrumentStatusChangeRequest Change(Guid id, string version,
        CustomerInstrumentTransition transition, Guid? bank, string? note = null) =>
        new(id, version, transition, new DateOnly(2026, 9, 25), bank, note);

    private static CustomerInstrumentCollectRequest Collect(Guid id, string version, Guid? bank = null) =>
        new(id, version, bank ?? Bank, new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 16));

    private sealed class Run : IAsyncDisposable
    {
        private readonly string databaseName = $"finance-instruments-{Guid.NewGuid():N}";

        public FinanceDbContext Context(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName).Options, companyContext);
        }

        public Scope<ICustomerInstrumentService> Instruments(string actor, Guid company,
            IFinancePermissionPort? permissions = null, IFinanceRecordPeriodRule? period = null)
        {
            var (context, companyContext, collector) = Tracked(company);
            var service = new CustomerInstrumentService(context, companyContext, new User(actor, company),
                permissions ?? new AllowAll(), new BankReader(), new PartyPort(), new SettingsPort(),
                period ?? new OpenPeriod(), collector, new FinanceReceiptDraftBinding(Operation),
                new CustomerInstrumentAuditEnvelopes(Envelope(FinancePermissionNames.CustomerInstrumentReceive),
                    Envelope(FinancePermissionNames.CustomerInstrumentStatusChange),
                    Envelope(FinancePermissionNames.CustomerInstrumentCollect)),
                TimeProvider.System);
            return new Scope<ICustomerInstrumentService>(context, service, Finance(company, actor));
        }

        public async Task<CustomerInstrumentView> ReceiveAsync(CustomerInstrumentReceiveRequest request)
        {
            await using var clerk = Instruments("clerk", Company);
            var result = await clerk.Service.ReceiveAsync(clerk.Context, Key($"receive-{Guid.NewGuid():N}"), request,
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            return result.Resource!;
        }

        public Scope<IManualReceiptService> Receipts(string actor)
        {
            var (context, companyContext, collector) = Tracked(Company);
            var service = new ReceiptDraftService(context, companyContext, new User(actor, Company), new AllowAll(),
                new BankReader(), new PartyPort(), new SettingsPort(), collector, new FinanceReceiptDraftBinding(Operation),
                Envelope(FinancePermissionNames.DraftSave), TimeProvider.System, Envelope(FinancePermissionNames.Record),
                new OpenPeriod());
            return new Scope<IManualReceiptService>(context, service, Finance(Company, actor));
        }

        public Scope<IFinanceAllocationService> Allocations(string actor)
        {
            var (context, companyContext, collector) = Tracked(Company);
            var service = new FinanceAllocationService(context, companyContext, new User(actor, Company), new AllowAll(),
                collector, Envelope(FinancePermissionNames.AllocationWrite), TimeProvider.System);
            return new Scope<IFinanceAllocationService>(context, service, Finance(Company, actor));
        }

        public Scope<IReversalProposalService> Reversals(string actor)
        {
            var (context, companyContext, collector) = Tracked(Company);
            var user = new User(actor, Company);
            var service = new ReversalProposalService(context, companyContext, user, new AllowAll(), new OpenPeriod(),
                new FinanceAllocationReversalGate(context, companyContext, user), collector,
                new FinanceReversalEnvelopes(Envelope(FinancePermissionNames.ReversePropose),
                    Envelope(FinancePermissionNames.ReverseApprove)), TimeProvider.System);
            return new Scope<IReversalProposalService>(context, service, Finance(Company, actor));
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
            new(company, actor, $"instrument-{Guid.NewGuid():N}", null);

        private static FinanceAuditEnvelope Envelope(string permission) =>
            new(permission, ["SENTETIK-FINANCE"], SourceChannels.Web);
    }

    private sealed class Scope<T>(FinanceDbContext db, T service, FinanceContext context) : IAsyncDisposable
    {
        public T Service => service;
        public FinanceContext Context => context;

        public async Task<TResult> UseAsync<TResult>(Func<Scope<T>, Task<TResult>> use)
        {
            await using (this)
            {
                return await use(this);
            }
        }

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
            CancellationToken cancellationToken) => Task.FromResult(action == FinanceAction.ReadCustomerInstrumentAmount
            ? PermissionDecision.Denied
            : PermissionDecision.Allowed);
    }

    private sealed class ReadOnly : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(
            action is FinanceAction.ReadCustomerInstruments or FinanceAction.ReadCustomerInstrumentAmount
                ? PermissionDecision.Allowed
                : PermissionDecision.Denied);
    }

    private sealed class BankReader : IFinanceBankAccountReader
    {
        private static BankAccountView View(Guid company, Guid id) =>
            new(id, company, Currency, "SENTETIK çek bankası", true, true, "2");

        public Task<ReadResult<BankAccountPage>> ListAuthorizedAsync(FinanceContext context,
            CancellationToken cancellationToken) => Task.FromResult(new ReadResult<BankAccountPage>(ReadOutcome.Found,
            new BankAccountPage(context.CompanyId, [View(context.CompanyId, Bank), View(context.CompanyId, OtherBank)])));

        public Task<ReadResult<BankAccountView>> GetAuthorizedAsync(FinanceContext context, Guid accountId,
            CancellationToken cancellationToken) => Task.FromResult(accountId == Bank || accountId == OtherBank
            ? new ReadResult<BankAccountView>(ReadOutcome.Found, View(context.CompanyId, accountId))
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

    /// <summary>A TEST INPUT that answers "closed" for any date.</summary>
    private sealed class ClosedPeriod : IFinanceRecordPeriodRule
    {
        public Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
            DateTimeOffset recordedAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(new RecordPeriodAnswer(RecordPeriodOutcome.NotOpen, RecordPeriodAnswer.PeriodNotOpenCode));
    }

    private sealed class User(string actor, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
