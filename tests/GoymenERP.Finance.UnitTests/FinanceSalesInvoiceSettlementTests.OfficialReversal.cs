using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinanceSalesInvoiceSettlementTests
{
    [Fact]
    public void A_complete_native_official_reversal_is_distinct_from_payment_settlement_and_preserves_admitted_scope()
    {
        var result = Project(ReversedProof());
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.OfficiallyReversed, result.Status);
        Assert.NotEqual(FinanceSalesInvoiceSettlementStatusV1.Settled, result.Status);
        Assert.Equal((Batch, "invoice:v4"), (result.ScopeId, result.SourceVersion));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Valid_previously_released_or_confirmed_cancellation_allocations_remain_reversed_without_becoming_settled(bool inCancellation)
    {
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.OfficiallyReversed,
            Project(ReversedProof(releasedAllocation: true, inCancellation)).Status);
    }

    [Theory]
    [InlineData("missing-cancellation")]
    [InlineData("missing-reversals")]
    [InlineData("partial-reversals")]
    [InlineData("duplicate-reversal")]
    [InlineData("extra-reversal")]
    [InlineData("foreign-reversal")]
    [InlineData("other-cancellation-reversal")]
    [InlineData("partial-amount")]
    [InlineData("wrong-cancellation-company")]
    [InlineData("wrong-kind")]
    [InlineData("wrong-version")]
    [InlineData("wrong-item-version")]
    [InlineData("wrong-item-company")]
    [InlineData("wrong-batch")]
    [InlineData("nonzero-outstanding")]
    [InlineData("closed-instead-of-reversed")]
    [InlineData("projection-without-original")]
    [InlineData("missing-admission")]
    [InlineData("bad-native-payload")]
    [InlineData("bad-native-receipt")]
    [InlineData("partial-native-receipt")]
    [InlineData("bad-original-correlation")]
    [InlineData("bad-item-change-metadata")]
    [InlineData("return-credit")]
    [InlineData("active-allocation")]
    [InlineData("missing-release-metadata")]
    [InlineData("release-after-cancellation")]
    [InlineData("unconfirmed-release-in-cancellation")]
    [InlineData("missing-released-source-state")]
    [InlineData("inconsistent-released-source-state")]
    [InlineData("excess-released-allocation")]
    [InlineData("foreign-released-source-assignment")]
    public void Partial_legacy_or_contradictory_reversal_evidence_remains_unknown(string fault)
    {
        var proof = ReversedProof(releasedAllocation: fault.Contains("allocation", StringComparison.Ordinal)
            || fault.Contains("release", StringComparison.Ordinal));
        var cancellation = proof.Cancellations[0];
        var row = proof.Items[0];
        switch (fault)
        {
            case "missing-cancellation": proof = proof with { Cancellations = [] }; break;
            case "missing-reversals": proof = proof with { CancellationItems = null }; break;
            case "partial-reversals": proof = proof with { CancellationItems = [proof.CancellationItems![0]] }; break;
            case "duplicate-reversal": proof = proof with { CancellationItems = [proof.CancellationItems![0], proof.CancellationItems[0]] }; break;
            case "extra-reversal": proof = proof with { CancellationItems = [.. proof.CancellationItems!, new() { CompanyId = Company,
                CancellationId = cancellation.Id, OpenItemId = Guid.NewGuid(), ReversedAmountText = "10" }] }; break;
            case "foreign-reversal": proof.CancellationItems![0].CompanyId = Guid.NewGuid(); break;
            case "other-cancellation-reversal": proof.CancellationItems![0].CancellationId = Guid.NewGuid(); break;
            case "partial-amount": proof.CancellationItems![0].ReversedAmountText = "9"; break;
            case "wrong-cancellation-company": cancellation.CompanyId = Guid.NewGuid(); break;
            case "wrong-kind": cancellation.SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialIncomingInvoiceInstallment; break;
            case "wrong-version": cancellation.InvoiceVersion = "invoice:v5"; break;
            case "wrong-item-version": row.SourceVersion = "invoice:v5"; break;
            case "wrong-item-company": row.CompanyId = Guid.NewGuid(); break;
            case "wrong-batch": row.SourceBatchId = Guid.NewGuid(); break;
            case "nonzero-outstanding": row.OutstandingAmountText = "1"; break;
            case "closed-instead-of-reversed": row.Status = FinanceOpenItemStatuses.Closed; break;
            case "projection-without-original": row.OriginalAmountText = "0"; break;
            case "missing-admission": proof = proof with { Admissions = [] }; break;
            case "bad-native-payload": cancellation.PayloadHash = new string('e', 64); break;
            case "bad-native-receipt": cancellation.ResultJson = "{}"; break;
            case "partial-native-receipt": cancellation.ResultJson = JsonSerializer.Serialize(new FinanceInvoiceOpenItemCancellationResultV1(
                CommandOutcome.Succeeded, [row.Id], false, cancellation.CorrelationId, []), Json); break;
            case "bad-original-correlation": cancellation.CorrelationId = "SENTETIK-other"; break;
            case "bad-item-change-metadata": row.UpdatedBy = "SENTETIK-other"; break;
            case "return-credit": proof = proof with { Credits = [new() { OriginalInvoiceId = Invoice }] }; break;
            case "active-allocation": proof.Allocations[0].Status = FinanceAllocationStatuses.Active; break;
            case "missing-release-metadata": proof.Allocations[0].ReleasedBy = null; break;
            case "release-after-cancellation": proof.Allocations[0].ReleasedAtUtc = cancellation.CancelledAtUtc.AddSeconds(1); break;
            case "unconfirmed-release-in-cancellation": cancellation.PayloadHash = FinanceInvoiceOpenItemCancellationService.PayloadHash(
                new(cancellation.Id, FinanceInvoiceProducerV1.Sales, Invoice, cancellation.InvoiceVersion, cancellation.Reason)); break;
            case "missing-released-source-state": proof = proof with { States = [] }; break;
            case "inconsistent-released-source-state": proof.States[0].UnallocatedAmountText = "0"; break;
            case "excess-released-allocation": proof.Allocations[0].AmountText = "11"; break;
            case "foreign-released-source-assignment": proof = proof with { Assignments = [new()
                { CompanyId = Guid.NewGuid(), Kind = FinanceBalanceAssignmentKinds.Receipt, SourceId = proof.Sources[0].Id, AvailableAmountText = "0" }] }; break;
        }
        var result = Project(proof);
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Unknown, result.Status);
        Assert.Null(result.ScopeId); Assert.Null(result.SourceVersion);
    }

    private static FinanceSalesInvoiceSettlementReaderV1.Snapshot ReversedProof(bool releasedAllocation = false, bool inCancellation = true)
    {
        var proof = releasedAllocation ? Proof("0", "10") : Proof("10", "10");
        var now = Now.AddDays(1);
        var native = new FinanceInvoiceOpenItemCancellationV1(Guid.NewGuid(), FinanceInvoiceProducerV1.Sales, Invoice,
            "invoice:v4", "SENTETIK reversal", releasedAllocation && inCancellation);
        var cancellation = new FinanceInvoiceCancellationRow
        {
            Id = native.CancellationId, CompanyId = Company, SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
            InvoiceId = Invoice, InvoiceVersion = native.InvoiceVersion, Reason = native.Reason, ActorId = "SENTETIK-canceller",
            IdempotencyKeyHash = new string('c', 64), PayloadHash = FinanceInvoiceOpenItemCancellationService.PayloadHash(native),
            CorrelationId = "SENTETIK-cancellation", CancelledAtUtc = now,
            ResultJson = JsonSerializer.Serialize(new FinanceInvoiceOpenItemCancellationResultV1(CommandOutcome.Succeeded,
                proof.Items.Select(x => x.Id).ToArray(), false, "SENTETIK-cancellation", []), Json),
        };
        proof.Admissions[0].ActorId = "SENTETIK-admitter";
        proof.Admissions[0].CorrelationId = "SENTETIK";
        proof.Admissions[0].IdempotencyKeyHash = new string('b', 64);
        proof.Admissions[0].AdmittedAtUtc = Now;
        foreach (var item in proof.Items)
        {
            item.Status = FinanceOpenItemStatuses.Cancelled; item.OutstandingAmountText = "0"; item.Version++;
            item.UpdatedBy = cancellation.ActorId; item.UpdatedAtUtc = now;
        }
        foreach (var allocation in proof.Allocations)
        {
            allocation.Status = FinanceAllocationStatuses.Released; allocation.Version = 2; allocation.CreatedAtUtc = Now;
            allocation.ReleasedAtUtc = inCancellation ? now : now.AddHours(-1);
            allocation.ReleasedBy = cancellation.ActorId; allocation.ReleaseReason = cancellation.Reason;
        }
        foreach (var state in proof.States) { state.UnallocatedAmountText = "10"; state.Version++; }
        return proof with { Cancellations = [cancellation], CancellationItems = proof.Items.Select(item => new FinanceInvoiceCancellationItemRow
            { CompanyId = Company, CancellationId = cancellation.Id, OpenItemId = item.Id, ReversedAmountText = item.OriginalAmountText }).ToArray() };
    }
}
