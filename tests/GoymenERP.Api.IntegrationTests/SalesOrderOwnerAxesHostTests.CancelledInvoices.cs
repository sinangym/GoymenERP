using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesOrderOwnerAxesHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelled_original_requires_official_reversal_while_active_replacement_requires_settlement(bool subcontract)
    {
        var f = subcontract ? SubcontractFixture() : new Fixture();
        var proof = CancelledProof(f, subcontract);
        f.CancelledProofs = [proof];
        f.SettlementOverride = id => id == proof.InvoiceId
            ? new(id, FinanceSalesInvoiceSettlementStatusV1.OfficiallyReversed, proof.AdmissionId, proof.OriginalFinalizedVersion.ToString())
            : new(id, FinanceSalesInvoiceSettlementStatusV1.Settled, f.SubcontractInvoice?.Finalization?.AdmissionId ?? f.Admission,
                f.SubcontractInvoice?.Finalization?.FinalizedVersion.ToString() ?? "4");
        var answer = await f.Host().ReadOrderOwnerAxesAsync(f.Query with { IsSubcontractService = subcontract }, f.Fence);
        Assert.True(answer!.Source.Complete);
        Assert.True(answer.Invoicing.Complete);
        Assert.True(answer.FinancialClosure.Complete);
        Assert.Equal(new[] { f.Invoice, proof.InvoiceId }.Order(), f.FinanceIds.Order());
    }

    [Theory]
    [InlineData(false, "settled-cancellation")]
    [InlineData(true, "settled-cancellation")]
    [InlineData(false, "open-cancellation")]
    [InlineData(true, "unknown-cancellation")]
    [InlineData(false, "wrong-scope")]
    [InlineData(true, "wrong-original-version")]
    [InlineData(false, "missing-cancelled-row")]
    [InlineData(true, "reversed-active")]
    public async Task Financial_scope_never_substitutes_payment_for_reversal_or_reversal_for_active_payment(bool subcontract, string fault)
    {
        // Explicit SYNTHETIC Finance/owner responses exercise the consumer boundary; native Sales SQL proof is separate.
        var f = subcontract ? SubcontractFixture() : new Fixture();
        var proof = CancelledProof(f, subcontract); f.CancelledProofs = [proof];
        var wrongScope = Guid.NewGuid(); // Stable negative identity across the two owner observations.
        f.SettlementOverride = id => id == proof.InvoiceId
            ? new(id, fault switch { "settled-cancellation" => FinanceSalesInvoiceSettlementStatusV1.Settled,
                "open-cancellation" => FinanceSalesInvoiceSettlementStatusV1.Open,
                "unknown-cancellation" => FinanceSalesInvoiceSettlementStatusV1.Unknown,
                _ => FinanceSalesInvoiceSettlementStatusV1.OfficiallyReversed },
                fault == "wrong-scope" ? wrongScope : proof.AdmissionId,
                fault == "wrong-original-version" ? proof.CancellationInvoiceVersion.ToString() : proof.OriginalFinalizedVersion.ToString())
            : new(id, fault == "reversed-active" ? FinanceSalesInvoiceSettlementStatusV1.OfficiallyReversed : FinanceSalesInvoiceSettlementStatusV1.Settled,
                f.SubcontractInvoice?.Finalization?.AdmissionId ?? f.Admission, f.SubcontractInvoice?.Finalization?.FinalizedVersion.ToString() ?? "4");
        f.OmitCancelledSettlement = fault == "missing-cancelled-row";
        var answer = await f.Host().ReadOrderOwnerAxesAsync(f.Query with { IsSubcontractService = subcontract }, f.Fence);
        Assert.True(answer!.Source.Complete);
        Assert.True(answer.Invoicing.Complete);
        Assert.False(answer.FinancialClosure.Complete);
        Assert.Equal(SalesDraftStatusAxisStatesV1.Unreadable, answer.FinancialClosure.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changing_Finance_cancellation_proof_invalidates_all_three_axes(bool subcontract)
    {
        var f = subcontract ? SubcontractFixture() : new Fixture();
        var proof = CancelledProof(f, subcontract); f.CancelledProofs = [proof];
        f.SettlementOverride = id => id == proof.InvoiceId
            ? new(id, FinanceSalesInvoiceSettlementStatusV1.OfficiallyReversed, Guid.NewGuid(), proof.OriginalFinalizedVersion.ToString())
            : new(id, FinanceSalesInvoiceSettlementStatusV1.Settled, f.SubcontractInvoice?.Finalization?.AdmissionId ?? f.Admission,
                f.SubcontractInvoice?.Finalization?.FinalizedVersion.ToString() ?? "4");
        var answer = await f.Host().ReadOrderOwnerAxesAsync(f.Query with { IsSubcontractService = subcontract }, f.Fence);
        Assert.Equal(2, f.FinanceCalls);
        Assert.Equal(SalesDraftStatusAxisStatesV1.Unreadable, answer!.Source.State);
        Assert.Equal(SalesDraftStatusAxisStatesV1.Unreadable, answer.Invoicing.State);
        Assert.Equal(SalesDraftStatusAxisStatesV1.Unreadable, answer.FinancialClosure.State);
        Assert.False(answer.Source.Complete);
        Assert.False(answer.Invoicing.Complete);
        Assert.False(answer.FinancialClosure.Complete);
    }

    [Theory]
    [InlineData("foreign-company")]
    [InlineData("missing-event")]
    [InlineData("bad-hash")]
    [InlineData("active-id")]
    [InlineData("duplicate")]
    public async Task Missing_or_malformed_native_cancellation_history_never_queries_Finance_as_complete(string fault)
    {
        var f = new Fixture(); var proof = CancelledProof(f, false);
        proof = fault switch { "foreign-company" => proof with { CompanyId = Guid.NewGuid() },
            "missing-event" => proof with { CancellationEventId = Guid.Empty },
            "bad-hash" => proof with { CancellationPayloadSha256 = "unknown" },
            "active-id" => proof with { InvoiceId = f.Invoice }, _ => proof };
        f.CancelledProofs = fault == "duplicate" ? [proof, proof] : [proof];
        var answer = await f.Read();
        Assert.False(answer!.FinancialClosure.Complete);
        Assert.Equal(0, f.FinanceCalls);
    }

    private static SalesOrderCancelledInvoiceProofV1 CancelledProof(Fixture f, bool subcontract) =>
        new(f.CompanyId, f.OtherInvoice, subcontract ? "Subcontract" : "Normal", Guid.NewGuid(), 4, 5,
            Guid.NewGuid(), new string('a', 64), Guid.NewGuid(), Guid.NewGuid(), new string('b', 64), DateTimeOffset.UnixEpoch);

    private sealed partial class Fixture
    {
        public SalesOrderCancelledInvoiceProofV1[]? CancelledProofs;
        public Func<Guid, FinanceSalesInvoiceSettlementV1>? SettlementOverride;
        public bool OmitCancelledSettlement;
    }
}
