using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceSalesInvoiceSettlementTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Invoice = Guid.NewGuid();
    private static readonly Guid Batch = Guid.NewGuid();
    private static readonly Guid Party = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly Guid Currency = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    [Fact]
    public void Every_installment_is_required_and_shared_invoice_scope_is_preserved()
    {
        var proof = Proof("0", "10");
        var open = Project(proof);
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Open, open.Status);
        Assert.Equal(Batch, open.ScopeId);
        Assert.Equal("invoice:v4", open.SourceVersion);
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Settled, Project(Proof("0", "0")).Status);
        // No order ID or order-specific portion is accepted by the owner port.
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Unknown,
            Project(proof with { Items = [proof.Items[0]] }).Status);
    }

    [Theory]
    [InlineData("missing-admission")]
    [InlineData("cancelled")]
    [InlineData("cancellation-receipt")]
    [InlineData("missing-event")]
    [InlineData("unrecorded")]
    [InlineData("released")]
    [InlineData("reversed")]
    [InlineData("wrong-company")]
    [InlineData("wrong-version")]
    [InlineData("projection-only")]
    [InlineData("bad-receipt")]
    [InlineData("unknown-kind")]
    public void Ambiguous_or_invalid_owner_evidence_never_settles(string fault)
    {
        var proof = Proof("0");
        switch (fault)
        {
            case "missing-admission": proof = proof with { Admissions = [] }; break;
            case "cancelled": proof.Items[0].Status = FinanceOpenItemStatuses.Cancelled; break;
            case "cancellation-receipt": proof = proof with { Cancellations = [new() { InvoiceId = Invoice }] }; break;
            case "missing-event": proof = proof with { Events = [] }; break;
            case "unrecorded": proof.Sources[0].Stage = ReceiptStages.Draft; break;
            case "released": proof.Allocations[0].Status = FinanceAllocationStatuses.Released; break;
            case "reversed": proof = proof with { Reversals = [new() { OriginalSourceId = proof.Sources[0].Id,
                CompanyId = Company, Status = ReversalProposalStatuses.Approved }] }; break;
            case "wrong-company": proof.Items[0].CompanyId = Guid.NewGuid(); break;
            case "wrong-version": proof.Items[0].SourceVersion = "invoice:v5"; break;
            case "projection-only": proof = proof with { Allocations = [] }; break;
            case "bad-receipt": proof.Admissions[0].ResultJson = "{}"; break;
            case "unknown-kind": proof.Items[0].SourceKind = "unrecognized.v1"; break;
        }
        var result = Project(proof);
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Unknown, result.Status);
        Assert.Null(result.ScopeId);
    }

    [Fact]
    public void Future_unrecorded_collection_remains_open_and_no_bank_match_is_required_for_recorded_collection()
    {
        var planned = Proof("10");
        planned.Items[0].DueOn = new DateOnly(2027, 6, 1);
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Open, Project(planned).Status);
        var recorded = Proof("0"); // No statement, statement line or match exists in the evidence model.
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Settled, Project(recorded).Status);
    }

    [Fact]
    public void Existing_return_credit_is_verified_and_orphan_reduction_fails_closed()
    {
        var proof = Proof("0");
        var id = Guid.NewGuid();
        var item = proof.Items[0];
        var credit = new FinanceCustomerReturnCreditRow
        {
            Id = id, CompanyId = Company, SourceKind = FinanceCustomerReturnCreditRules.SourceKind,
            OriginalInvoiceId = Invoice, OriginalInvoiceVersion = item.SourceVersion,
            PartyId = Party, CompanyAccountId = Account, CurrencyId = Currency,
            AmountText = "10", AppliedAmountText = "10", AdvanceAmountText = "0",
            ResultJson = JsonSerializer.Serialize(new FinanceInvoiceAcceptanceResultV2(CommandOutcome.Succeeded,
                id, [new(item.Id, "10")], "0", false, "SENTETIK", []), Json),
        };
        proof = proof with { Allocations = [], Sources = [], Events = [], Credits = [credit],
            Reductions = [new() { CompanyId = Company, CreditId = id, OpenItemId = item.Id, ReducedAmountText = "10" }] };
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Settled, Project(proof).Status);
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Unknown,
            Project(proof with { Credits = [] }).Status);
    }

    [Fact]
    public async Task Server_actor_company_and_required_consumer_fence_fail_before_database_access()
    {
        var company = new CompanyContext(); company.Set(Company);
        await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>().Options, company);
        var reader = new FinanceSalesInvoiceSettlementReaderV1(db, company, new User());
        var context = new FinanceContext(Company, "SENTETIK", "SENTETIK-correlation", null);
        var fences = 0;
        Task Reject(CancellationToken _) { fences++; throw new UnauthorizedAccessException(); }
        Assert.Equal(ReadOutcome.Denied, (await reader.ReadAsync(context with { ActorId = "other" }, [Invoice], Reject)).Outcome);
        Assert.Equal(ReadOutcome.Denied, (await reader.ReadAsync(context with { CompanyId = Guid.NewGuid() }, [Invoice], Reject)).Outcome);
        Assert.Equal(0, fences);
        Assert.Equal(ReadOutcome.Unavailable, (await reader.ReadAsync(context, [Invoice], Reject)).Outcome);
        Assert.Equal(1, fences);
        Assert.Equal(ReadOutcome.Unavailable, (await reader.ReadAsync(context, [Invoice], null!)).Outcome);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(context, [Invoice], Reject, cancelled.Token));
    }

    [Fact]
    public void Public_response_contains_scope_and_state_without_amount_or_currency_fields()
    {
        var response = new FinanceSalesInvoiceSettlementBatchV1(Company, [Project(Proof("0"))]);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, Json));
        var row = document.RootElement.GetProperty("invoices")[0];
        Assert.Equal(new[] { "invoiceId", "status", "scopeId", "sourceVersion" },
            row.EnumerateObject().Select(x => x.Name));
    }

    private static FinanceSalesInvoiceSettlementV1 Project(FinanceSalesInvoiceSettlementReaderV1.Snapshot proof) =>
        FinanceSalesInvoiceSettlementReaderV1.Project(Company, Invoice, proof);

    private static FinanceSalesInvoiceSettlementReaderV1.Snapshot Proof(params string[] outstanding)
    {
        var items = outstanding.Select(value => new FinanceOpenItemRow
        {
            Id = Guid.NewGuid(), CompanyId = Company, SourceId = Invoice, SourceLineId = Guid.NewGuid(),
            SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
            Direction = FinanceOpenItemDirectionCodes.Receivable, SourceBatchId = Batch,
            SourceVersion = "invoice:v4", SourceBatchVersion = "invoice:v4", SourceBatchChecksum = new string('a', 64),
            PartyId = Party, CompanyAccountId = Account, CurrencyId = Currency, OriginalAmountText = "10",
            OutstandingAmountText = value, Status = value == "0" ? FinanceOpenItemStatuses.Closed : FinanceOpenItemStatuses.Open,
            Version = 2,
        }).ToArray();
        var sources = items.Where(x => x.Status == FinanceOpenItemStatuses.Closed).Select(_ => new ReceiptDraftRow
        {
            Id = Guid.NewGuid(), CompanyId = Company, PartyId = Party, CompanyAccountId = Account,
            CurrencyId = Currency, AmountText = "10", Stage = ReceiptStages.Recorded, RecordedAtUtc = Now, RecordedBy = "SENTETIK",
        }).ToArray();
        var closed = items.Where(x => x.Status == FinanceOpenItemStatuses.Closed).ToArray();
        var allocations = sources.Select((source, index) => new FinanceAllocationRow
        {
            Id = Guid.NewGuid(), CompanyId = Company, SourceId = source.Id, OpenItemId = closed[index].Id,
            CurrencyId = Currency, AmountText = "10", Status = FinanceAllocationStatuses.Active, Version = 1,
        }).ToArray();
        var events = sources.Select(source => new MoneyEventRow
        {
            Id = Guid.NewGuid(), CompanyId = Company, SourceId = source.Id, Kind = MoneyEventKinds.ManualBankReceiptRecorded,
            PartyId = Party, CompanyAccountId = Account, CurrencyId = Currency, AmountText = "10", RecordedAtUtc = Now, RecordedBy = "SENTETIK",
        }).ToArray();
        var admission = new FinanceOpenItemSourceBatchRow
        {
            Id = Batch, CompanyId = Company, SourceKind = items[0].SourceKind, SourceVersion = "invoice:v4",
            SourceChecksum = new string('a', 64), PayloadHash = new string('a', 64),
            ResultJson = JsonSerializer.Serialize(new FinanceOpenItemAdmissionResult(CommandOutcome.Succeeded,
                items.Select(x => x.Id).ToArray(), false, "SENTETIK", []), Json),
        };
        return new(items, [admission], allocations, sources, events, [], [], [], []);
    }

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "SENTETIK";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
