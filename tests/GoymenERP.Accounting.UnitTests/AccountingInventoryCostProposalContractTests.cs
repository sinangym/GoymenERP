using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingInventoryCostProposalContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void A_legacy_proposal_without_a_current_posted_journal_remains_readable_and_serializes_without_a_new_null_field()
    {
        var proposal = Source();
        var encoded = JsonSerializer.Serialize(proposal, Json);
        using var document = JsonDocument.Parse(encoded);
        Assert.False(document.RootElement.TryGetProperty("currentPostedJournalId", out _));
        Assert.False(document.RootElement.TryGetProperty("componentCancellation", out _));
        Assert.False(document.RootElement.GetProperty("component").TryGetProperty("recordedRounding", out _));
        Assert.False(document.RootElement.GetProperty("component").TryGetProperty("alreadyPostedPurchaseComponents", out _));
        Assert.False(document.RootElement.GetProperty("component").TryGetProperty("hasPostedComponents", out _));
        Assert.False(document.RootElement.GetProperty("component").TryGetProperty("claimSuccession", out _));
        Assert.Equal("100.01", document.RootElement.GetProperty("exactTlAmount").GetString());
        var restored = Assert.IsType<AccountingInventoryCostProposalV1>(JsonSerializer.Deserialize<AccountingInventoryCostProposalV1>(encoded, Json));
        Assert.Null(restored.CurrentPostedJournalId);
        Assert.Equal(proposal.ProposalId, restored.ProposalId);
        Assert.Equal(proposal.Component, restored.Component);
    }

    [Fact]
    public void Claim_succession_preserves_exact_source_and_release_proof_without_rewriting_the_prior_identity()
    {
        var proposal = Source(); var physical = new AccountingSalesPhysicalCostClaimV1(Guid.NewGuid(), Guid.NewGuid(), 1, proposal.Component.ProductId, 1m);
        var proof = new InventoryCostClaimSuccessionV1(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3,
            Guid.NewGuid(), 1, new string('c', 64), Guid.NewGuid(), 0,
            [new(physical, 2, new string('d', 64))], [], [], new("1", "3"), new("1", "3"), new("1", "3"));
        var source = proposal with { Component = proposal.Component with { SignedTlAmount = new("1", "3"),
            SalesPhysicalClaims = [physical], ClaimSuccession = proof } };
        var json = JsonSerializer.Serialize(source, Json);
        var restored = Assert.IsType<AccountingInventoryCostProposalV1>(JsonSerializer.Deserialize<AccountingInventoryCostProposalV1>(json, Json));
        Assert.Equal(source.InvoiceId, restored.InvoiceId);
        Assert.Equal(proof.PreviousInvoiceId, restored.Component.ClaimSuccession!.PreviousInvoiceId);
        Assert.NotEqual(restored.InvoiceId, restored.Component.ClaimSuccession.PreviousInvoiceId);
        Assert.Equal(proof.RestoredAmount, restored.Component.SignedTlAmount);
        Assert.Equal(2, Assert.Single(restored.Component.ClaimSuccession.Claims).PreviousGeneration);
        Assert.Equal(json, JsonSerializer.Serialize(restored, Json));
    }

    [Fact]
    public void A_current_correction_journal_is_additive_proof_and_never_replaces_the_original_posted_journal_identity()
    {
        var originalJournal = Guid.NewGuid(); var correctedJournal = Guid.NewGuid();
        var proposal = Source() with { JournalId = originalJournal, CurrentPostedJournalId = correctedJournal };
        var encoded = JsonSerializer.Serialize(proposal, Json);
        using var document = JsonDocument.Parse(encoded);
        Assert.Equal(originalJournal, document.RootElement.GetProperty("journalId").GetGuid());
        Assert.Equal(correctedJournal, document.RootElement.GetProperty("currentPostedJournalId").GetGuid());
        var restored = Assert.IsType<AccountingInventoryCostProposalV1>(JsonSerializer.Deserialize<AccountingInventoryCostProposalV1>(encoded, Json));
        Assert.Equal(originalJournal, restored.JournalId);
        Assert.Equal(correctedJournal, restored.CurrentPostedJournalId);
    }

    private static AccountingInventoryCostProposalV1 Source()
    {
        var line = Guid.NewGuid();
        return new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), AccountingInventoryCostKindV1.SalesInvoicedShipment,
            Guid.NewGuid(), 3, Guid.NewGuid(), new(2026, 11, 17), "SENTETIK-COST-CONTRACT",
            new(AccountingInventoryCostTraceV1.SalesComponent(line), Guid.NewGuid(), line, 1, null,
                new("10001", "100"), "{\"syntheticOwnerProof\":true}", new string('a', 64)),
            1, "AwaitingPreparation", new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
    }
}
